using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
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
        [FunctionName("ImportBundleHTTP")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "importbundle")] HttpRequest req,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            string filename = req.Query["bundlename"];
            if (string.IsNullOrEmpty(filename)) filename = $"bundle{Guid.NewGuid().ToString().Replace("-", "")}.json";
            if (!filename.ToLower().EndsWith(".json")) filename += ".json";
            try
            {
                var o = JObject.Parse(requestBody);
                if (o["resourceType"] !=null && o["resourceType"].ToString().Equals("Bundle"))
                {
                    var cbclient = StorageUtils.GetCloudBlobClient(System.Environment.GetEnvironmentVariable("FBI-STORAGEACCT"));
                    await StorageUtils.WriteStringToBlob(cbclient, "bundles", filename, requestBody, log);
                    return new ContentResult() { Content = "{\"filename\":\"" + filename + "\"}", StatusCode = 202, ContentType = "application/json" };

                }
                return new ContentResult() { Content = $"Not a Valid FHIR Bundle", StatusCode = 400, ContentType = "text/plain" };

            }
            catch (JsonReaderException jre)
            {
                return new ContentResult() {Content=$"Invalid JSONRequest Body:{jre.Message}",StatusCode=400,ContentType="text/plain" };
            }
            catch (Exception e)
            {
                return new ContentResult() { Content = $"Error processing request:{e.Message}", StatusCode = 500, ContentType = "text/plain" };

            }
        }
    }
}
