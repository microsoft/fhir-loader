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


namespace FhirLoader.Common
{
    /// <summary>
    /// FHIR version agnostic client for sending bundles
    /// </summary>
    public class FhirResourceClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly ILogger _logger;
        private readonly string? _tenantId;
        AsyncPolicyWrap<HttpResponseMessage> _resiliencyStrategy;

        const string METADATA_SUFFIX = "/metadata";

        public FhirResourceClient(string baseUrl, ILogger logger, string? tenantId = null, AsyncPolicyWrap<HttpResponseMessage>? resiliencyStrategy = null)
        {
            _logger = logger;
            _client = new HttpClient();
            _tenantId = tenantId;

            if (baseUrl.EndsWith(METADATA_SUFFIX))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - METADATA_SUFFIX.Length);

            _client.BaseAddress = new Uri(baseUrl);
            var accessToken = FetchToken(_tenantId);
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken.Token}");

            _resiliencyStrategy = resiliencyStrategy ?? DefaultResiliencyStrategy();
        }

        public async Task Send(ProcessedResource bundle, Action<int, long>? metricsCallback = null, CancellationToken? cancel = null)
        {
            var content = new StringContent(bundle.ResourceText!, Encoding.UTF8, "application/json");
            HttpResponseMessage response;

            var requestUri = !string.IsNullOrEmpty(bundle.ResourceType) ? $"/{bundle.ResourceType}" : string.Empty;

            var timer = new Stopwatch();
            timer.Start();

            try
            {
                _logger.LogTrace($"Sending {bundle.ResourceCount} resources to {_client.BaseAddress}...");

                response = await _resiliencyStrategy.ExecuteAsync(
                    async ct => await _client.PostAsync(requestUri, content, ct),
                    cancellationToken: cancel ?? CancellationToken.None
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
            catch (Exception e)
            {
                throw new FatalBundleClientException($"Critical error: {e.Message}", e);
            }

            timer.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Could not send bundle due to error from server: {response.StatusCode}");
                var responseString = await response.Content.ReadAsStringAsync() ?? "{}";
                try
                {
                    _logger.LogError(JObject.Parse(responseString).ToString(Formatting.Indented));
                }
                catch (JsonReaderException)
                {
                    _logger.LogError(responseString);
                }
            }
            else
            {
                if (metricsCallback is not null)
                    metricsCallback(bundle.ResourceCount, timer.ElapsedMilliseconds);

                _logger.LogTrace("Successfully sent bundle.");
            }
        }

        private AccessToken FetchToken(string? tenantId = null)
        {
            _logger.LogInformation($"Attempting to get access token for {_client.BaseAddress}...");

            string[] scopes = new string[] { $"{_client.BaseAddress}/.default" };
            TokenRequestContext tokenRequestContext = new TokenRequestContext(scopes: scopes, tenantId: tenantId);
            var credential = new DefaultAzureCredential(true);
            var token = credential.GetToken(tokenRequestContext);

            _logger.LogInformation($"Got token for FHIR server {_client.BaseAddress}!");
            return token;
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        public AsyncPolicyWrap<HttpResponseMessage> DefaultResiliencyStrategy()
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

            // Define our first CircuitBreaker policy: Break if the action fails 4 times in a row.
            // This is designed to handle Exceptions from the FHIR API, as well as
            // a number of recoverable status messages, such as 500, 502, and 504.
            var circuitBreakerPolicyForRecoverable = Policy
                .Handle<HttpRequestException>()
                .Or<TimeoutRejectedException>()
                .OrResult<HttpResponseMessage>(r => httpStatusCodesWorthRetrying.Contains(r.StatusCode))
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 3,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (outcome, breakDelay) =>
                    {
                        _logger.LogWarning($"Polly Circuit Breaker logging: Breaking the circuit for {breakDelay.TotalMilliseconds}ms due to: {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
                    },
                    onReset: () => _logger.LogWarning("Polly Circuit Breaker logging: Call ok... closed the circuit again"),
                    onHalfOpen: () => _logger.LogWarning("Polly Circuit Breaker logging: Half-open: Next call is a trial")
                );

            var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(
                TimeSpan.FromSeconds(60),
                Polly.Timeout.TimeoutStrategy.Optimistic
            );

            var circuitBreakerWrappingTimeout = circuitBreakerPolicyForRecoverable.
                WrapAsync(timeoutPolicy);

            return Policy.WrapAsync(waitAndRetryPolicy, circuitBreakerWrappingTimeout);
        }
    }

    public class FatalBundleClientException : Exception
    {
        public FatalBundleClientException(string message) : base(message) { }
        public FatalBundleClientException(string message, Exception inner) : base(message, inner) { }
    }
}
