// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using System.Text;
using Azure.Core;
using Azure.Identity;
using FhirLoader.CommandLineTool.FileType;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using Polly.Wrap;

namespace FhirLoader.CommandLineTool.Client
{
    /// <summary>
    /// FHIR version agnostic client for sending FHIR Resources (bundles or plain resources).
    /// </summary>
    public class FhirResourceClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly bool _skipErrors;
        private readonly ILogger _logger;
        private readonly string? _tenantId;
        private readonly string? _audience;
        private readonly AsyncPolicyWrap<HttpResponseMessage> _resiliencyStrategy;

        // Used to get/refresh access token across threads
        private static readonly SemaphoreSlim s_tokenSemaphore = new(1, 1);
        private DateTime _tokenGeneratedDateTime = DateTime.MinValue;
        private const string ReindexSuffix = "/$reindex";

        public FhirResourceClient(Uri baseUrl, int expectedParallelRequests, bool skipErrors, ILogger logger, string? tenantId = null, string? audience = null, CancellationToken cancel = default)
        {
            if (baseUrl is null)
            {
                throw new ArgumentNullException(nameof(baseUrl));
            }

            _logger = logger;
            _skipErrors = skipErrors;
            _client = new HttpClient();
            _tenantId = tenantId;
            _audience = audience;

            // Ensure that the base URL is only the scheme and authority - not metadata if the user copied the URL from the Azure Portal.
            _client.BaseAddress = new Uri(baseUrl.GetLeftPart(UriPartial.Authority));
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Accept.Clear();

            _resiliencyStrategy = CreateDefaultResiliencyStrategy(expectedParallelRequests);

            SetAccessTokenAsync(cancel).GetAwaiter().GetResult();
        }

