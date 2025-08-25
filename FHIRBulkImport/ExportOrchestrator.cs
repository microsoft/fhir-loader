using Azure.Storage.Blobs.Specialized;
using DurableTask.Core;
using DurableTask.Core.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Http;
using Microsoft.Azure.Functions.Worker.Extensions.DurableTask;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace FHIRBulkImport
{
    public class ExportOrchestrator
    {
         private static void IdentifyUniquePatientReferences(JObject resource, string patreffield, HashSet<string> uniquePats)
        {
            List<string> retVal = new List<string>();
           
            if (resource.FHIRResourceType().Equals("Bundle")) 
            {
                JArray arr = (JArray)resource["entry"];
                if (arr != null)
                {
                    foreach (JToken entry in arr)
                    {
                        var r = entry["resource"];
                        if (r == null)
                        {
                            continue;
                        }
                        string id = null;
                        if (patreffield.Equals("id"))
                        {
                            id = r.FHIRResourceId();
                        }
                        else
                        {
                            if (!r[patreffield].IsNullOrEmpty())
                            {
                                string patref = (string)r[patreffield]["reference"];
                                if (patref != null && patref.StartsWith("Patient") && patref.IndexOf("/") > 0)
                                {
                                    id = patref.Split("/")[1];
                                }
                            }
                         
                        }
                        if (id != null && !uniquePats.Contains(id))
                        {
                            uniquePats.Add(id);
                        }

                    }
                }
            }
           
        }
        private static JObject SetContextVariables(string instanceId, string ids = null, JArray include = null)
        {
            JObject o = new JObject();
            o["instanceId"] = instanceId;
            if( ids != null) o["ids"] = ids;
            if (include != null) o["include"] = include;
            return o;
        }
        [Function("CountFileLines")]
        public async Task<JObject> CountFileLines(
        [ActivityTrigger] JObject ctx,
        FunctionContext context)
        {
            var log = context.GetLogger("CountFileLines");
            string instanceid = (string)ctx["instanceid"];
            string blob = (string)ctx["filename"];
            return await FileHolderManager.CountLinesInBlob(Utils.GetEnvironmentVariable("FBI_STORAGEACCT"),instanceid, blob,log);
        }
        [Function("FileNames")]
        public async Task<List<string>> FileNames(
         [ActivityTrigger] string instanceid,
         FunctionContext context)
        {
            var log = context.GetLogger("FileNames");

            return await FileHolderManager.GetFileNames(instanceid,log);
        }
        [Function("ExportOrchestrator")]
        public async Task<JArray> RunOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context,
            FunctionContext functionContext)
        {
            var log = functionContext.GetLogger("ExportOrchestrator");
            JObject config = null;
            JObject retVal = new JObject();
            HashSet<string> uniquePats = new HashSet<string>();
            retVal["instanceid"] = context.InstanceId;
            string inputs = context.GetInput<string>();
            try
            {
                config = JObject.Parse(inputs);
            }
            catch (Newtonsoft.Json.JsonReaderException jre)
            {
                retVal["error"] = $"Not a valid JSON Object from starter input:{jre.Message}";
                log.LogError("ExportOrchestrator: Not a valid JSON Object from starter input");
                return new JArray(retVal);
            }
            string query = (string)config["query"];
            string patreffield = (string)config["patientreferencefield"];
            JArray include = (JArray)config["include"];
            if (query==null || patreffield==null)
            {
                retVal["error"] = "query and/or patientreferencefield is empty";
                return new JArray(retVal);
            }
            retVal["extractstarted"] = context.CurrentUtcDateTime;
            context.SetCustomStatus(retVal);
            //get a list of N work items to process in parallel
            var tasks = new List<Task<JObject>>();
            JObject parms = new JObject();
            parms["query"] = query;
            parms["instanceid"] = context.InstanceId;
            parms["patreffield"] = patreffield;
            retVal["gatheringidsstarted"] = context.CurrentUtcDateTime;
            context.SetCustomStatus(retVal);
            var uniquepats = await context.CallActivityAsync<HashSet<string>>("QueryFHIR", parms);
            retVal["gatheringidscompleted"] = context.CurrentUtcDateTime;
            retVal["uniqueidstoprocess"] = uniquepats.Count();
            context.SetCustomStatus(retVal);
            List<string> ids = new List<string>();
            int suborchs = 0;
            foreach (string id in uniquepats)
            {
                ids.Add(id);
                if (ids.Count() == 50)
                {
                    var send = string.Join(",", ids);
                    tasks.Add(context.CallSubOrchestratorAsync<JObject>("ExportOrchestrator_ProcessPatientQueryPage", SetContextVariables(context.InstanceId, send, include)));
                    ids.Clear();
                    suborchs++;
                    retVal["suborchestrationsqueued"] = suborchs;
                    context.SetCustomStatus(retVal);
                }
            }
            if (ids.Count() > 0)
            {
                var send = string.Join(",", ids);
                tasks.Add(context.CallSubOrchestratorAsync<JObject>("ExportOrchestrator_ProcessPatientQueryPage", SetContextVariables(context.InstanceId, send, include)));
                ids.Clear();
                suborchs++;
                retVal["suborchestrationsqueued"] = suborchs;
                context.SetCustomStatus(retVal);
            }
            retVal["suborchestionwaitstarted"] = context.CurrentUtcDateTime;
            context.SetCustomStatus(retVal);
            await Task.WhenAll(tasks);
            retVal["suborchestionwaitcompleted"] = context.CurrentUtcDateTime;
            context.SetCustomStatus(retVal);
            var callResults = tasks
                    .Where(t => t.Status == TaskStatus.RanToCompletion)
                    .Select(t => t.Result);
            retVal["extractcompleted"] = context.CurrentUtcDateTime;
            List<string> blobNames = new List<string>();
            JObject extractresult = new JObject();
            foreach (JObject j in callResults)
                {
                    foreach (JProperty property in j.Properties())
                    {
                        if (extractresult[property.Name] != null)
                        {
                            int total = (int)extractresult[property.Name];
                            total +=(int)property.Value;
                            extractresult[property.Name] = total;
                        } else
                        {
                            extractresult[property.Name] = property.Value;
                        }
                        if (!blobNames.Contains(property.Name)) blobNames.Add(property.Name);
                    }
                }
            retVal["extractresults"] = extractresult;
            context.SetCustomStatus(retVal);
            tasks.Clear();
            retVal["fileresourcecountstarted"] = context.CurrentUtcDateTime;
            context.SetCustomStatus(retVal);
            var filenames = await context.CallActivityAsync<List<string>>("FileNames", context.InstanceId);
            foreach (string s in filenames)
            {
                JObject parms1 = new JObject();
                parms1["instanceid"] = context.InstanceId;
                parms1["filename"] = s;
                tasks.Add(context.CallActivityAsync<JObject>("CountFileLines", parms1));
            }
            await Task.WhenAll(tasks);
            retVal["fileresourcecountcompleted"] = context.CurrentUtcDateTime;
            context.SetCustomStatus(retVal);
            var callResults1 = tasks
                    .Where(t => t.Status == TaskStatus.RanToCompletion)
                    .Select(t => t.Result);
            JArray filecounts = new JArray();
            foreach (JObject j in callResults1)
            {
                filecounts.Add(j);
            }
            string rm = retVal.ToString(Newtonsoft.Json.Formatting.None);
            await context.CallActivityAsync<bool>("AppendBlob", SetContextVariables(context.InstanceId, rm));
            log.LogInformation($"Completed orchestration with ID = '{context.InstanceId}'.");
            return filecounts;
        }
        [Function("AppendBlob")]
        public async Task<bool> AppendBlob(
           [ActivityTrigger] JToken ctx,
           FunctionContext context)
        {
            var log = context.GetLogger("AppendBlob");
            string instanceid = (string)ctx["instanceId"];
            string rm = (string)ctx["ids"];
            var appendBlobClient = await StorageUtils.GetAppendBlobClient(Utils.GetEnvironmentVariable("FBI_STORAGEACCT"), $"export/{instanceid}", "_completed_run.json");
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(rm)))
            {
                await appendBlobClient.AppendBlockAsync(ms);
            }
            return true;
        }
        [Function("QueryFHIR")] 
        public  async Task<HashSet<string>> QueryFHIR(
            [ActivityTrigger] JToken input,
            FunctionContext context)
        {
            var log = context.GetLogger("QueryFHIR");
            HashSet<string> uniquePats = new HashSet<string>();
            try
            {
               
                string query = input["query"].ToString();
                string instanceid = input["instanceid"].ToString();
                string patreffield = (string)input["patreffield"];
                var fhirresp = await FHIRUtils.CallFHIRServer(query, "", HttpMethod.Get, log);
                if (fhirresp.Success && !string.IsNullOrEmpty(fhirresp.Content))
                {
                    var resource = JObject.Parse(fhirresp.Content);
                    //For group resource loop through the member array
                    if (resource.FHIRResourceType().Equals("Group"))
                    {
                        JArray ga = (JArray)resource["member"];
                        if (!ga.IsNullOrEmpty())
                        {
                            int cnt = 0;
                            var bundle = ImportUtils.initBundle();
                            foreach (JToken t in ga)
                            {
                                string prv = (string)t["entity"]["reference"];
                                JObject o = new JObject();
                                o["resourceType"] = "GroupInternal";
                                o["entity"] = new JObject();
                                o["entity"]["reference"] = prv;
                                ImportUtils.addResource(bundle, o);
                                cnt++;
                                if (cnt % 50 == 0)
                                {
                                    IdentifyUniquePatientReferences(bundle, "entity", uniquePats);
                                    bundle = ImportUtils.initBundle();
                                }
                            }
                            if (((JArray)bundle["entry"]).Count > 0)
                            {
                                IdentifyUniquePatientReferences(bundle, "entity", uniquePats);
                            }

                        }
                    }
                    else
                    {
                        //Page through query results fo everything else          
                        IdentifyUniquePatientReferences(resource, patreffield, uniquePats);
                        bool nextlink = !resource["link"].IsNullOrEmpty() && ((string)resource["link"].getFirstField()["relation"]).Equals("next");
                        while (nextlink)
                        {
                            string nextpage = (string)resource["link"].getFirstField()["url"];
                            fhirresp = await FHIRUtils.CallFHIRServer(nextpage, "", HttpMethod.Get, log);
                            if (!fhirresp.Success)
                            {
                                log.LogError($"Query FHIR: FHIR Server Call Failed: {fhirresp.Status} Content:{fhirresp.Content} Query:{nextpage}");
                                nextlink = false;
                            }
                            else
                            {

                                resource = JObject.Parse(fhirresp.Content);
                                IdentifyUniquePatientReferences(resource, patreffield, uniquePats);
                                nextlink = !resource["link"].IsNullOrEmpty() && ((string)resource["link"].getFirstField()["relation"]).Equals("next");
                            }
                        }
                    }

                }
                else
                {
                    log.LogError($"Query FHIR: FHIR Server Call Failed: {fhirresp.Status} Content:{fhirresp.Content} Query:{query}");

                }
            }
            catch (Exception e)
            {
                log.LogError($"Query FHIR: Unhandled Exception: {e.Message}\r\n{e.StackTrace}");
            }
            return uniquePats;
        }
        [Function("ExportOrchestrator_ProcessPatientQueryPage")]
        public async Task<JObject> ProcessPatientQueryPage([OrchestrationTrigger] TaskOrchestrationContext context, FunctionContext functionContext)
        {
            var log = functionContext.GetLogger("ExportOrchestrator_ProcessPatientQueryPage");
            JObject retVal = new JObject();
            var vars = context.GetInput<JToken>();
            try
            {
                JArray include = (JArray)vars["include"];
                string ids = (string)vars["ids"];
                string instanceid = (string)vars["instanceId"];
                if (string.IsNullOrEmpty(ids)) log.LogWarning("ExportOrchestrator_ProcessPatientQueryPage: Null/Empty Check IDS is null or empty");
                if (include == null) log.LogWarning("ExportOrchestrator_ProcessPatientQueryPage: Null Check include is null");
                if (!string.IsNullOrEmpty(ids))
                {
                        if (include != null)
                        {
                            var subtasks = new List<Task<string>>();
                            foreach (JToken t in include)
                            {
                                string sq = t.ToString();
                                sq = sq.Replace("$IDS", ids);
                                string rt = sq.Split("?")[0];
                                string key = $"{vars["instanceId"].ToString()}-{rt}";
                                JObject ctxparms = new JObject();
                                ctxparms["instanceid"] = vars["instanceId"].ToString();
                                ctxparms["ids"] = sq;
                                ctxparms["resourcetype"] = rt;
                                subtasks.Add(context.CallActivityAsync<string>("ExportOrchestrator_GatherResources", ctxparms));
                            }
                        
                            await Task.WhenAll(subtasks);
                            var callResults = subtasks
                                .Where(t => t.Status == TaskStatus.RanToCompletion)
                                .Select(t => t.Result);
                            foreach (string s in callResults)
                            {
                                string[] sa = s.Split(":");
                                string prop = sa[0];
                                int added = int.Parse(sa[1]);
                                JToken p = retVal[prop];
                                if (p == null)
                                {
                                    retVal[prop] = added;
                                }
                                else
                                {
                                    int val = (int)p;
                                    val += added;
                                    p = val;
                                }

                            }
                        }
                }
            } catch (Exception e)
            {
                log.LogError($"ExportOrchestrator Process Patient Page Exception:{e.Message}\r\nTrace:{e.ToString()}");
            }            
            return retVal;
        }
        [Function("FileTracker")]
        public async Task Run([OrchestrationTrigger] TaskOrchestrationContext ctx, FunctionContext context)
        {
            var log = context.GetLogger("FileTracker");
            var input = ctx.GetInput<dynamic>();
            if (input.Operation == "set")
            {
                await ctx.CallActivityAsync("FileTracker_Set", input.Value);
            }
            else if (input.Operation == "get")
            {
                var value = await ctx.CallActivityAsync<string>("FileTracker_Get", null);
                ctx.SetCustomStatus(value);
            }

        }
        [Function("ExportOrchestrator_GatherResources")]
        public static async Task<string> GatherResources([ActivityTrigger] JToken input, [DurableClient] DurableTaskClient entityclient, FunctionContext context)
        {
            var log = context.GetLogger("ExportOrchestrator_GatherResources");
   
            int total = 0;
            string query = input["ids"].ToString();
            string instanceid = input["instanceid"].ToString();
            var rt = (string)input["resourcetype"];
            var fhirresp = await FHIRUtils.CallFHIRServer(query, "", HttpMethod.Get, log);
            if (fhirresp.Success && !string.IsNullOrEmpty(fhirresp.Content))
            {

                    var resource = JObject.Parse(fhirresp.Content);
                    total = total + (await ConvertToNDJSON(resource,instanceid,rt,entityclient,log)).Value.ResourceCount;
                    bool nextlink = !resource["link"].IsNullOrEmpty() && ((string)resource["link"].getFirstField()["relation"]).Equals("next");
                    while (nextlink)
                    {
                        string nextpage = (string)resource["link"].getFirstField()["url"];
                        fhirresp = await FHIRUtils.CallFHIRServer(nextpage, "", HttpMethod.Get, log);
                        if (!fhirresp.Success || string.IsNullOrEmpty(fhirresp.Content))
                        {
                            log.LogError($"ExportOrchestrator: FHIR Server Call Failed: {fhirresp.Status} Content:{fhirresp.Content} Query:{nextpage}");
                           nextlink = false;
                        }
                        else
                        {
                            resource = JObject.Parse(fhirresp.Content);
                            total = total + (await ConvertToNDJSON(resource, instanceid, rt, entityclient, log)).Value.ResourceCount;
                        nextlink = !resource["link"].IsNullOrEmpty() && ((string)resource["link"].getFirstField()["relation"]).Equals("next");
                        }
                    }
            } else
            {
                log.LogError($"ExportOrchestrator: FHIR Server Call Failed: {fhirresp.Status} Content:{fhirresp.Content} Query:{query}");
            }
            return $"{rt}:{total}";
        }

        public record struct ConvertToNDJSONResponse(int ResourceCount, string ResourceType, string BlobUrl);

        internal static async Task<ConvertToNDJSONResponse?> ConvertToNDJSON(JToken bundle, string instanceId, string resourceType, DurableTaskClient entityclient, ILogger log, int? parallelFileId = null)
        {
            ConvertToNDJSONResponse? retVal = null;
            int cnt = 0;

            try
            {
                StringBuilder sb = new StringBuilder();
                if (!bundle.IsNullOrEmpty() && bundle.FHIRResourceType().Equals("Bundle"))
                {
                    JArray arr = (JArray)bundle["entry"];
                    if (arr != null)
                    {
                        foreach (JToken tok in arr)
                        {
                            JToken res = tok["resource"];
                            sb.Append(res.ToString(Newtonsoft.Json.Formatting.None));
                            sb.Append("\n");
                            cnt++;
                        }
                    }

                }
                if (sb.Length > 0)
                {
                    string parallelizationModifierStr = parallelFileId.HasValue ? $"-{parallelFileId.Value}" : string.Empty;
                    string key = $"{instanceId}{parallelizationModifierStr}-{resourceType}";

                    var getInstanceId = await entityclient.ScheduleNewOrchestrationInstanceAsync("FileTracker", (Operation: "get", Key: key));
                    var status = await entityclient.WaitForInstanceCompletionAsync(getInstanceId, CancellationToken.None);
                    int fileno = status?.SerializedOutput!= null ? JsonSerializer.Deserialize<int>(status.SerializedOutput) : 0;
                    var filename = resourceType + parallelizationModifierStr + "-" + (fileno + 1) + ".ndjson";                                  
                    var blobclient = StorageUtils.GetAppendBlobClientSync(Utils.GetEnvironmentVariable("FBI_STORAGEACCT"), $"export/{instanceId}", filename);
                    long maxfilesizeinbytes = Utils.GetIntEnvironmentVariable("FBI_MAXFILESIZEMB", "-1") * 1024000;
                    int bytestoadd = System.Text.ASCIIEncoding.UTF8.GetByteCount(sb.ToString());
                    var props = blobclient.GetProperties();
                    long filetotalbytes = props.Value.ContentLength + bytestoadd;

                    // If the next file would be larger than the max, point to a new blob instead.
                    if (props.Value.BlobCommittedBlockCount > 49500 || (maxfilesizeinbytes > 0 && filetotalbytes >= maxfilesizeinbytes))
                    {
                        fileno++;
                        filename = resourceType + parallelizationModifierStr + "-" + (fileno + 1) + ".ndjson";
                        blobclient = StorageUtils.GetAppendBlobClientSync(Utils.GetEnvironmentVariable("FBI_STORAGEACCT"), $"export/{instanceId}", filename);
                        await entityclient.ScheduleNewOrchestrationInstanceAsync("FileTracker", (Operation: "set", Key: key, Value: fileno));
                    }

                    // Write the data to blob storage
                    var rslt = await FileHolderManager.WriteAppendBlobAsync(blobclient, sb.ToString(), log);

                    return new ConvertToNDJSONResponse(cnt, resourceType, blobclient.Uri.ToString());
                }

                return new ConvertToNDJSONResponse(cnt, resourceType, null);

            }
            catch (Exception e)
            {
                log.LogError($"ExportNDJSON Exception: {e.Message}\r\n{e.StackTrace}");
            }

            return retVal;
        }
        [Function("ExportOrchestrator_HttpStart")]
        public async Task<HttpResponseData> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function,"post",Route = "$alt-export")] HttpRequestData req,
            [DurableClient] DurableTaskClient starter,
            FunctionContext context)
        {
            var log = context.GetLogger("ExportOrchestrator_HttpStart");

            string config = await new StreamReader(req.Body).ReadToEndAsync();
            var state  = await runningInstances(starter, context);
            int running = state.Count();
            int maxinstances = Utils.GetIntEnvironmentVariable("FBI_MAXEXPORTS", "0");
            if (maxinstances > 0 && running >= maxinstances)
            {
                string msg = $"Unable to start export there are {running} exports the max concurrent allowed is {maxinstances}";
                var response = req.CreateResponse(System.Net.HttpStatusCode.TooManyRequests);
                await response.WriteStringAsync($"{{\"error\":\"{msg}\"}}");
              
                return response;
            }
            // Function input comes from the request content.
            string instanceId = await starter.ScheduleNewOrchestrationInstanceAsync("ExportOrchestrator",config);
            
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
        [Function("ExportOrchestrator_InstanceAction")]
        public async Task<HttpResponseData> InstanceAction(
          [HttpTrigger(AuthorizationLevel.Function, "get", Route = "$alt-export-manage/{instanceid}")] HttpRequestData req,
          [DurableClient] DurableTaskClient starter,string instanceid,
          FunctionContext context)
        {
            var log = context.GetLogger("ExportOrchestrator_InstanceAction");
            var parms = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            string action = parms["action"];
            await starter.TerminateInstanceAsync(instanceid, "Terminated by User");
            StringContent sc = new StringContent($"Terminated {instanceid}");
            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain");
            await response.WriteStringAsync("Terminated abc");
            return response;
        }
        [Function("ExportOrchestrator_ExportStatus")]
        public async Task<HttpResponseData> ExportStatus(
           [HttpTrigger(AuthorizationLevel.Function, "get", Route = "$alt-export-status")] HttpRequestData req,
           [DurableClient] DurableTaskClient client,
           FunctionContext context)
        {
            var log = context.GetLogger("ExportOrchestrator_ExportStatus");
            string config = await new StreamReader(req.Body).ReadToEndAsync();
            var state = await runningInstances(client, context);
            JArray retVal = new JArray();
            foreach (var status in state)
            {
                var statusWithInput = await client.GetInstanceAsync(status.InstanceId, getInputsAndOutputs: true);
                JObject o = new JObject();
                o["instanceId"] = status.InstanceId;
                o["createdDateTime"] = status.CreatedAt;
                o["status"] = status.RuntimeStatus.ToString();
                TimeSpan span = (DateTime.UtcNow - status.CreatedAt);
                o["elapsedtimeinminutes"] = span.TotalMinutes;
                o["input"] = statusWithInput?.SerializedInput ?? "";
                retVal.Add(o);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(retVal.ToString());
            return response;
        }

        [Function("ExportBlobTrigger")]
        public async Task RunBlobTrigger([BlobTrigger("export-trigger/{name}", Connection = "FBI_STORAGEACCT_IDENTITY")] Stream myBlob, string name, [DurableClient] DurableTaskClient starter, FunctionContext context)
        {
            var log = context.GetLogger("ExportBlobTrigger");

            StreamReader reader = new StreamReader(myBlob);
            var text = await reader.ReadToEndAsync();
            var state = await runningInstances(starter, context);
            int running = state.Count();
            int maxinstances = Utils.GetIntEnvironmentVariable("FBI_MAXEXPORTS", "0");
            if (maxinstances > 0 && running >= maxinstances)
            {
                string msg = $"Unable to start export there are {running} exports the max concurrent allowed is {maxinstances}";
                log.LogError($"ExportBlobTrigger:{msg}");
                return;
            }
            string instanceId = await starter.ScheduleNewOrchestrationInstanceAsync("ExportOrchestrator", text);
            var bc = StorageUtils.GetCloudBlobClient(Utils.GetEnvironmentVariable("FBI_STORAGEACCT"));
            await StorageUtils.MoveTo(bc, "export-trigger", "export-trigger-processed", name, name, log);
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }
        public async Task<IEnumerable<OrchestrationMetadata>> runningInstances(DurableTaskClient client, FunctionContext context)
        {
           
            var queryFilter = new OrchestrationQuery
            {
                Statuses = new[]
                {
                    OrchestrationRuntimeStatus.Pending,
                    OrchestrationRuntimeStatus.Running
                }

            };
            var result = new List<OrchestrationMetadata>();
            await foreach (var item in client.GetAllInstancesAsync(queryFilter))
            {
                result.Add(item);
            }

            var retVal = new List<OrchestrationMetadata>();
            await foreach (var status in client.GetAllInstancesAsync(queryFilter))
            {
                if (!status.InstanceId.Contains(":") && !status.InstanceId.StartsWith("@"))
                {
                    retVal.Add(status);
                }
            }
            return retVal;

        }
        [Function("ExportHistoryCleanUp")]
        public static async Task CleanupOldRuns(
        [TimerTrigger("0 0 0 * * *")] TimerInfo timerInfo,
        [DurableClient] DurableTaskClient orchestrationClient,
        FunctionContext context)
        {
            var log = context.GetLogger("ExportHistoryCleanUp");
                var createdTimeFrom = DateTime.MinValue;
                var createdTimeTo = DateTime.UtcNow.Subtract(TimeSpan.FromDays(Utils.GetIntEnvironmentVariable("FBI_EXPORTPURGEAFTERDAYS", "30")));
                var runtimeStatus = new List<OrchestrationRuntimeStatus>
                {
                    OrchestrationRuntimeStatus.Completed,
                   OrchestrationRuntimeStatus.Canceled,
                   OrchestrationRuntimeStatus.Failed,
                   OrchestrationRuntimeStatus.Terminated
                };
                var result = await orchestrationClient.PurgeInstancesAsync(createdTimeFrom, createdTimeTo, runtimeStatus);
                log.LogInformation($"Scheduled cleanup done and instances deleted");
        }

    }
    
}