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
    public static class ImportNDJSON
    {
      
        [FunctionName("ImportNDJSON")]

        public static async Task Run([EventGridTrigger]JObject blobCreatedEvent,
                                     [Blob("{data.url}", FileAccess.Read, Connection = "FBI-STORAGEACCT")] Stream myBlob,
                                     ILogger log)
        {
            
            int maxresourcesperbundle = 200;
            var cbclient = StorageUtils.GetCloudBlobClient(System.Environment.GetEnvironmentVariable("FBI-STORAGEACCT"));
            string mrbundlemax = System.Environment.GetEnvironmentVariable("FBI-MAXRESOURCESPERBUNDLE");
            if (!string.IsNullOrEmpty(mrbundlemax))
            {
                if (!int.TryParse(mrbundlemax, out maxresourcesperbundle)) maxresourcesperbundle = 200;
            }
            string url = (string)blobCreatedEvent["data"]["url"];
            log.LogInformation($"NDJSONConverter: Processing blob at {url}...");
            string name = url.Substring(url.LastIndexOf('/') + 1);
            JObject rv = initBundle();
            int linecnt = 0;
            int total = 0;
            int bundlecnt = 0;
            int errcnt = 0;
            int fileno = 1;
            //Stream myBlob = null;
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
                            addResource(rv, res);
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
                            rv = initBundle();
                        }
                    }
                    if (bundlecnt > 0)
                    {
                        await StorageUtils.WriteStringToBlob(cbclient, "bundles", $"{name}-{fileno++}.json", rv.ToString(), log);
                    }
                    await StorageUtils.MoveTo(cbclient,"ndjson","ndjsonprocessed",name,$"{name}.processed",log);
                    if (errcnt > 0)
                    {
                        await StorageUtils.WriteStringToBlob(cbclient, "ndjsonerr", $"{name}.err", errsb.ToString(), log);
                    }
                    log.LogInformation($"NDJSONConverter: Processing file {name} completed with {total} resources created in {fileno-1} bundles with {errcnt} errors...");
                
            }

        }
        public static JObject initBundle()
        {
            JObject rv = new JObject();
            rv["resourceType"] = "Bundle";
            rv["type"] = "batch";
            rv["entry"] = new JArray();
            return rv;
        }
        public static void addResource(JObject bundle, JToken tok)
        {
            JObject rv = new JObject();
            string rt = (string)tok["resourceType"];
            string rid = (string)tok["id"];
            rv["fullUrl"] = $"{rt}/{rid}";
            rv["resource"] = tok;
            JObject req = new JObject();
            req["method"] = "PUT";
            req["url"] = $"{rt}?_id={rid}";
            rv["request"] = req;
            JArray entries = (JArray)bundle["entry"];
            entries.Add(rv);
        }
       
    }
}
