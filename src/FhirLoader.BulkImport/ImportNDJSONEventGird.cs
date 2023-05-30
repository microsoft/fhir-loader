using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.EventGrid;
using Azure.Messaging.EventGrid.SystemEvents;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;


using Newtonsoft.Json.Linq;
namespace FHIRBulkImport
{
    public static class ImportNDJSONEventGird
    {
      
        [FunctionName("ImportNDJSON")]
        [return: Queue("ndjsonqueue", Connection = "FBI-STORAGEACCT")]
        public static JObject Run([EventGridTrigger]JObject blobCreatedEvent,
                                     ILogger log)
        {

            return blobCreatedEvent;

        }
       
       
    }
}
