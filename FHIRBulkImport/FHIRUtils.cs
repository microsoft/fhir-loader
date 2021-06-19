using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using System;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Polly;
using System.Net;

namespace FHIRBulkImport
{
    public static class FHIRUtils
    {
        private static object lockobj = new object();
        private static string _bearerToken = null;
        private static readonly HttpStatusCode[] httpStatusCodesWorthRetrying = {
            HttpStatusCode.RequestTimeout, // 408
            HttpStatusCode.InternalServerError, // 500
            HttpStatusCode.BadGateway, // 502
            HttpStatusCode.ServiceUnavailable, // 503
            HttpStatusCode.GatewayTimeout, // 504
            HttpStatusCode.TooManyRequests //429
        };

        private static HttpClient _fhirClient = null;
        private static void InititalizeHttpClient(ILogger log)
        {
            if (_fhirClient==null)
            {
                log.LogInformation("Initializing FHIR Client...");
                SocketsHttpHandler socketsHandler = new SocketsHttpHandler
                  {
                      ResponseDrainTimeout = TimeSpan.FromSeconds(Utils.GetIntEnvironmentVariable("FBI-POOLEDCON-RESPONSEDRAINSECS", "60")),
                      PooledConnectionLifetime = TimeSpan.FromMinutes(Utils.GetIntEnvironmentVariable("FBI-POOLEDCON-LIFETIME", "5")),
                      PooledConnectionIdleTimeout = TimeSpan.FromMinutes(Utils.GetIntEnvironmentVariable("FBI-POOLEDCON-IDLETO", "2")),
                      MaxConnectionsPerServer = Utils.GetIntEnvironmentVariable("FBI-POOLEDCON-MAXCONNECTIONS", "20")
                  };
                 _fhirClient = new HttpClient(socketsHandler);
            }
        }
        public static async System.Threading.Tasks.Task<FHIRResponse> CallFHIRServer(string path, string body, HttpMethod method, ILogger log)
        {
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FS-RESOURCE")) && ADUtils.isTokenExpired(_bearerToken))
            {
                lock (lockobj)
                {
                    
                    if (ADUtils.isTokenExpired(_bearerToken))
                    {
                        InititalizeHttpClient(log);
                        log.LogInformation("Bearer Token is expired...Obtaining new bearer token...");
                        _bearerToken = ADUtils.GetOAUTH2BearerToken(System.Environment.GetEnvironmentVariable("FS-RESOURCE"), System.Environment.GetEnvironmentVariable("FS-TENANT-NAME"),
                                                                 System.Environment.GetEnvironmentVariable("FS-CLIENT-ID"), System.Environment.GetEnvironmentVariable("FS-SECRET")).GetAwaiter().GetResult();
                    }
                }
            }
            var retryPolicy = Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(r => httpStatusCodesWorthRetrying.Contains(r.StatusCode))
                .WaitAndRetryAsync(Utils.GetIntEnvironmentVariable("FBI-POLLY-MAXRETRIES","3"), retryAttempt =>
                   TimeSpan.FromMilliseconds(Utils.GetIntEnvironmentVariable("FBI-POLLY-RETRYMS", "500")), (result, timeSpan, retryCount, context) =>
                   {
                       log.LogWarning($"FHIR Request failed. Waiting {timeSpan} before next retry. Retry attempt {retryCount}");
                    }
                );
            HttpResponseMessage _fhirResponse =
            await retryPolicy.ExecuteAsync(async () =>
            {
                HttpRequestMessage _fhirRequest;
                var fhirurl = $"{Environment.GetEnvironmentVariable("FS-URL")}/{path}";
                _fhirRequest = new HttpRequestMessage(method, fhirurl);
                _fhirRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
                _fhirRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _fhirRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return await _fhirClient.SendAsync(_fhirRequest);
                
            });
            return await FHIRResponse.FromHttpResponseMessage(_fhirResponse, log);
        }
        public static string TransformBundle(string requestBody, ILogger log)
        {
            JObject result = JObject.Parse(requestBody);
            if (result == null || result["resourceType"] == null || result["type"] == null) return requestBody;
            string rtt = result.FHIRResourceType();
            string bt = (string)result["type"];
            if (rtt.Equals("Bundle") && bt.Equals("transaction"))
            {
                log.LogInformation($"TransformBundleProcess: looks like a valid transaction bundle");
                JArray entries = (JArray)result["entry"];
                if (entries.IsNullOrEmpty()) return result.ToString();
                log.LogInformation($"TransformBundleProcess: Phase 1 searching for existing entries on FHIR Server...");
                foreach (JToken tok in entries)
                {
                    if (!tok.IsNullOrEmpty() && tok["request"]["ifNoneExist"] != null)
                    {
                        string resource = (string)tok["request"]["url"];
                        string query = (string)tok["request"]["ifNoneExist"];
                        log.LogInformation($"TransformBundleProcess:Loading Resource {resource} with query {query}");
                        var r = FHIRUtils.CallFHIRServer($"{resource}?{query}", "", HttpMethod.Get, log).Result;
                        if (r.Success && r.Content !=null)
                        {
                            var rs = JObject.Parse(r.Content);
                            if (!rs.IsNullOrEmpty() && ((string)rs["resourceType"]).Equals("Bundle") && !rs["entry"].IsNullOrEmpty())
                            {
                                JArray respentries = (JArray)rs["entry"];
                                string existingid = "urn:uuid:" + (string)respentries[0]["resource"]["id"];
                                tok["fullUrl"] = existingid;
                            }
                        }
                    }
                }
                //reparse JSON with replacement of existing ids prepare to convert to Batch bundle with PUT to maintain relationships
                Dictionary<string, string> convert = new Dictionary<string, string>();
                result["type"] = "batch";
                entries = (JArray)result["entry"];
                foreach (JToken tok in entries)
                {
                    string urn = (string)tok["fullUrl"];
                    if (!string.IsNullOrEmpty(urn) && !tok["resource"].IsNullOrEmpty())
                    {
                        string rt = (string)tok["resource"]["resourceType"];
                        string rid = urn.Replace("urn:uuid:", "");
                        tok["resource"]["id"] = rid;
                        if (!convert.TryAdd(rid, rt))
                        {
                            //Duplicate catch
                            Guid g = Guid.NewGuid();
                            rid = g.ToString();
                            tok["resource"]["id"] = rid;

                        }
                        tok["request"]["method"] = "PUT";
                        tok["request"]["url"] = $"{rt}?_id={rid}";
                    }

                }
                log.LogInformation($"TransformBundleProcess: Phase 2 Localizing {convert.Count} resource entries...");
                IEnumerable<JToken> refs = result.SelectTokens("$..reference");
                foreach (JToken item in refs)
                {
                    string s = item.ToString();
                    string t = "";
                    s = s.Replace("urn:uuid:", "");

                    if (convert.TryGetValue(s, out t))
                    {
                        item.Replace(t + "/" + s);
                    }
                }
                log.LogInformation($"TransformBundleProcess: Complete.");
                return result.ToString();
            }
            return requestBody;
        }
    }
    public class FHIRResponse {
        public static async Task<FHIRResponse> FromHttpResponseMessage(HttpResponseMessage resp,ILogger log)
        {
            var retVal = new FHIRResponse();
            
            if (resp != null)
            {
                retVal.Content = await resp.Content.ReadAsStringAsync();
                retVal.Status = resp.StatusCode;
                retVal.Success = resp.IsSuccessStatusCode;
                if (!retVal.Success)
                {
                    if (string.IsNullOrEmpty(retVal.Content))
                            retVal.Content = resp.ReasonPhrase;
                    if (retVal.Status==System.Net.HttpStatusCode.TooManyRequests)
                    {
                        IEnumerable<string> values;
                        resp.Headers.TryGetValues("x-ms-retry-after-ms", out values);
                        string s_retry = Environment.GetEnvironmentVariable("FBI-DEFAULTRETRY");
                        if (s_retry == null) s_retry = "500";
                        var s = values.First();
                        if (s == null) s = s_retry;
                        int i = 0;
                        if (!int.TryParse(s, out i))
                        {
                            i = 500;
                        }
                        retVal.RetryAfterMS = i;
                    }
                }
               
            }
            return retVal;
        }
        public FHIRResponse()
        {
            Status = System.Net.HttpStatusCode.InternalServerError;
            Success = false;
            Content = null;
            RetryAfterMS = 500;
        }
        public string Content { get; set; }
        public System.Net.HttpStatusCode Status {get;set;}
        public bool Success { get; set; }
        public int RetryAfterMS { get; set; }
    }
}