        public async Task Send(BaseProcessedResource processedResource, Action<int, long>? metricsCallback = null, CancellationToken cancel = default)
        {
            if (processedResource is null)
            {
                throw new ArgumentNullException(nameof(processedResource));
            }

            var content = new StringContent(processedResource.ResourceText!, Encoding.UTF8, "application/json");
            HttpResponseMessage response = new();

            var requestUri = processedResource.IsBundle ? string.Empty : !string.IsNullOrEmpty(processedResource.ResourceId) ? $"/{processedResource.ResourceType}/{processedResource.ResourceId}" : $"/{processedResource.ResourceType}";

            var timer = new Stopwatch();
            int perFileFailedCount = 0;

            while (true)
            {
                timer.Start();

                try
                {
                    _logger.LogTrace($"Sending resource type {processedResource.ResourceType} having {processedResource.ResourceCount} resources to {_client.BaseAddress}...");

                    if (!string.IsNullOrEmpty(processedResource.ResourceId) && !processedResource.IsBundle)
                    {
                        response = await _resiliencyStrategy.ExecuteAsync(
                            async ct => await _client.PutAsync(requestUri, content, ct),
                            cancellationToken: cancel);
                    }
                    else
                    {
                        response = await _resiliencyStrategy.ExecuteAsync(
                            async ct => await _client.PostAsync(requestUri, content, ct),
                            cancellationToken: cancel);
                    }
                }
                catch (TaskCanceledException tcex)
                {
                    throw tcex;
                }
                catch (BrokenCircuitException bce)
                {
                    throw new FatalFhirResourceClientException($"Could not contact the FHIR Endpoint due to the following error: {bce.Message}", bce, null);
                }
                catch (TimeoutRejectedException)
                {
                    _logger.LogWarning("Maximum client timeout reached, delaying and retrying...");
                    await Task.Delay(30000, cancel);
                    continue;
                }
                catch (HttpRequestException)
                {
                    _logger.LogWarning("Network error encountered, delaying and retrying...");
                    await Task.Delay(30000, cancel);
                    continue;
                }
                catch (Exception e)
                {
                    throw new FatalFhirResourceClientException($"Critical error: {e.Message}", e, null);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync(cancel) ?? "{}";

                    // Stopgap for duplicate search parameters. Really, the class calling this one should filter the search parameters by what already exists on the server.
                    if (
                        (responseString.Contains("SearchParameter", StringComparison.OrdinalIgnoreCase) &&
                        responseString.Contains("already exists", StringComparison.OrdinalIgnoreCase)) ||
                        (responseString.Contains("custom search parameter", StringComparison.OrdinalIgnoreCase) &&
                        responseString.Contains("An item with the same key has already been added.", StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogInformation("Search parameter resource already exists on the server...skipping...");
                        timer.Stop();
                        break;
                    }

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _logger.LogWarning($"Token rejected as unauthorized. Inspect token against your FHIR Service configuration and try again. {_client.DefaultRequestHeaders.Authorization?.Parameter}.");
                    }

                    _logger.LogError($"Could not send resource(s) due to error from server: {response.StatusCode}. Adding request back to queue...");

                    perFileFailedCount++;

                    try
                    {
                        var responseObject = JObject.Parse(responseString);

                        if (perFileFailedCount >= 3)
                        {
                            if (_skipErrors)
                            {
                                _logger.LogWarning("Could not send bundle because of code {Code}. Skipping...", response.StatusCode);
                                break;
                            }

                            throw new FatalFhirResourceClientException("Single bundle failed for 3 consecutive attempts.", response.StatusCode);
                        }

                        _logger.LogError(responseObject.ToString(Formatting.Indented));
                    }
                    catch (JsonReaderException)
                    {
                        _logger.LogError(responseString);
                    }

                    await Task.Delay(30000, cancel);
                    continue;
                }

                timer.Stop();
                break;
            }

            if (metricsCallback is not null)
            {
                metricsCallback(processedResource.ResourceCount, timer.ElapsedMilliseconds);
            }

            _logger.LogTrace("Successfully sent bundle.");
        }

        public async Task<JObject?> Get(Uri requestUri)
        {
            HttpResponseMessage response = new();

            var timer = new Stopwatch();
            timer.Start();

            try
            {
                _logger.LogTrace("Fetching metadata.");

                response = await _resiliencyStrategy.ExecuteAsync(
                   async ct => await _client.GetAsync(requestUri, ct),
                   CancellationToken.None);
            }
            catch (TaskCanceledException tcex)
            {
                throw tcex;
            }
            catch (BrokenCircuitException bce)
            {
                throw new FatalFhirResourceClientException($"Could not contact the FHIR Endpoint due to the following error: {bce.Message}", bce);
            }
            catch (Exception e)
            {
                throw new FatalFhirResourceClientException($"Critical error: {e.Message}", e);
            }

            timer.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Could not get metadata due to error from server: {response.StatusCode}");
                var responseString = await response.Content.ReadAsStringAsync() ?? "{}";
                try
                {
                    _logger.LogError(JObject.Parse(responseString).ToString(Formatting.Indented));
                }
                catch (JsonReaderException)
                {
                    _logger.LogError(responseString);
                }

                return null;
            }
            else
            {
                _logger.LogTrace("Metadata fetch succeeded.");
                var metadata = JObject.Parse(await response.Content.ReadAsStringAsync());
                return metadata;
            }
        }

