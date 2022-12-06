using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FHIRBulkImport
{
    
    public class ImportBundleEventGrid
    {
        private readonly TelemetryClient _telemetryClient;
        public ImportBundleEventGrid(TelemetryConfiguration telemetryConfiguration)
        {
            _telemetryClient = new TelemetryClient(telemetryConfiguration);
        }
        [FunctionName("ImportBundleEventGrid")]
        public async Task Run([EventGridTrigger] JObject blobCreatedEvent,
                                     [Blob("{data.url}", FileAccess.Read, Connection = "FBI-STORAGEACCT")] Stream myBlob,
                                     ILogger log)
        {
            string url = (string)blobCreatedEvent["data"]["url"];
            log.LogInformation($"ImportBundleEventGrid: Processing blob at {url}...");
            string name = url.Substring(url.LastIndexOf('/') + 1);
            await ImportUtils.ImportBundle(myBlob, name, log,_telemetryClient);
        }
    }
}
