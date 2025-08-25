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
        [QueueOutput("ndjsonqueue", Connection = "FBI_STORAGEACCT_QUEUEURI_IDENTITY")]
        public static JObject Run([EventGridTrigger]EventGridEvent eventGridEvent,
                                     FunctionContext context)
        {
            var logger = context.GetLogger("ImportNDJSON");
            var eventJson = JObject.Parse(eventGridEvent.Data.ToString());
            logger.LogInformation($"Full EventGrid received: {eventJson}");
            return eventJson;
        }
       
       
    }
}