        public async Task ReIndex(CancellationToken? cancel = null)
        {
            HttpResponseMessage response;
            try
            {
                _logger.LogInformation($"Calling reindex api..");
                var content = new StringContent("{ \"resourceType\": \"Parameters\",\"parameter\": [] }", Encoding.UTF8, "application/json");
                response = await _resiliencyStrategy.ExecuteAsync(
                  async ct => await _client.PostAsync(ReindexSuffix, content, ct),
                  cancellationToken: cancel ?? CancellationToken.None);
            }
            catch (Exception e)
            {
                _logger.LogInformation($"Error while reindexing : {e.Message}");
                throw new FatalFhirResourceClientException($"Error while reindexing : {e.Message}", e);
            }

            var responseString = await response.Content.ReadAsStringAsync() ?? "{}";
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var jObject = JObject.Parse(responseString);
                    if (jObject != null && jObject.Count > 0)
                    {
                        _logger.LogError(JObject.Parse(responseString).ToString(Formatting.Indented));
                        _logger.LogInformation("Use this command to check the status of the reindex job. : az rest --resource " + _client.BaseAddress + " --url " + _client.BaseAddress + "_operations/reindex/" + jObject["id"]?.ToString());
                    }
                }
                catch (JsonReaderException ex)
                {
                    _logger.LogError($"Error while parsing reindex response : {ex.Message}", ex);
                }
            }
            else
            {
                _logger.LogInformation(responseString);
            }
        }

        private async Task<AccessToken> SetAccessTokenAsync(CancellationToken cancel = default)
        {
            DefaultAzureCredentialOptions credentialOptions = new();
            credentialOptions.AdditionallyAllowedTenants.Add("*");
            DefaultAzureCredential credential = new(true);

            string[] scopes = new string[] { $"{_audience ?? _client.BaseAddress?.ToString()}/user_impersonation" };
            var tokenRequestContext = new TokenRequestContext(scopes: scopes, tenantId: _tenantId);

            _logger.LogInformation($"Attempting to get access token for {_client.BaseAddress} with scopes {string.Join(", ", scopes)}...");

            AccessToken token = await credential.GetTokenAsync(tokenRequestContext, cancel);

            _client.DefaultRequestHeaders.Remove("Authorization");
            _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.Token}");
            _tokenGeneratedDateTime = DateTime.UtcNow;

            _logger.LogInformation($"Got token for FHIR server {_client.BaseAddress}!");
            return token;
        }

        public void Dispose()
        {
            _client.Dispose();
            GC.SuppressFinalize(this);
        }

        public AsyncPolicyWrap<HttpResponseMessage> CreateDefaultResiliencyStrategy(int expectedParallelRequests)
        {
            var rnd = new Random();

            // Retry when these status codes are encountered.
            HttpStatusCode[] httpStatusCodesWorthRetrying =
            {
               HttpStatusCode.InternalServerError, // 500
               HttpStatusCode.BadGateway, // 502
               HttpStatusCode.GatewayTimeout, // 504
            };

            // Define our waitAndRetry policy: retry n times with an exponential backoff in case the FHIR API throttles us for too many requests.
            AsyncRetryPolicy<HttpResponseMessage> waitAndRetryPolicy = Policy
                .HandleResult<HttpResponseMessage>(e => e.StatusCode == HttpStatusCode.ServiceUnavailable || e.StatusCode == (HttpStatusCode)429 || e.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    expectedParallelRequests * 3,
                    attempt => TimeSpan.FromMilliseconds(500 * rnd.Next(8) * Math.Pow(2, attempt)),
                    (exception, calculatedWaitDuration) =>
                    {
                        _logger.LogWarning($"FHIR API server throttling our requests. Automatically delaying for {calculatedWaitDuration.TotalMilliseconds / 1000} seconds");
                    });

            // Define our first CircuitBreaker policy: Break if the action fails 5 times in a row.
            // This is designed to handle Exceptions from the FHIR API, as well as
            // a number of recoverable status messages, such as 500, 502, and 504.
            AsyncCircuitBreakerPolicy<HttpResponseMessage> circuitBreakerPolicyForRecoverable = Policy
                .Handle<HttpRequestException>()
                .Or<TimeoutRejectedException>()
                .OrResult<HttpResponseMessage>(r => httpStatusCodesWorthRetrying.Contains(r.StatusCode))
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 3 * expectedParallelRequests,
                    durationOfBreak: TimeSpan.FromSeconds(60),
                    onBreak: (outcome, breakDelay) =>
                    {
                        _logger.LogWarning($"Polly Circuit Breaker logging: Breaking the circuit for {breakDelay.TotalMilliseconds}ms due to response {outcome.Result}. More Details: {outcome.Exception?.Message}");
                    },
                    onReset: () => _logger.LogWarning("Polly Circuit Breaker logging: Call ok... closed the circuit again"),
                    onHalfOpen: () => _logger.LogWarning("Polly Circuit Breaker logging: Half-open: Next call is a trial"));

            // Timeout before HttpClient timeout of 100ms
            AsyncTimeoutPolicy<HttpResponseMessage> timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(
                TimeSpan.FromSeconds(95),
                TimeoutStrategy.Optimistic);

            AsyncPolicyWrap<HttpResponseMessage> circuitBreakerWrappingTimeout = circuitBreakerPolicyForRecoverable.
                WrapAsync(timeoutPolicy);

            AsyncRetryPolicy<HttpResponseMessage> tokenRefreshPolicy = Policy
                .HandleResult<HttpResponseMessage>(message => message.StatusCode == HttpStatusCode.Unauthorized)
                .RetryAsync(1 * expectedParallelRequests, async (result, retryCount, context) =>
                {
                    await s_tokenSemaphore.WaitAsync();
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
                        s_tokenSemaphore.Release();
                    }
                });

            return Policy.WrapAsync(waitAndRetryPolicy, circuitBreakerWrappingTimeout, tokenRefreshPolicy);
        }
    }
}
