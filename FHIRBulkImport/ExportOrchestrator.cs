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

namespace FHIRBulkImport
{
    public static class ExportOrchestrator
    {
        private static JObject SetContextVariables(string instanceId, JToken token = null, string query = null, JArray include = null,string patientreffield=null)
        {
            JObject o = new JObject();
            o["instanceId"] = instanceId;
            if(token != null) o["resource"] = token;
            if (query != null) o["query"] = query;
            if (include != null) o["include"] = include;
            if (patientreffield != null) o["patientreffield"] = patientreffield;
            return o;
        }
       
        [FunctionName("ExportOrchestrator")]
        public static async Task<string> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            string inputs = context.GetInput<string>();
            JObject config = JObject.Parse(inputs);
            JObject retVal = new JObject();
            retVal["instanceid"] = context.InstanceId;
            retVal["configuration"] = config;
            retVal["started"] = System.DateTime.UtcNow;

            string query = config["query"].ToString();
            string patreffield = config["patientreferencefield"].ToString();
            JArray include = (JArray)config["include"];
            var rt = query.Split("?")[0];
            //get a list of N work items to process in parallel

            var tasks = new List<Task<JObject>>();
            var fhirresp = await context.CallActivityAsync<FHIRResponse>("QueryFHIR", query);
            if (fhirresp.Success && !string.IsNullOrEmpty(fhirresp.Content))
            {
                var resource = JObject.Parse(fhirresp.Content);
                //For group resource loop through the member array
                if (rt.StartsWith("Group"))
                {
                    JArray ga = (JArray)resource["member"];
                    if (!ga.IsNullOrEmpty())
                    {
                        int cnt = 0;
                        var bundle = ImportNDJSON.initBundle();
                        foreach (JToken t in ga)
                        {
                            string prv = (string)t["entity"]["reference"];
                            JObject o = new JObject();
                            o["resourceType"] = "GroupInternal";
                            o["entity"] = new JObject();
                            o["entity"]["reference"] = prv;
                            ImportNDJSON.addResource(bundle, o);
                            cnt++;
                            if (cnt % 50 == 0)
                            {
                                tasks.Add(context.CallSubOrchestratorAsync<JObject>("ExportOrchestrator_ProcessPatientQueryPage", SetContextVariables(context.InstanceId, bundle, query, include, "entity")));
                                bundle = ImportNDJSON.initBundle();                           
                            }
                        }
                        if (((JArray)bundle["entry"]).Count > 0)
                        {
                            tasks.Add(context.CallSubOrchestratorAsync<JObject>("ExportOrchestrator_ProcessPatientQueryPage", SetContextVariables(context.InstanceId, bundle, query, include, "entity")));
                        }

                    }
                } else {
                    //Page through query results fo everything else          
                    tasks.Add(context.CallSubOrchestratorAsync<JObject>("ExportOrchestrator_ProcessPatientQueryPage", SetContextVariables(context.InstanceId, resource, query, include, patreffield)));
                    bool nextlink = !resource["link"].IsNullOrEmpty() && ((string)resource["link"].getFirstField()["relation"]).Equals("next");
                    while (nextlink)
                    {
                        string nextpage = (string)resource["link"].getFirstField()["url"];
                        fhirresp = await context.CallActivityAsync<FHIRResponse>("QueryFHIR", nextpage);
                        if (!fhirresp.Success)
                        {
                            log.LogError($"ExportOrchestrator: FHIR Server Call Failed: {fhirresp.Status} Content:{fhirresp.Content} Query:{nextpage}");
                            nextlink = false;
                        }
                        else
                        {

                            resource = JObject.Parse(fhirresp.Content);
                            tasks.Add(context.CallSubOrchestratorAsync<JObject>("ExportOrchestrator_ProcessPatientQueryPage", SetContextVariables(context.InstanceId, resource, query, include)));
                            nextlink = !resource["link"].IsNullOrEmpty() && ((string)resource["link"].getFirstField()["relation"]).Equals("next");
                        }
                    }
                }
                await Task.WhenAll(tasks);
                var callResults = tasks
                    .Where(t => t.Status == TaskStatus.RanToCompletion)
                    .Select(t => t.Result);
                retVal["finished"] = System.DateTime.UtcNow;
                foreach (JObject j in callResults)
                {
                    foreach (JProperty property in j.Properties())
                    {
                        if (retVal[property.Name] != null)
                        {
                            int total = (int)retVal[property.Name];
                            total +=(int)property.Value;
                            retVal[property.Name] = total;
                        } else
                        {
                            retVal[property.Name] = property.Value;
                        }
                    }
                }
                string rm = retVal.ToString(Newtonsoft.Json.Formatting.None);
                await context.CallActivityAsync<bool>("AppendBlob", SetContextVariables(context.InstanceId, null, rm));
                log.LogInformation($"ExportOrchestrator:Instance {context.InstanceId} Completed:\r\n{rm}");
                return rm;
            }
            else
            {
                var m = $"ExportOrchestrator:Failed to communicate with FHIR Server: Status {fhirresp.Status} Response {fhirresp.Content}";
                log.LogError(m);
                return m;
            }
        }
        [FunctionName("AppendBlob")]
        public static async Task<bool> AppendBlob(
           [ActivityTrigger] JToken ctx,
           ILogger log)
        {
            string instanceid = (string)ctx["instanceId"];
            string rm = (string)ctx["query"];
            var appendBlobClient = await StorageUtils.GetAppendBlobClient(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"), $"export/{instanceid}", "_completed_run.xjson");
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(rm)))
            {
                await appendBlobClient.AppendBlockAsync(ms);
            }
            return true;
        }
        [FunctionName("QueryFHIR")]
        public static async Task<FHIRResponse> Run(
            [ActivityTrigger] string query,
            ILogger log)
        {

            var fhirresp = await FHIRUtils.CallFHIRServer(query, "", HttpMethod.Get, log);
            return fhirresp;
        }
       
        [FunctionName("ExportOrchestrator_ProcessPatientQueryPage")]
        public static async Task<JObject> ProcessPatientQueryPage([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            
            JObject retVal = new JObject();
            var vars = context.GetInput<JToken>();
            var resource = vars["resource"];
            string patreffield = (string)vars["patientreffield"];
            JArray include = (JArray)vars["include"];
            string sourcequery = (string)vars["query"];
            var rt = sourcequery.Split("?")[0];
            if (resource.FHIRResourceType().Equals("Bundle"))
            {
                var subtasks = new List<Task<string>>();
                HashSet<string> ids = new HashSet<string>();
                JArray arr = (JArray)resource["entry"];
                if (arr != null)
                {
                        foreach (JToken entry in arr)
                        {
                            var r = entry["resource"];
                            if (rt.Equals("Patient"))
                            {
                                ids.Add(r.FHIRResourceId());
                            }
                            else
                            {
                                if (!r[patreffield].IsNullOrEmpty())
                                {
                                    string patref = (string)r[patreffield]["reference"];
                                    if (patref != null && patref.StartsWith("Patient") && patref.IndexOf("/") > 0)
                                    {
                                        string pid = patref.Split("/")[1];
                                        ids.Add(pid);
                                    }
                                }
                            }
                        }
                }
                string qids = string.Join(",", ids);
                //Just use bundle to gather patient resources if source query is patient based
                if (rt.Equals("Patient"))
                    subtasks.Add(context.CallActivityAsync<string>("ExportOrchestrator_GatherResources", SetContextVariables(vars["instanceId"].ToString(), resource,"Patient")));
                if (include != null)
                {
                    foreach (JToken t in include)
                    {
                        string sq = t.ToString();
                        sq = sq.Replace("$IDS", qids);
                        subtasks.Add(context.CallActivityAsync<string>("ExportOrchestrator_GatherResources", SetContextVariables(vars["instanceId"].ToString(), null, sq)));
                    }
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
            
            return retVal;
        }
        [FunctionName("ExportOrchestrator_GatherResources")]
        public static async Task<string> GatherResources([ActivityTrigger] IDurableActivityContext context, ILogger log)
        {

            JToken vars = context.GetInput<JToken>();
            int total = 0;
            JToken resource = vars["resource"];
            string query = vars["query"].ToString();
            string instanceid = vars["instanceId"].ToString();
            var rt = query.Split("?")[0];
            var appendBlobClient = await StorageUtils.GetAppendBlobClient(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"), $"export/{instanceid}", rt + ".xndjson");
            if (resource != null)
            {
                total = total + await ConvertToNDJSON(resource, appendBlobClient);
            }
            else
            {
                var fhirresp = await FHIRUtils.CallFHIRServer(query, "", HttpMethod.Get, log);
                if (fhirresp.Success && !string.IsNullOrEmpty(fhirresp.Content))
                {

                    resource = JObject.Parse(fhirresp.Content);
                    total = total + await ConvertToNDJSON(resource, appendBlobClient);
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
                            total = total + await ConvertToNDJSON(resource, appendBlobClient);
                            nextlink = !resource["link"].IsNullOrEmpty() && ((string)resource["link"].getFirstField()["relation"]).Equals("next");
                        }
                    }
                } else
                {
                    log.LogError($"ExportOrchestrator: FHIR Server Call Failed: {fhirresp.Status} Content:{fhirresp.Content} Query:{query}");
                  
                }
            }
            return $"{rt}:{total}";
        }
       
        private static async Task<int> ConvertToNDJSON(JToken bundle, Azure.Storage.Blobs.Specialized.AppendBlobClient appendBlobClient)
        {
            int cnt = 0;
            StringBuilder sb = new StringBuilder();
            if (!bundle.IsNullOrEmpty() && bundle.FHIRResourceType().Equals("Bundle"))
            {
                JArray arr = (JArray)bundle["entry"];
                if (arr != null)
                {
                    foreach(JToken tok in arr)
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
                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())))
                {
                    await appendBlobClient.AppendBlockAsync(ms);
                }
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
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("ExportOrchestrator",null,config);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
        [FunctionName("ExportBlobTrigger")]
        public static async Task RunBlobTrigger([BlobTrigger("export-trigger/{name}", Connection = "FBI-STORAGEACCT")] Stream myBlob, string name, [DurableClient] IDurableOrchestrationClient starter, ILogger log)
        {

            StreamReader reader = new StreamReader(myBlob);
            var text = await reader.ReadToEndAsync();
            string instanceId = await starter.StartNewAsync("ExportOrchestrator", null, text);
            var bc = StorageUtils.GetCloudBlobClient(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"));
            await StorageUtils.MoveTo(bc, "export-trigger", "export-trigger-processed", name, name, log);
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }
    }
    
}