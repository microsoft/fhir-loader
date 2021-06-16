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
    public static class ImportFHIRBundles
    {

        [FunctionName("ImportFHIRBundles")]
        public static async Task Run([EventGridTrigger]EventGridEvent blobCreatedEvent,
                                     [Blob("{data.url}", FileAccess.Read, Connection = "FBI-STORAGEACCT")] Stream myBlob,
                                     ILogger log)
        {
            bool trbundles = true;
            string strbundles = System.Environment.GetEnvironmentVariable("FBI-TRANSFORMBUNDLES");
            if (!string.IsNullOrEmpty(strbundles) && (strbundles.ToLower().Equals("no") || strbundles.ToLower().Equals("false"))) trbundles = false;
            StorageBlobCreatedEventData createdEvent = ((JObject)blobCreatedEvent.Data).ToObject<StorageBlobCreatedEventData>();
            string name = createdEvent.Url.Substring(createdEvent.Url.LastIndexOf('/') + 1);
            if (myBlob == null) return;
            log.LogInformation($"ImportFHIRBUndles: Processing file Name:{name} \n Size: {myBlob.Length}");
            var cbclient = StorageUtils.GetCloudBlobClient(System.Environment.GetEnvironmentVariable("FBI-STORAGEACCT"));
            StreamReader reader = new StreamReader(myBlob);
            var text = await reader.ReadToEndAsync();
            var trtext = (trbundles ? FHIRUtils.TransformBundle(text, log) : text);
            var fhirbundle = await FHIRUtils.CallFHIRServer("", trtext, HttpMethod.Post, log);
            var result = LoadErrorsDetected(trtext, fhirbundle, name, log);
           
            //Bundle Post was Throttled we can retry
            if (!fhirbundle.Success && fhirbundle.Status==System.Net.HttpStatusCode.TooManyRequests)
            {
                //Currently cannot use retry hints with EventGrid Trigger function bindings so we will throw and exception to enter eventgrid retry logic for FHIR Server throttling and do
                //our own dead letter for internal errors or unrecoverable conditions
                log.LogInformation($"ImportFHIRBUndles File Name:{name} is throttled...");
                throw new Exception($"ImportFHIRBUndles: Transient Error File: {name}...Entering eventgrid retry process until success or ultimate failure to dead letter if configured.");
            }
            //No Errors move to processed container
            if (fhirbundle.Success && ((JArray)result["errors"]).Count == 0 && ((JArray)result["throttled"]).Count == 0)
            {
                await StorageUtils.MoveTo(cbclient, "bundles", "bundlesprocessed", name, $"{name}.processed", log);
                await StorageUtils.WriteStringToBlob(cbclient, "bundlesprocessed", $"{name}.processed.result", fhirbundle.Content, log);
                log.LogInformation($"ImportFHIRBUndles Processed file Name:{name}");
            }
            //Handle Errors from FHIR Server of proxy
            if (!fhirbundle.Success || ((JArray)result["errors"]).Count > 0) {
                await StorageUtils.MoveTo(cbclient, "bundles", "bundleserr", name,$"{name}.err", log);
                await StorageUtils.WriteStringToBlob(cbclient, "bundleserr", $"{name}.err.response", fhirbundle.Content, log);
                await StorageUtils.WriteStringToBlob(cbclient, "bundleserr", $"{name}.err.actionneeded",result.ToString(), log);
                log.LogInformation($"ImportFHIRBUndles File Name:{name} had errors. Moved to deadletter bundleserr directory");
                
            }
            //Handle Throttled Requests inside of bundle so we will create a new bundle to retry
            if (fhirbundle.Success && ((JArray)result["throttled"]).Count > 0)
            {
                var nb = NDJSONConverter.initBundle();
                nb["entry"] = result["throttled"];
                string fn = $"retry{Guid.NewGuid().ToString().Replace("-","")}.json";
                await StorageUtils.MoveTo(cbclient, "bundles", "bundlesprocessed", name, $"{name}.processed", log);
                await StorageUtils.WriteStringToBlob(cbclient, "bundlesprocessed", $"{name}.processed.result", fhirbundle.Content, log);
                await StorageUtils.WriteStringToBlob(cbclient, "bundles",fn, nb.ToString(), log);
                log.LogInformation($"ImportFHIRBUndles File Name:{name} had throttled resources in response bundle. Moved to processed..Created retry bunde {fn}");

            }

        }
        public static JObject LoadErrorsDetected(string source, FHIRResponse response, string name, ILogger log)
        {
            log.LogInformation($"ImportFHIRBundles:Checking for load errors file {name}");
            JObject retVal = new JObject();
            retVal["id"] = name;
            retVal["errors"] = new JArray();
            retVal["throttled"] = new JArray();
            try
            {
                JObject so = JObject.Parse(source);
                JObject o = JObject.Parse(response.Content);
                int ec = 0;
                if (o["entry"] != null && so["entry"] != null)
                {
                    JArray oentries = (JArray)so["entry"];
                    JArray entries = (JArray)o["entry"];
                    if (oentries.Count != entries.Count)
                    {
                        log.LogWarning($"ImportFHIRBundles: Original resource count and response counts do not agree for file {name}");
                    }
                    foreach (JToken tok in entries)
                    {
                        if (tok["response"] != null && tok["response"]["status"] != null)
                        {
                            string s = (string)tok["response"]["status"];
                            int rc = 200;
                            if (int.TryParse(s, out rc))
                            {

                                if (rc < 200 || rc > 299)
                                {

                                    if (rc == 429)
                                    {
                                        JArray ja = (JArray)retVal["throttled"];
                                        ja.Add(oentries[ec]);
                                    }
                                    else
                                    {
                                        JObject errcontainer = new JObject();
                                        errcontainer["resource"] = oentries[ec];
                                        errcontainer["response"] = tok["response"];
                                        JArray ja = (JArray)retVal["errors"];
                                        ja.Add(errcontainer);
                                    }
                                }
                            }
                        }
                        ec++;
                    }
                    JArray jac = (JArray)retVal["throttled"];
                    if (jac.Count > 0)
                    {
                        log.LogError($"ImportFHIRBundles: {jac.Count} resources were throttled by server for {name}");
                    }
                    jac = (JArray)retVal["errors"];
                    if (jac.Count > 0)
                    {
                        log.LogError($"ImportFHIRBundles: {jac.Count} errors detected in response entries for {name}");
                    }
                }
                else
                {
                    log.LogWarning($"ImportFHIRBundles: Cannot detect resource entries in source/response for {name}");
                }
                return retVal;
            }
            catch (Exception e)
            {
                log.LogError($"ImportFHIRBundles: Unable to parse server response to check for errors file {name}:{e.Message}");
                return retVal;
            }
        }
    }
}
