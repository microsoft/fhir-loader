using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Polly.Wrap;
using Polly;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using Polly.CircuitBreaker;
using System.Diagnostics;
using Newtonsoft.Json;
using Polly.Timeout;
using Polly.Retry;

namespace FhirLoader.Common
{
    /// <summary>
    /// FHIR version agnostic client for sending bundles
    /// </summary>
    public class BundleClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly ILogger _logger;
        private readonly string? _tenantId;
        AsyncPolicyWrap<HttpResponseMessage> _resiliencyStrategy;

        // Used to get/refresh access token across threads
        static SemaphoreSlim _tokenSemaphore = new SemaphoreSlim(1, 1);
        private DateTime _tokenGeneratedDateTime = DateTime.MinValue;

        const string METADATA_SUFFIX = "/metadata";

        public BundleClient(string baseUrl, ILogger logger, string? tenantId = null, AsyncPolicyWrap<HttpResponseMessage>? resiliencyStrategy = null, CancellationToken cancel = default)
        {
            _logger = logger;
            _client = new HttpClient();
            _tenantId = tenantId;

            if (baseUrl.EndsWith(METADATA_SUFFIX))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - METADATA_SUFFIX.Length);

            _client.BaseAddress = new Uri(baseUrl);
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Accept.Clear();

