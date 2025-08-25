using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net.Http;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;

namespace FHIRBulkImport
{
    public class ImportBundleHTTP
    {
        private readonly TelemetryClient _telemetryClient;
        public ImportBundleHTTP(TelemetryConfiguration telemetryConfiguration)
        {
            _telemetryClient = new TelemetryClient(telemetryConfiguration);
        }
      
        [Function("ImportBundleHTTP")]
        public  async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "importbundle")] HttpRequestData req,
            FunctionContext context)
        {
            var logger = context.GetLogger("ImportBundleHTTP");
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            string filename = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["bundlename"];
            if (string.IsNullOrEmpty(filename)) filename = $"bundle{Guid.NewGuid().ToString().Replace("-", "")}.json";
            if (!filename.ToLower().EndsWith(".json")) filename += ".json";
            var response = req.CreateResponse();
            try
            {
                var o = JObject.Parse(requestBody);
                if (o["resourceType"] !=null && o["resourceType"].ToString().Equals("Bundle"))
                {
                    var cbclient = StorageUtils.GetCloudBlobClient(System.Environment.GetEnvironmentVariable("FBI_STORAGEACCT"));
                    await StorageUtils.WriteStringToBlob(cbclient, "bundles", filename, requestBody, logger);
                    response.StatusCode = System.Net.HttpStatusCode.Accepted;
                    response.Headers.Add("Content-Type", "application/json");
                    await response.WriteStringAsync($"{{\"filename\":\"{filename}\"}}");
                    return response;

                }
                response.StatusCode = System.Net.HttpStatusCode.BadRequest;
                response.Headers.Add("Content-Type", "text/plain");
                await response.WriteStringAsync("Not a Valid FHIR Bundle");
                return response;

            }
            catch (JsonReaderException jre)
            {
                response.StatusCode = System.Net.HttpStatusCode.BadRequest;
                response.Headers.Add("Content-Type", "text/plain");
                await response.WriteStringAsync($"Invalid JSONRequest Body:{jre.Message}");
                return response;

            }
            catch (Exception e)
            {
                response.StatusCode = System.Net.HttpStatusCode.InternalServerError;
                response.Headers.Add("Content-Type", "text/plain");
                await response.WriteStringAsync($"Error processing request:{e.Message}");
                return response;
             

            }
        }
    }
}
