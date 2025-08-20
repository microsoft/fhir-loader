using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;

namespace FHIRBulkImport
{
    
    public class ImportBundleBlobTrigger
    {
        private readonly TelemetryClient _telemetryClient;
        public ImportBundleBlobTrigger(TelemetryConfiguration telemetryConfiguration)
        {
            _telemetryClient = new TelemetryClient(telemetryConfiguration);
        } 
       
        [Function("ImportBundleBlobTrigger")]
       
        public async Task Run([BlobTrigger("bundles/{name}", Connection = "FBI-STORAGEACCT-IDENTITY")]Stream myBlob, string name, FunctionContext context)
        {
            var logger = context.GetLogger("ImportBundleBlobTrigger");
            await ImportUtils.ImportBundle(name, logger, _telemetryClient);
        }   
    }
}
