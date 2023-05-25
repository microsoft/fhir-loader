using Microsoft.Extensions.Logging;
using System;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.IdentityModel.Abstractions;
using System.ComponentModel;
using System.Linq;

namespace FHIRBulkImport
{
    public static class ImportUtils
    {
        //Unahandeled Exceptions worth retrying
        public static string MESSAGE_RETRY_SETTING = "request was canceled due to the configured HttpClient.Timeout,target machine actively refused it,an error occurred while sending the request";
        public static string[] EXCEPTION_MESSAGE_STRINGS_RETRY = Utils.GetEnvironmentVariable("FBI-UNHANDLED-RETRY-MESSAGES",MESSAGE_RETRY_SETTING).Split(',');
        
        public static async Task ImportBundle(string name, ILogger log, TelemetryClient telemetryClient)
        {
          
            // Setup for metrics
            bool trbundles = Utils.GetBoolEnvironmentVariable("FBI-TRANSFORMBUNDLES", true);
            log.LogInformation($"ImportFHIRBundles: Processing file Name:{name}...");
            var cbclient = StorageUtils.GetCloudBlobClient(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"));
            string container = Utils.GetEnvironmentVariable("FBI-CONTAINER-BUNDLES", "bundles");
            Stream myBlob = await StorageUtils.GetStreamForBlob(cbclient, container, name, log);
            if (myBlob == null)
            {
                log.LogWarning($"ImportBundle:The blob {name} in container {container} does not exist or cannot be read.");
                return;
            }
            string trtext = "";
            using (StreamReader reader = new StreamReader(myBlob)) { 
                trtext = await reader.ReadToEndAsync();
            }
            telemetryClient.GetMetric("BundlesReceivedCount").TrackValue(1);
            //If not a Batch or Transaction Bundle move it to error and quit
            var bt = FHIRUtils.DetermineBundleType(trtext, log);
            if (bt != BundleType.Transaction && bt != BundleType.Batch)
            {
                log.LogWarning($"ImportFHIRBundles: File Name:{name} is not a Transaction or Batch Bundle resource");
                await StorageUtils.MoveTo(cbclient, "bundles", "bundleserr", name, $"{name}.err", log);
                await StorageUtils.WriteStringToBlob(cbclient, "bundleserr", $"{name}.err.response", "Not a transaction or batch bundle resource.", log);
                return;
            }
            //If it's a transaction bundle convert it to batch if flag is set
            if (bt == BundleType.Transaction && trbundles)
            {
                var timer = Stopwatch.StartNew();
                trtext = FHIRUtils.TransformBundle(trtext, log);
                timer.Stop();
                telemetryClient.GetMetric("BundleTransformDuration").TrackValue(timer.Elapsed.TotalMilliseconds);
                bt = BundleType.Batch;
            }
            if (bt == BundleType.Batch)
            {
                var timer = Stopwatch.StartNew();
                string[] bundlearr = FHIRUtils.SplitBundle(trtext, name, log);
                //If max entries allowed and is a batch then go ahead and split entries to process move original
                if (bundlearr.Length > 1)
                {
                    int cnt = 1;
                    await StorageUtils.MoveTo(cbclient, "bundles", "bundlesprocessed", name, $"{name}.split", log);
                    foreach (string t in bundlearr)
                    {
                        string fn = name.ToLower().Replace(".json", "");
                        await StorageUtils.WriteStringToBlob(cbclient, "bundles", $"{fn}-{cnt}.json", t, log);
                        cnt++;
                    }
                    timer.Stop();
                    telemetryClient.GetMetric("LargeBundleSplitDuration").TrackValue(timer.Elapsed.TotalMilliseconds);
                    return;
                }
                timer.Stop();
                telemetryClient.GetMetric("LargeBundleSplitDuration").TrackValue(timer.Elapsed.TotalMilliseconds);
            }
            //OK we can try and process it
            Stopwatch timefhir = null;
            try
            {
               
                string sresourcecnt = "";
                int resourcecnt = 0;
                using (var jsonDoc = JsonDocument.Parse(trtext))
                {
                    if (jsonDoc.RootElement.TryGetProperty("entry", out JsonElement entries))
                    {
                        resourcecnt = entries.GetArrayLength();
                        sresourcecnt = $" with {resourcecnt} resources ";
                    }
                }
                var starttime = DateTime.UtcNow;
                timefhir = Stopwatch.StartNew();
                var fhirbundle = await FHIRUtils.CallFHIRServer("", trtext, HttpMethod.Post, log);
                timefhir.Stop();
                telemetryClient.TrackDependency(new DependencyTelemetry()
                {
                    Name = "FHIR Server",
                    Data = $"POST bundle {name}{sresourcecnt} file size {trtext.Length} bytes",
                    Timestamp = starttime,
                    Duration = timefhir.Elapsed,
                    Success = fhirbundle.Success,
                    Type = "FHIR Call"
                });
                telemetryClient.GetMetric("FHIRPostBundleDuration").TrackValue(timefhir.Elapsed.TotalMilliseconds);
                telemetryClient.GetMetric("FHIRBundleNumberResources").TrackValue(resourcecnt);
                timefhir = Stopwatch.StartNew();
                var result = LoadErrorsDetected(trtext, fhirbundle, name, log);
                timefhir.Stop();
                telemetryClient.GetMetric("DetectLoadErrorsDuration").TrackValue(timefhir.Elapsed.TotalMilliseconds);
                //Bundle Post was Throttled we can retry
                if (!fhirbundle.Success && fhirbundle.Status == System.Net.HttpStatusCode.TooManyRequests)
                {
                    //Currently cannot use retry hints with EventGrid Trigger function bindings so we will throw and exception to enter eventgrid retry logic for FHIR Server throttling and do
                    //our own dead letter for internal errors or unrecoverable conditions
                    log.LogWarning($"ImportFHIRBundles File Name:{name} is throttled...");
                    throw new TransientError($"ImportFHIRBundles: Transient Error File: {name} was throttled by FHIR Service...Entering eventgrid retry process until success or ultimate failure to dead letter if configured.");
                }
                //No Errors move to processed container
                if (fhirbundle.Success && ((JArray)result["errors"]).Count == 0 && ((JArray)result["throttled"]).Count == 0)
                {
                    await StorageUtils.MoveTo(cbclient, "bundles", "bundlesprocessed", name, $"{name}.processed", log);
                    await StorageUtils.WriteStringToBlob(cbclient, "bundlesprocessed", $"{name}.processed.result", fhirbundle.Content, log);
                    log.LogInformation($"ImportFHIRBundles Processed file Name:{name}");
                }
                //Handle Errors from FHIR Server of proxy
                if (!fhirbundle.Success || ((JArray)result["errors"]).Count > 0)
                {
                    await StorageUtils.MoveTo(cbclient, "bundles", "bundleserr", name, $"{name}.err", log);
                    await StorageUtils.WriteStringToBlob(cbclient, "bundleserr", $"{name}.err.response", fhirbundle.Content, log);
                    await StorageUtils.WriteStringToBlob(cbclient, "bundleserr", $"{name}.err.actionneeded", result.ToString(), log);
                    log.LogInformation($"ImportFHIRBUndles File Name:{name} had errors. Moved to deadletter bundleserr directory");

                }
                //Handle Throttled Requests inside of bundle so we will create a new bundle to retry
                if (fhirbundle.Success && ((JArray)result["throttled"]).Count > 0)
                {
                    var nb = ImportUtils.initBundle();
                    nb["entry"] = result["throttled"];
                    string fn = $"retry{Guid.NewGuid().ToString().Replace("-", "")}.json";
                    await StorageUtils.MoveTo(cbclient, "bundles", "bundlesprocessed", name, $"{name}.processed", log);
                    await StorageUtils.WriteStringToBlob(cbclient, "bundlesprocessed", $"{name}.processed.result", fhirbundle.Content, log);
                    await StorageUtils.WriteStringToBlob(cbclient, "bundles", fn, nb.ToString(), log);
                    log.LogInformation($"ImportFHIRBundles File Name:{name} had throttled resources in response bundle. Moved to processed..Created retry bunde {fn}");

                }
            } catch (TransientError)
            {
                throw;
            }
            catch (Exception e)
            {
                log.LogError($"ImportFHIRBundles:Unhandled Exception on bundle {name}: {e.Message}", e);
                //Check for Unhandled Connection Exceptions and convert to transient error to requeue message
                if (ExceptionWorthRetrying(e))
                {
                    log.LogWarning($"ImportFHIRBundles: Unhandled server exception on bundle {name} worth retrying...requeuing");
                    throw new TransientError(e.Message);
                }
                await StorageUtils.MoveTo(cbclient, "bundles", "bundleserr", name, $"{name}.err", log);
                await StorageUtils.WriteStringToBlob(cbclient, "bundleserr", $"{name}.err.response",$"Unhandled Error:{e.Message}\r\n{e.StackTrace}", log);
                
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
        public static bool ExceptionWorthRetrying(Exception e)
        {
            foreach (string s in EXCEPTION_MESSAGE_STRINGS_RETRY)
            {
                if (e.Message.Contains(s,StringComparison.InvariantCultureIgnoreCase)) return true;
            }
            return false;
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
            req["url"] = $"{rt}/{rid}";
            rv["request"] = req;
            JArray entries = (JArray)bundle["entry"];
            entries.Add(rv);
        }
    }
}
