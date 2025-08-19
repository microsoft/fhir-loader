using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.EventGrid;
using Azure.Messaging.EventGrid.SystemEvents;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;


using Newtonsoft.Json.Linq;
namespace FHIRBulkImport
{
    public static class ImportNDJSONEventGird
    {
      
        [Function("ImportNDJSON")]
        [QueueOutput("ndjsonqueue", Connection = "FBI-STORAGEACCT-QUEUEURI-IDENTITY")]
        public static JObject Run([EventGridTrigger]JObject blobCreatedEvent,
                                     FunctionContext context)
        {
            var logger = context.GetLogger("ImportNDJSON");
            return blobCreatedEvent;

        }
       
       
    }
}
