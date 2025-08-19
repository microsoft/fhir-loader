using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FHIRBulkImport
{
    
    public class ImportBundleEventGrid
    {
    
        [Function("ImportBundleEventGrid")]
        [QueueOutput("bundlequeue", Connection = "FBI-STORAGEACCT-QUEUEURI-IDENTITY")]
        public JObject Run([EventGridTrigger] JObject blobCreatedEvent,
                                     FunctionContext context)
        {
            {
                var logger = context.GetLogger("ImportBundleEventGrid");
                logger.LogInformation("EventGrid trigger recieved event.");
                return blobCreatedEvent;

            }
        }
    }
}