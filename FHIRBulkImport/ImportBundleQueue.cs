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
    
    public class ImportBundleQueue
    {
        private readonly TelemetryClient _telemetryClient;
        public ImportBundleQueue(TelemetryConfiguration telemetryConfiguration)
        {
            _telemetryClient = new TelemetryClient(telemetryConfiguration);
        }
        [FunctionName("ImportBundleQueue")]
        public async Task Run([QueueTrigger("bundlequeue", Connection = "FBI-STORAGEACCT")] JObject blobCreatedEvent, ILogger log)
        {
            string url = (string)blobCreatedEvent["data"]["url"];
            log.LogInformation($"ImportBundleEventGrid: Processing blob at {url}...");
            string name = url.Substring(url.LastIndexOf('/') + 1);
            await ImportUtils.ImportBundle(name, log, _telemetryClient);
        }
    }
}
