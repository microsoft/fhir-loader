using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Linq;
using System;
using System.Threading;
using System.Net.Http.Headers;
using DurableTask.Core;
using Azure.Storage.Blobs.Specialized;
using System.ComponentModel;

namespace FHIRBulkImport
{
    public static class ExportOrchestrator
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
        [FunctionName("CountFileLines")]
        public static async Task<JObject> CountFileLines(
        [ActivityTrigger] JObject ctx,
        ILogger log)
        {
            string instanceid = (string)ctx["instanceid"];
            string blob = (string)ctx["filename"];
            return await FileHolderManager.CountLinesInBlob(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"),instanceid, blob,log);
        }
        [FunctionName("FileNames")]
        public static async Task<List<string>> FileNames(
         [ActivityTrigger] string instanceid,
         ILogger log)
        {
            return await FileHolderManager.GetFileNames(instanceid,log);
        }
        [FunctionName("ExportOrchestrator")]
        public static async Task<JArray> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            
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
        [FunctionName("AppendBlob")]
        public static async Task<bool> AppendBlob(
           [ActivityTrigger] JToken ctx,
           ILogger log)
        {
            string instanceid = (string)ctx["instanceId"];
            string rm = (string)ctx["ids"];
            var appendBlobClient = await StorageUtils.GetAppendBlobClient(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"), $"export/{instanceid}", "_completed_run.json");
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(rm)))
            {
                await appendBlobClient.AppendBlockAsync(ms);
            }
            return true;
        }
        [FunctionName("QueryFHIR")]
        public static async Task<HashSet<string>> QueryFHIR(
            [ActivityTrigger] IDurableActivityContext context,
            ILogger log)
        {
            
            HashSet<string> uniquePats = new HashSet<string>();
            try
            {
                JToken vars = context.GetInput<JToken>();
                string query = vars["query"].ToString();
                string instanceid = vars["instanceid"].ToString();
                string patreffield = (string)vars["patreffield"];
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
        [FunctionName("ExportOrchestrator_ProcessPatientQueryPage")]
        public static async Task<JObject> ProcessPatientQueryPage([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
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
        [FunctionName("FileTracker")]
        public static void FileTracker ([EntityTrigger] IDurableEntityContext ctx,ILogger log)
        {
            
            switch (ctx.OperationName.ToLowerInvariant())
            {
                //Set File Number
                case "set":
                    ctx.SetState(ctx.GetInput<int>());
                    break;
                case "get":
                    ctx.Return(ctx.GetState<int>());
                    break;
            }
        }
        [FunctionName("ExportOrchestrator_GatherResources")]
        public static async Task<string> GatherResources([ActivityTrigger] IDurableActivityContext context, [DurableClient] IDurableEntityClient entityclient, ILogger log)
        {
           

            JToken vars = context.GetInput<JToken>();
            int total = 0;
            string query = vars["ids"].ToString();
            string instanceid = vars["instanceid"].ToString();
            var rt = (string)vars["resourcetype"];
            var fhirresp = await FHIRUtils.CallFHIRServer(query, "", HttpMethod.Get, log);
            if (fhirresp.Success && !string.IsNullOrEmpty(fhirresp.Content))
            {

                    var resource = JObject.Parse(fhirresp.Content);
                    total = total + await ConvertToNDJSON(resource,instanceid,rt,entityclient,log);
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
                            total = total + await ConvertToNDJSON(resource, instanceid, rt, entityclient, log);
                        nextlink = !resource["link"].IsNullOrEmpty() && ((string)resource["link"].getFirstField()["relation"]).Equals("next");
                        }
                    }
            } else
            {
                log.LogError($"ExportOrchestrator: FHIR Server Call Failed: {fhirresp.Status} Content:{fhirresp.Content} Query:{query}");
            }
            return $"{rt}:{total}";
        }
       
        private static async Task<int> ConvertToNDJSON(JToken bundle, string instanceId, string resourceType, IDurableEntityClient entityclient,ILogger log)
        {
          
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
                    string key = $"{instanceId}-{resourceType}";
                    var entityId = new EntityId(nameof(FileTracker), key);
                    var esresp = await entityclient.ReadEntityStateAsync<int>(entityId);
                    int fileno = esresp.EntityState;
                    var filename = resourceType + "-" + (fileno + 1) + ".ndjson";
                    var blobclient = StorageUtils.GetAppendBlobClientSync(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"), $"export/{instanceId}", filename);
                    long maxfilesizeinbytes = Utils.GetIntEnvironmentVariable("FBI-MAXFILESIZEMB", "-1") * 1024000;
                    int bytestoadd = System.Text.ASCIIEncoding.UTF8.GetByteCount(sb.ToString());
                    var props = blobclient.GetProperties();
                    long filetotalbytes = props.Value.ContentLength + bytestoadd;
                    if (props.Value.BlobCommittedBlockCount > 49500 || (maxfilesizeinbytes > 0 && filetotalbytes >= maxfilesizeinbytes))
                    {
                        fileno++;
                        filename = resourceType + "-" + (fileno + 1) + ".ndjson";
                        blobclient = StorageUtils.GetAppendBlobClientSync(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"), $"export/{instanceId}", filename);
                        await entityclient.SignalEntityAsync(entityId, "set", fileno);
                    }
                    var rslt = await FileHolderManager.WriteAppendBlobAsync(blobclient, sb.ToString(), log);
                }
                
            }
            catch (Exception e)
            {
                log.LogError($"ExportNDJSON Exception: {e.Message}\r\n{e.StackTrace}");
            }
            return cnt;
        }
        [FunctionName("ExportOrchestrator_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function,"post",Route = "$alt-export")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {

            string config = await req.Content.ReadAsStringAsync();
            var state  = await runningInstances(starter, log);
            int running = state.Count();
            int maxinstances = Utils.GetIntEnvironmentVariable("FBI-MAXEXPORTS", "0");
            if (maxinstances > 0 && running >= maxinstances)
            {
                string msg = $"Unable to start export there are {running} exports the max concurrent allowed is {maxinstances}";
                StringContent sc = new StringContent("{\"error\":\"" + msg + "\"");
                return new HttpResponseMessage() { Content = sc, StatusCode = System.Net.HttpStatusCode.TooManyRequests};
            }
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("ExportOrchestrator",null,config);
            
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
        [FunctionName("ExportOrchestrator_InstanceAction")]
        public static async Task<HttpResponseMessage> InstanceAction(
          [HttpTrigger(AuthorizationLevel.Function, "get", Route = "$alt-export-manage/{instanceid}")] HttpRequestMessage req,
          [DurableClient] IDurableOrchestrationClient starter,string instanceid,
          ILogger log)
        {

            var parms = System.Web.HttpUtility.ParseQueryString(req.RequestUri.Query);
            string action = parms["action"];
            await starter.TerminateAsync(instanceid, "Terminated by User");
            StringContent sc = new StringContent($"Terminated {instanceid}");
            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            response.Content = sc;
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            return response;
        }
        [FunctionName("ExportOrchestrator_ExportStatus")]
        public static async Task<HttpResponseMessage> ExportStatus(
           [HttpTrigger(AuthorizationLevel.Function, "get", Route = "$alt-export-status")] HttpRequestMessage req,
           [DurableClient] IDurableOrchestrationClient starter,
           ILogger log)
        {
            string config = await req.Content.ReadAsStringAsync();
            var state = await runningInstances(starter, log);
            JArray retVal = new JArray();
            foreach (DurableOrchestrationStatus status in state)
            {
                JObject o = new JObject();
                o["instanceId"] = status.InstanceId;
                o["createdDateTime"] = status.CreatedTime;
                o["status"] = status.RuntimeStatus.ToString();
                TimeSpan span = (DateTime.UtcNow - status.CreatedTime);
                o["elapsedtimeinminutes"] = span.TotalMinutes;
                o["input"] = status.Input;
                retVal.Add(o);
            }
            StringContent sc = new StringContent(retVal.ToString());
            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            response.Content = sc;
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return response;
        }
        [FunctionName("ExportBlobTrigger")]
        public static async Task RunBlobTrigger([BlobTrigger("export-trigger/{name}", Connection = "FBI-STORAGEACCT")] Stream myBlob, string name, [DurableClient] IDurableOrchestrationClient starter, ILogger log)
        {

            StreamReader reader = new StreamReader(myBlob);
            var text = await reader.ReadToEndAsync();
            var state = await runningInstances(starter, log);
            int running = state.Count();
            int maxinstances = Utils.GetIntEnvironmentVariable("FBI-MAXEXPORTS", "0");
            if (maxinstances > 0 && running >= maxinstances)
            {
                string msg = $"Unable to start export there are {running} exports the max concurrent allowed is {maxinstances}";
                log.LogError($"ExportBlobTrigger:{msg}");
                return;
            }
            string instanceId = await starter.StartNewAsync("ExportOrchestrator", null, text);
            var bc = StorageUtils.GetCloudBlobClient(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"));
            await StorageUtils.MoveTo(bc, "export-trigger", "export-trigger-processed", name, name, log);
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }
        public static async Task<IEnumerable<DurableOrchestrationStatus>> runningInstances(IDurableOrchestrationClient client,ILogger log)
        {
            var queryFilter = new OrchestrationStatusQueryCondition
            {
                RuntimeStatus = new[]
                {
                    OrchestrationRuntimeStatus.Pending,
                    OrchestrationRuntimeStatus.Running
                }
                
            };

            OrchestrationStatusQueryResult result = await client.ListInstancesAsync(
                queryFilter,
                CancellationToken.None);
            var retVal = new List<DurableOrchestrationStatus>();
            foreach (DurableOrchestrationStatus status in result.DurableOrchestrationState)
            {
                if (!status.InstanceId.Contains(":") && !status.InstanceId.StartsWith("@"))
                {
                    retVal.Add(status);
                }
            }
            return retVal;
            
        }
        [FunctionName("ExportHistoryCleanUp")]
        public static async Task CleanupOldRuns(
        [TimerTrigger("0 0 0 * * *")] TimerInfo timerInfo,
        [DurableClient] IDurableOrchestrationClient orchestrationClient,
        ILogger log)
        {
                var createdTimeFrom = DateTime.MinValue;
                var createdTimeTo = DateTime.UtcNow.Subtract(TimeSpan.FromDays(Utils.GetIntEnvironmentVariable("FBI-EXPORTPURGEAFTERDAYS", "30")));
                var runtimeStatus = new List<OrchestrationStatus>
                {
                    OrchestrationStatus.Completed,
                    OrchestrationStatus.Canceled,
                    OrchestrationStatus.Failed,
                    OrchestrationStatus.Terminated
                };
                var result = await orchestrationClient.PurgeInstanceHistoryAsync(createdTimeFrom, createdTimeTo, runtimeStatus);
                log.LogInformation($"Scheduled cleanup done, {result.InstancesDeleted} instances deleted");
        }

    }
    
}