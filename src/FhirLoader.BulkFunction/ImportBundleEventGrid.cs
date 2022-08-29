using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FHIRBulkImport
{
    
    public static class ImportBundleEventGrid
    {
        [FunctionName("ImportBundleEventGrid")]
        public static async Task Run([EventGridTrigger] EventGridEvent blobCreatedEvent,
                                     [Blob("{data.url}", FileAccess.Read, Connection = "FBI-STORAGEACCT")] Stream myBlob,
                                     ILogger log)
        {
            StorageBlobCreatedEventData createdEvent = ((JObject)blobCreatedEvent.Data).ToObject<StorageBlobCreatedEventData>();
            string name = createdEvent.Url.Substring(createdEvent.Url.LastIndexOf('/') + 1);
            await ImportUtils.ImportBundle(myBlob, name, log);
        }
    }
}
