using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FHIRBulkImport
{
    public class ImportNDJSONQueue
    {
        [FunctionName("ImportNDJSONQueue")]
        public static async Task Run([QueueTrigger("ndjsonqueue", Connection = "FBI-STORAGEACCT")] QueueMessage queueMessage,ILogger log)
        {
            JObject blobCreatedEvent = JObject.Parse(queueMessage.Body.ToString());
            string url = (string)blobCreatedEvent["data"]["url"];
            if (queueMessage.DequeueCount > 1)
            {
                log.LogInformation($"ImportNDJSONQueue: Ignoring long running requeue of file {url}");
                return;
            }
            int maxresourcesperbundle = 200;
            var cbclient = StorageUtils.GetCloudBlobClient(System.Environment.GetEnvironmentVariable("FBI-STORAGEACCT"));
            string container = Utils.GetEnvironmentVariable("FBI-CONTAINER-NDJSON", "ndjson");
            string name = url.Substring(url.LastIndexOf("/")+1);
            string mrbundlemax = System.Environment.GetEnvironmentVariable("FBI-MAXRESOURCESPERBUNDLE");
            if (!string.IsNullOrEmpty(mrbundlemax))
            {
                if (!int.TryParse(mrbundlemax, out maxresourcesperbundle)) maxresourcesperbundle = 200;
            }
            log.LogInformation($"NDJSONConverter: Processing blob at {url}...");
            JObject rv = ImportUtils.initBundle();
            int linecnt = 0;
            int total = 0;
            int bundlecnt = 0;
            int errcnt = 0;
            int fileno = 1;
            Stream myBlob = await StorageUtils.GetStreamForBlob(cbclient, container, name,log);
            if (myBlob==null)
            {
                log.LogWarning($"ImportNDJSONQueue:The blob {name} in container {container} does not exist or cannot be read.");
                return;
            }
            StringBuilder errsb = new StringBuilder();
            using (StreamReader reader = new StreamReader(myBlob))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {

                    linecnt++;
                    JObject res = null;
                    try
                    {
                        res = JObject.Parse(line);
                        ImportUtils.addResource(rv, res);
                        bundlecnt++;
                        total++;
                    }
                    catch (Exception e)
                    {
                        log.LogError($"NDJSONConverter: File {name} is in error or contains invalid JSON at line number {linecnt}:{e.Message}");
                        errsb.Append($"{line}\n");
                        errcnt++;
                    }

                    if (bundlecnt >= maxresourcesperbundle)
                    {
                        await StorageUtils.WriteStringToBlob(cbclient, "bundles", $"{name}-{fileno++}.json", rv.ToString(), log);
                        bundlecnt = 0;
                        rv = null;
                        rv = ImportUtils.initBundle();
                    }
                }
                if (bundlecnt > 0)
                {
                    await StorageUtils.WriteStringToBlob(cbclient, "bundles", $"{name}-{fileno++}.json", rv.ToString(), log);
                }
                await StorageUtils.MoveTo(cbclient, "ndjson", "ndjsonprocessed", name, $"{name}.processed", log);
                if (errcnt > 0)
                {
                    await StorageUtils.WriteStringToBlob(cbclient, "ndjsonerr", $"{name}.err", errsb.ToString(), log);
                }
                log.LogInformation($"NDJSONConverter: Processing file {name} completed with {total} resources created in {fileno - 1} bundles with {errcnt} errors...");

            }
        }
       
    }
}