            _resiliencyStrategy = resiliencyStrategy ?? CreateDefaultResiliencyStrategy();
        }

        public async Task PrefetchToken(CancellationToken cancel = default)
        {
            await SetAccessTokenAsync(cancel);
        }

        public async Task Send(ProcessedBundle bundle, Action<int, long>? metricsCallback = null, CancellationToken cancel = default)
        {
            var content = new StringContent(bundle.BundleText!, Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            var timer = new Stopwatch();
            int perBundleFailCount = 0;

            while (true)
            {
                timer.Start();

                try
                {
                    _logger.LogTrace($"Sending {bundle.BundleCount} resources to {_client.BaseAddress}...");

                    var strategyWithTokenRefresh = _resiliencyStrategy.WrapAsync(CreateTokenRefreshPolicy());

                    response = await strategyWithTokenRefresh.ExecuteAsync(
                        async ct => await _client.PostAsync("", content, ct),
                        cancellationToken: cancel
                    );
                }
                catch (TaskCanceledException tcex)
                {
                    throw tcex;
                }
                catch (BrokenCircuitException bce)
                {
                    throw new FatalBundleClientException($"Could not contact the FHIR Endpoint due to the following error: {bce.Message}", bce);
                }
                catch (TimeoutRejectedException ex)
                {
                    _logger.LogWarning("Maximum client timeout reached, delaying and retrying...");
                    await Task.Delay(30000);
                    continue;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning("Network error encountered, delaying and retrying...");
                    await Task.Delay(30000);
                    continue;
                }
                catch (Exception ex)
                {
                    throw new FatalBundleClientException($"Critical error: {ex.Message}", ex);
                }

                timer.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Could not send bundle due to error from server: {response.StatusCode}. Adding request back to queue...");
                    var responseString = await response.Content.ReadAsStringAsync() ?? "{}";
                    try
                    {
                        var responseObject = JObject.Parse(responseString);
                        _logger.LogError(responseObject.ToString(Formatting.Indented));
                    }
                    catch (JsonReaderException)
                    {
                        _logger.LogError(responseString);
                    }

                    perBundleFailCount++;

                    if (perBundleFailCount >= 3)
                        throw new FatalBundleClientException("Single bundle failed for 3 consecutive attempts.");

                    await Task.Delay(30000);
                    continue;
                }

                break;
            }

            if (metricsCallback is not null)
                metricsCallback(bundle.BundleCount, timer.ElapsedMilliseconds);

            _logger.LogTrace("Successfully sent bundle.");
        }

        private async Task<AccessToken> SetAccessTokenAsync(CancellationToken cancel = default)
        {
            _logger.LogInformation($"Attempting to get access token for {_client.BaseAddress}...");

            string[] scopes = new string[] { $"{_client.BaseAddress}/.default" };
            TokenRequestContext tokenRequestContext = new TokenRequestContext(scopes: scopes, tenantId: _tenantId);
            var credential = new DefaultAzureCredential(true);

            var token = await credential.GetTokenAsync(tokenRequestContext, cancel);

            _client.DefaultRequestHeaders.Remove("Authorization");
            _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.Token}");
            _tokenGeneratedDateTime = DateTime.UtcNow;

            _logger.LogInformation($"Got token for FHIR server {_client.BaseAddress}!");
            return token;
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        public AsyncPolicyWrap<HttpResponseMessage> CreateDefaultResiliencyStrategy()
        {
            var rnd = new Random();

            // Retry when these status codes are encountered.
            HttpStatusCode[] httpStatusCodesWorthRetrying = {
               HttpStatusCode.InternalServerError, // 500
               HttpStatusCode.BadGateway, // 502
               HttpStatusCode.GatewayTimeout, // 504
            };

            // Define our waitAndRetry policy: retry n times with an exponential backoff in case the FHIR API throttles us for too many requests.
            var waitAndRetryPolicy = Policy
                .HandleResult<HttpResponseMessage>(e => e.StatusCode == HttpStatusCode.ServiceUnavailable || e.StatusCode == (HttpStatusCode)429 || e.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(5, // Retry 5 times with a delay between retries before ultimately giving up
                    attempt => TimeSpan.FromMilliseconds((500 * rnd.Next(8)) * Math.Pow(2, attempt)), // Back off!  2, 4, 8, 16 etc times 2 seconds plus a random number
                                                                                                      //attempt => TimeSpan.FromSeconds(6), // Wait 6 seconds between retries
                    (exception, calculatedWaitDuration) =>
                    {
                        _logger.LogWarning($"FHIR API server throttling our requests. Automatically delaying for {calculatedWaitDuration.TotalMilliseconds / 1000} seconds");
                    }
                );

            // Define our first CircuitBreaker policy: Break if the action fails 5 times in a row.
            // This is designed to handle Exceptions from the FHIR API, as well as
            // a number of recoverable status messages, such as 500, 502, and 504.
            var circuitBreakerPolicyForRecoverable = Policy
                .Handle<HttpRequestException>()
                .Or<TimeoutRejectedException>()
                .OrResult<HttpResponseMessage>(r => httpStatusCodesWorthRetrying.Contains(r.StatusCode))
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 6,
                    durationOfBreak: TimeSpan.FromSeconds(60),
                    onBreak: (outcome, breakDelay) =>
                    {
                        _logger.LogWarning($"Polly Circuit Breaker logging: Breaking the circuit for {breakDelay.TotalMilliseconds}ms due to response {outcome.Result.ToString()}. More Details: {outcome.Exception?.Message}");
                    },
                    onReset: () => _logger.LogWarning("Polly Circuit Breaker logging: Call ok... closed the circuit again"),
                    onHalfOpen: () => _logger.LogWarning("Polly Circuit Breaker logging: Half-open: Next call is a trial")
                );

            // Timeout before HttpClient timeout of 100ms
            var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(
                TimeSpan.FromSeconds(95),
                Polly.Timeout.TimeoutStrategy.Optimistic
            );

            var circuitBreakerWrappingTimeout = circuitBreakerPolicyForRecoverable.
                WrapAsync(timeoutPolicy);

            return Policy.WrapAsync(waitAndRetryPolicy, circuitBreakerWrappingTimeout);
        }

        private AsyncRetryPolicy<HttpResponseMessage> CreateTokenRefreshPolicy()
        {
            var policy = Policy
                .HandleResult<HttpResponseMessage>(message => message.StatusCode == HttpStatusCode.Unauthorized)
                .RetryAsync(1, async (result, retryCount, context) =>
                {
                    await _tokenSemaphore.WaitAsync();
                    try
                    {
                        if (DateTime.UtcNow > _tokenGeneratedDateTime.AddSeconds(100))
                        {
                            // #TODO - add cancel token
                            await SetAccessTokenAsync();
                        }
                    }
                    finally
                    {
                        _tokenSemaphore.Release();
                    }
                });

            return policy;
        }
    }

    public class FatalBundleClientException : Exception
    {
        public FatalBundleClientException(string message) : base(message) { }
        public FatalBundleClientException(string message, Exception inner) : base(message, inner) { }
    }
}
