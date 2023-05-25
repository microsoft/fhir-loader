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
    
        [FunctionName("ImportBundleEventGrid")]
        [return: Queue("bundlequeue", Connection = "FBI-STORAGEACCT")]
        public static JObject Run([EventGridTrigger] JObject blobCreatedEvent,
                                     ILogger log)
        {
            {

                return blobCreatedEvent;

            }
        }
    }
}
