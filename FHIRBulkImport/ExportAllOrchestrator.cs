using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Linq;
using System;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace FHIRBulkImport
{
    public class ExportAllOrchestrator
    {
        private static int _exportResourceCount = Utils.GetIntEnvironmentVariable("FS-EXPORTRESOURCECOUNT", "1000");

        [FunctionName(nameof(ExportAll_RunOrchestrator))]
        public async Task<JObject> ExportAll_RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger logger)
        {
            JObject retVal = new JObject();
            retVal["instanceid"] = context.InstanceId;
            string inputs = context.GetInput<string>();
            var fileResourceCounts = new Dictionary<string, int>();

            JObject requestParameters;
            try
            {
                requestParameters = JObject.Parse(inputs);
            }
            catch (JsonReaderException jre)
            {
                retVal["error"] = $"Not a valid JSON Object from starter input:{jre.Message}";
                logger.LogError("ExportOrchestrator: Not a valid JSON Object from starter input");
                return retVal;
            }

            string resourceType = (string)requestParameters["resourceType"];

            if (resourceType is null)
            {
                retVal["error"] = "resourceType is empty";
                return retVal;
            }

            retVal["exportStarted"] = context.CurrentUtcDateTime;
            context.SetCustomStatus(retVal);

            logger.LogError($"ExportAllOrchestrator started at {context.CurrentUtcDateTime} with resource type {resourceType}.");

            DataPageResult? fhirResult;
            try
            {
                fhirResult = await ProcessPagedFhirExport(context, resourceType, $"{resourceType}?_count={_exportResourceCount}", fileResourceCounts, logger);
            }
            catch (Exception ex)
            {
                retVal["error"] = ex.Message;
                logger.LogError(ex.Message);
                return retVal;
            }

            retVal["exportFilesCompleted"] = fileResourceCounts.Keys.Count;
            retVal["exportResourceCount"] = fileResourceCounts.Values.Sum();
            context.SetCustomStatus(retVal);

            var retryOptions = new RetryOptions(firstRetryInterval: TimeSpan.FromSeconds(15), maxNumberOfAttempts: 3);

            while (fhirResult.Value.NextLink is not null)
            {
                logger.LogInformation($"NextLink: {fhirResult.Value.NextLink}");

                try
                {
                    fhirResult = await ProcessPagedFhirExport(context, resourceType, fhirResult.Value.NextLink, fileResourceCounts, logger);
                }
                catch (Exception ex)
                {
                    retVal["error"] = ex.Message;
                    logger.LogError(ex.Message);
                    return retVal;
                }

                retVal["exportFilesCompleted"] = fileResourceCounts.Keys.Count;
                retVal["exportResourceCount"] = fileResourceCounts.Values.Sum();
                context.SetCustomStatus(retVal);
            }

            retVal["exportCompleted"] = context.CurrentUtcDateTime;

            retVal["output"] = new JArray();
            foreach (var item in fileResourceCounts)
            {
                var fileInfo = new JObject();
                fileInfo["type"] = resourceType;
                fileInfo["url"] = item.Key;
                fileInfo["count"] = item.Value;
                ((JArray)retVal["output"]).Add(fileInfo);
            }

            await context.CallActivityAsync(nameof(ExportAllOrchestrator_UpdateStatus), retVal);

            logger.LogInformation($"Completed orchestration with ID = '{context.InstanceId}'.");
            return retVal;
        }

        private async Task<DataPageResult?> ProcessPagedFhirExport(IDurableOrchestrationContext context, string resourceType, string localPathAndQuery, Dictionary<string, int> fileResourceCounts, ILogger logger)
        {
            // Get the first result in the orchestrator since this query is not idempotent
            var fhirResult = await context.CallActivityAsync<DataPageResult?>(
                                        nameof(ExportAllOrchestrator_GetAndWriteDataPage),
                                        new DataPageRequest(LocalPathAndQuery: localPathAndQuery, context.InstanceId, ResourceType: resourceType));

            if (fhirResult is null)
            {
                string message = "Exiting as FHIR response from ExportAllOrchestrator_GetAndWriteDataPage is empty indicating failure. Check the logs for more information";
                throw new Exception("Exiting as FHIR response from ExportAllOrchestrator_GetAndWriteDataPage is empty indicating failure. Check the logs for more information");
            }

            if (fileResourceCounts.ContainsKey(fhirResult.Value.BlobUri))
            {
                fileResourceCounts[fhirResult.Value.BlobUri] = fileResourceCounts[fhirResult.Value.BlobUri] + fhirResult.Value.ResourceCount;
            }
            else
            {
                fileResourceCounts[fhirResult.Value.BlobUri] = fhirResult.Value.ResourceCount;
            }

            return fhirResult;
        }

        [FunctionName(nameof(ExportAllOrchestrator_GetAndWriteDataPage))]
        public async Task<DataPageResult?> ExportAllOrchestrator_GetAndWriteDataPage(
            [ActivityTrigger] DataPageRequest req,
            [DurableClient] IDurableEntityClient ec,
            ILogger logger)
        {

            var response = await FHIRUtils.CallFHIRServer(req.LocalPathAndQuery, "", HttpMethod.Get, logger);

            if (response.Success && !string.IsNullOrEmpty(response.Content))
            {
                var result = JObject.Parse(response.Content);
                var nextLinkObject = result["link"].FirstOrDefault(link => (string)link["relation"] == "next");
                var nextLinkUrl = nextLinkObject != null ? nextLinkObject.Value<string>("url") : null;

                var ndjsonResult = await ExportOrchestrator.ConvertToNDJSON(result, req.InstanceId, req.ResourceType, ec, logger);

                if (ndjsonResult is null)
                {
                    throw new Exception("ndjsonResult is null unexpectedly");
                }

                DataPageResult? retVal = new DataPageResult(nextLinkUrl, ndjsonResult.Value.ResourceCount, ndjsonResult.Value.BlobUrl);
                logger.LogInformation($"ExportAll: FHIR Server Call Succeeded: {response.Status} Next: {nextLinkUrl}, Count: {ndjsonResult.Value.ResourceCount}");
                return retVal;
            }
            
            logger.LogError($"ExportAll: FHIR Server Call Failed: {response.Status} Content:{response.Content} Query:{req.LocalPathAndQuery}");
            return null;
        }

        
        [FunctionName(nameof(ExportAllOrchestrator_HttpStart))]
        public async Task<HttpResponseMessage> ExportAllOrchestrator_HttpStart(
            [HttpTrigger(AuthorizationLevel.Function,"post",Route = "$alt-export-all")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {

            string requestParameters = await req.Content.ReadAsStringAsync();
            var state  = await ExportOrchestrator.runningInstances(starter, log);
            int running = state.Count();
            int maxinstances = Utils.GetIntEnvironmentVariable("FBI-MAXEXPORTS", "0");
            if (maxinstances > 0 && running >= maxinstances)
            {
                string msg = $"Unable to start export there are {running} exports the max concurrent allowed is {maxinstances}";
                StringContent sc = new StringContent("{\"error\":\"" + msg + "\"");
                return new HttpResponseMessage() { Content = sc, StatusCode = System.Net.HttpStatusCode.TooManyRequests};
            }
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync(nameof(ExportAll_RunOrchestrator), null, requestParameters);
            
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName(nameof(ExportAllOrchestrator_UpdateStatus))]
        public async Task ExportAllOrchestrator_UpdateStatus(
           [ActivityTrigger] JObject ctx,
           ILogger log)
        {
            string instanceId = (string)ctx["instanceId"];

            var blobClient = StorageUtils.GetCloudBlobClient(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"));
            await StorageUtils.WriteStringToBlob(blobClient, $"export/{instanceId}", "_completed_run.json", ctx.ToString(), log);
        }
    }

    public record struct DataPageRequest(string LocalPathAndQuery, string InstanceId, string ResourceType);

    public record struct DataPageResult(string NextLink, int ResourceCount, string BlobUri);

    public record struct ResultUpdateRequest(string InstanceId, string ResourceType, int ResourceCount, string NextLink);
}