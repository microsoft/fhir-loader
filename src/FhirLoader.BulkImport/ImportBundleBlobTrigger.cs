using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace FHIRBulkImport
{
    
    public class ImportBundleBlobTrigger
    {
        private readonly TelemetryClient _telemetryClient;
        public ImportBundleBlobTrigger(TelemetryConfiguration telemetryConfiguration)
        {
            _telemetryClient = new TelemetryClient(telemetryConfiguration);
        }
        [Disable("FBI-DISABLE-BLOBTRIGGER")]
        [FunctionName("ImportBundleBlobTrigger")]
        public async Task Run([BlobTrigger("bundles/{name}", Connection = "FBI-STORAGEACCT")]Stream myBlob, string name, ILogger log)
        {
            await ImportUtils.ImportBundle(name, log, _telemetryClient);
        }
    }
}
