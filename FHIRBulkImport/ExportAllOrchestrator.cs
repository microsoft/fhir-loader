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
        private static string _storageAccount = Utils.GetEnvironmentVariable("FBI-STORAGEACCT");
        private static int _maxInstances = Utils.GetIntEnvironmentVariable("FBI-MAXEXPORTS", "0");
        private static RetryOptions _exportAllRetryOptions = new RetryOptions(firstRetryInterval: TimeSpan.FromSeconds(5), maxNumberOfAttempts: 5)
                                                                 { BackoffCoefficient = 2  };
        [FunctionName(nameof(ExportAll_RunOrchestrator))]
        public async Task<JObject> ExportAll_RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger logger)
        {
            // Setup function level variables.
            JObject retVal = new JObject();
            retVal["instanceid"] = context.InstanceId;
            string inputs = context.GetInput<string>();
            var fileTracker = new Dictionary<string, int>();

            // Get the input from the request to the function.
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

            // We only allow execution for a certain resource type.
            string resourceType = (string)requestParameters["_type"];

            if (resourceType is null)
            {
                retVal["error"] = "resourceType is empty";
                return retVal;
            }

            // Get and validate the time range for the export if given.
            string sinceStr = (string)requestParameters["_since"], tillStr = (string)requestParameters["_till"];
            DateTime since = default, till = default;

            if ((sinceStr is not null && !DateTime.TryParse(sinceStr, out since)) ||
                (tillStr is not null && !DateTime.TryParse(tillStr, out till)))
            {
                string message = $"Invalid input for _since  or _till parameter. _since: {sinceStr ?? string.Empty} _till: {tillStr ?? string.Empty}";
                retVal["error"] = message;
                logger.LogError(message);
                return retVal;
            }

            string baseAddress = ((string)requestParameters["_baseAddress"]);
            if (baseAddress is null)
            {
                baseAddress = string.Empty;
            }
            else if (!baseAddress.EndsWith('/'))
            {
                baseAddress = baseAddress + "/";
            }

            string audience = ((string)requestParameters["_audience"]);

            // Signal function start in the durable task status object.
            retVal["exportStarted"] = context.CurrentUtcDateTime;
            context.SetCustomStatus(retVal);

            // Setup our first execution query to get the given resource with the configured page size.
            string nextLink = $"{baseAddress}{resourceType}?_count={_exportResourceCount}";

            // Add custom date range if given.
            if (since != default)
            {
                nextLink += $"&_lastUpdated=ge{since.ToString("o")}";
            }
            if (till != default)
            {
                nextLink += $"&_lastUpdated=lt{till.ToString("o")}";
            }

            // Loop until no continuation token is returned.
            while (nextLink is not null)
            {
                logger.LogInformation($"Fetching page of resources using query ${nextLink}");

                DataPageResult fhirResult = await context.CallActivityWithRetryAsync<DataPageResult>(
                                            nameof(ExportAllOrchestrator_GetAndWriteDataPage),
                                            _exportAllRetryOptions,
                                            new DataPageRequest(FhirRequestPath: nextLink, InstanceId: context.InstanceId, ResourceType: resourceType, Audience: audience));

                // Keep track of how many files we export and the number of resources per file.
                if (fhirResult.ResourceCount == 0)
                {
                    retVal["error"] = $"Zero result bundle returnd for query {nextLink}. Ensure your inputs will return data.";
                }
                else if (fileTracker.ContainsKey(fhirResult.BlobUrl))
                {
                    fileTracker[fhirResult.BlobUrl] = fileTracker[fhirResult.BlobUrl] + fhirResult.ResourceCount;
                }
                else
                {
                    fileTracker[fhirResult.BlobUrl] = fhirResult.ResourceCount;
                }

                // Subsequent executions will use the continuation token.
                nextLink = fhirResult.NextLink;

                // Attempt to add output array - untested.
                retVal["output"] = new JArray();
                foreach (var item in fileTracker)
                {
                    var fileInfo = new JObject();
                    fileInfo["type"] = resourceType;
                    fileInfo["url"] = item.Key;
                    fileInfo["count"] = item.Value;
                    ((JArray)retVal["output"]).Add(fileInfo);
                }

                // Update durable function status
                retVal["exportFilesCompleted"] = fileTracker.Keys.Count;
                retVal["exportResourceCount"] = fileTracker.Values.Sum();
                context.SetCustomStatus(retVal);
            }

            // Report completed export
            retVal["exportCompleted"] = context.CurrentUtcDateTime;

            // Save the status in blob storage next to the exported files.
            await context.CallActivityAsync(
                nameof(ExportAllOrchestrator_WriteCompletionStatusToBlob), 
                new WriteCompletionStatusRequest(context.InstanceId, retVal));

            // Remove details from the custom status to avoid payload bloat / duplication.
            var completedStatus = new JObject();
            completedStatus["status"] = "Success";
            context.SetCustomStatus(completedStatus);

            logger.LogInformation($"Completed orchestration with ID = '{context.InstanceId}'.");
            return retVal;
        }

        [FunctionName(nameof(ExportAllOrchestrator_GetAndWriteDataPage))]
        public async Task<DataPageResult> ExportAllOrchestrator_GetAndWriteDataPage(
            [ActivityTrigger] DataPageRequest req,
            [DurableClient] IDurableEntityClient ec,
            ILogger logger)
        {

            var response = await FHIRUtils.CallFHIRServer(req.FhirRequestPath, "", HttpMethod.Get, logger, req.Audience);

            if (response.Success && !string.IsNullOrEmpty(response.Content))
            {
                // Parse the content and try to find the continuation token.
                var result = JObject.Parse(response.Content);
                var nextLinkObject = result["link"].FirstOrDefault(link => (string)link["relation"] == "next");
                var nextLinkUrl = nextLinkObject != null ? nextLinkObject.Value<string>("url") : null;

                // Write bundle resources to NDJSON using the file manager - this will prevent many small files.
                var ndjsonResult = await ExportOrchestrator.ConvertToNDJSON(result, req.InstanceId, req.ResourceType, ec, logger);

                // Placeholder in case any issues are found writing to NDJSON.
                if (ndjsonResult is null)
                {
                    throw new Exception("ndjsonResult is null unexpectedly");
                }

                logger.LogInformation($"ExportAll: FHIR Server Call Succeeded: {response.Status} Next: {nextLinkUrl}, Count: {ndjsonResult.Value.ResourceCount}");

                return new DataPageResult(nextLinkUrl, ndjsonResult.Value.ResourceCount, ndjsonResult.Value.BlobUrl, null);
            }
            
            string message = $"ExportAll: FHIR Server Call Failed: {response.Status} Content:{response.Content} Query:{req.FhirRequestPath}";
            logger.LogError(message);
            throw new Exception(message);
        }

        
        [FunctionName(nameof(ExportAllOrchestrator_HttpStart))]
        public async Task<HttpResponseMessage> ExportAllOrchestrator_HttpStart(
            [HttpTrigger(AuthorizationLevel.Function,"post", Route = "$alt-export-all")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            string requestParameters = await req.Content.ReadAsStringAsync();
            var state  = await ExportOrchestrator.runningInstances(starter, log);
            int running = state.Count();

            if (_maxInstances > 0 && running >= _maxInstances)
            {
                string msg = $"Unable to start export there are {running} exports the max concurrent allowed is {_maxInstances}";
                StringContent sc = new StringContent("{\"error\":\"" + msg + "\"");
                return new HttpResponseMessage() { Content = sc, StatusCode = System.Net.HttpStatusCode.TooManyRequests};
            }

            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync(nameof(ExportAll_RunOrchestrator), null, requestParameters);
            
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName(nameof(ExportAllOrchestrator_WriteCompletionStatusToBlob))]
        public async Task ExportAllOrchestrator_WriteCompletionStatusToBlob(
           [ActivityTrigger] WriteCompletionStatusRequest req,
           ILogger log)
        {
            var blobClient = StorageUtils.GetCloudBlobClient(_storageAccount);
            await StorageUtils.WriteStringToBlob(blobClient, $"export/{req.InstanceId}", "_completed_run.json", req.StatusObject.ToString(), log);
        }
    }

    public record struct DataPageRequest(string FhirRequestPath, string InstanceId, string ResourceType, string Audience);

    public record struct DataPageResult(string NextLink, int ResourceCount, string BlobUrl, string ErrorMessage);

    public record struct ResultUpdateRequest(string InstanceId, string ResourceType, int ResourceCount, string NextLink);

    public record struct WriteCompletionStatusRequest(string InstanceId, JObject StatusObject);
}