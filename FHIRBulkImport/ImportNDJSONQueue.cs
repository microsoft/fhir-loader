using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Queues.Models;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FHIRBulkImport
{
    public class ImportNDJSONQueue
    {
        [Function("ImportNDJSONQueue")]
        public static async Task Run([QueueTrigger("ndjsonqueue", Connection = "FBI-STORAGEACCT-QUEUEURI-IDENTITY")] QueueMessage queueMessage,FunctionContext context)
        {
            var logger = context.GetLogger("ImportNDJSONQueue");
            logger.LogInformation("Function triggered. Started Processing");
            string bodyText = queueMessage.Body.ToString();
            logger.LogInformation($"Queue Message Body: {bodyText}");
            JObject blobCreatedEvent;
            try
            {
                blobCreatedEvent = JObject.Parse(bodyText);
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to parse queue message body: {ex.Message}");
                return;
            }
            string url = (string)blobCreatedEvent["data"]["url"];
            logger.LogInformation($"Blob url extracted: {url}");
            if (queueMessage.DequeueCount > 1)
            {
                logger.LogInformation($"ImportNDJSONQueue: Ignoring long running requeue of file {url} on dequeue {queueMessage.DequeueCount}");
                return;
            }
            int maxresourcesperbundle = 200;
            var cbclient = StorageUtils.GetCloudBlobClient(System.Environment.GetEnvironmentVariable("FBI-STORAGEACCT"));
            string container = Utils.GetEnvironmentVariable("FBI-CONTAINER-NDJSON", "ndjson");
            string name = url.Substring(url.IndexOf($"/{container}/") + $"/{container}/".Length);
            logger.LogInformation($"Blob name resolved: {name}");
            string mrbundlemax = System.Environment.GetEnvironmentVariable("FBI-MAXRESOURCESPERBUNDLE");
            if (!string.IsNullOrEmpty(mrbundlemax))
            {
                if (!int.TryParse(mrbundlemax, out maxresourcesperbundle)) maxresourcesperbundle = 200;
            }
            logger.LogInformation($"NDJSONConverter: Processing blob at {url}...");
            JObject rv = ImportUtils.initBundle();
            int linecnt = 0;
            int total = 0;
            int bundlecnt = 0;
            int errcnt = 0;
            int fileno = 1;
            Stream myBlob = await StorageUtils.GetStreamForBlob(cbclient, container, name,logger);
            if (myBlob==null)
            {
                logger.LogWarning($"ImportNDJSONQueue:The blob {name} in container {container} does not exist or cannot be read.");
                return;
            }
            StringBuilder errsb = new StringBuilder();
            logger.LogInformation($"Starting to read blob stream for {name}");
            using (StreamReader reader = new StreamReader(myBlob))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {

                    linecnt++;   
                    JObject res = null;
                    logger.LogDebug($"Reading line {linecnt}");
                    try
                    {
                        res = JObject.Parse(line);
                        ImportUtils.addResource(rv, res);
                        bundlecnt++;
                        total++;
                    }
                    catch (Exception e)
                    {
                        logger.LogError($"NDJSONConverter: File {name} is in error or contains invalid JSON at line number {linecnt}:{e.Message}");
                        errsb.Append($"{line}\n");
                        errcnt++;
                    }

                    if (bundlecnt >= maxresourcesperbundle)
                    {
                        logger.LogInformation($"Writing bundle {name}--{fileno++}.json with {bundlecnt} resources");
                        await StorageUtils.WriteStringToBlob(cbclient, "bundles", $"{name}-{fileno++}.json", rv.ToString(), logger);
                        
                        bundlecnt = 0;
                        rv = null;
                        rv = ImportUtils.initBundle();
                    }
                }
                if (bundlecnt > 0)
                {
                    logger.LogInformation($"Writing final bundle {name}--{fileno++}.json with {bundlecnt} resources");
                    await StorageUtils.WriteStringToBlob(cbclient, "bundles", $"{name}-{fileno++}.json", rv.ToString(), logger);
                }
                logger.LogInformation($"Moving processed file to 'ndjsonprocessed'");
                await StorageUtils.MoveTo(cbclient, "ndjson", "ndjsonprocessed", name, $"{name}.processed", logger);
                if (errcnt > 0)
                {
                    logger.LogWarning($"Writing error file with {errcnt} errors");
                    await StorageUtils.WriteStringToBlob(cbclient, "ndjsonerr", $"{name}.err", errsb.ToString(), logger);
                }
                logger.LogInformation($"NDJSONConverter: Processing file {name} completed with {total} resources created in {fileno - 1} bundles with {errcnt} errors...");

            }
        }
       
    }
}
