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
        private static int _maxParallelizationCount = 100;
        private static int _parallelSearchBundleSize = _maxInstances = Utils.GetIntEnvironmentVariable("FBI-PARALLELSEARCHBUNDLESIZE", "50");
        private static RetryOptions _exportAllRetryOptions = new RetryOptions(firstRetryInterval: TimeSpan.FromSeconds(Utils.GetIntEnvironmentVariable("FBI-EXPORTALLRETRYINTERVAL", "30")), maxNumberOfAttempts: 5)
                                                                 { BackoffCoefficient = Utils.GetIntEnvironmentVariable("FBI-EXPORTALLBACKOFFCOEFFICIENT", "3") };

        [FunctionName(nameof(ExportAllOrchestrator_HttpStart))]
        public async Task<HttpResponseMessage> ExportAllOrchestrator_HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "$alt-export-all")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            string requestParameters = await req.Content.ReadAsStringAsync();
            var state = await ExportOrchestrator.runningInstances(starter, log);
            int running = state.Count();

            if (_maxInstances > 0 && running >= _maxInstances)
            {
                string msg = $"Unable to start export there are {running} exports the max concurrent allowed is {_maxInstances}";
                StringContent sc = new StringContent("{\"error\":\"" + msg + "\"");
                return new HttpResponseMessage() { Content = sc, StatusCode = System.Net.HttpStatusCode.TooManyRequests };
            }

            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync(nameof(ExportAll_RunOrchestrator), null, requestParameters);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName(nameof(ExportAll_RunOrchestrator))]
        public async Task<JObject> ExportAll_RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger logger)
        {
            // Setup function level variables.
            JObject retVal = new JObject();
            retVal["instanceid"] = context.InstanceId;
            var fileTracker = new Dictionary<string, int>();
            ExportAllOrchestratorOptions options;

            try
            {
                string inputJson = context.GetInput<string>();
                options = ParseOptions(inputJson, context.CurrentUtcDateTime);
                logger.LogInformation($"Got options: {JsonConvert.SerializeObject(options)}");
            }
            catch (Exception ex)
            {
                retVal["error"] = $"Error while parsing body. Please check the inputs and try again. Error: {ex.Message}. Trace: {ex.StackTrace}";
                return retVal;
            }

            // If we aren't running in parallel, seed the ranges with a single value for the request.
            List<(DateTime? Start, DateTime? End)> searchRanges = new()
            {
                (options.Since, options.Till)
            };

            // For parallel requests, we need to find  ranges.
            if (options.ParallelSearchRanges is not null && options.ParallelizationCount is not null)
            {
                // Signal function start in the durable task status object.
                retVal["parallelizationLogicStarted"] = context.CurrentUtcDateTime;
                context.SetCustomStatus(retVal);

                var parallelizationTasks = options.ParallelSearchRanges.Select(
                    x => context.CallActivityWithRetryAsync<List<(DateTime Start, DateTime End, int Count)>> (
                        nameof(ExportAllOrchestrator_GetCountsForListOfDateRanges),
                        _exportAllRetryOptions,
                        new GetCountsForListOfDateRangesRequest(options.ResourceType, x.Select(y => (y.Start, y.End, -1)).ToList())));

                var results = await Task.WhenAll(parallelizationTasks);

                var flattenedResult = FlattenCountsByParallelizationCount(results, options.ParallelizationCount.Value);
                searchRanges = flattenedResult.Select(x => ((DateTime?)x.Start, (DateTime?)x.End)).ToList();
            }

            // Signal function start in the durable task status object.
            retVal["exportStarted"] = context.CurrentUtcDateTime;
            context.SetCustomStatus(retVal);

            List<Task<DataPageResult>> exportTasks = new();
            for (int i = 0; i < searchRanges.Count; i++)
            {
                string nextLink = $"{options.BaseAddress}{options.ResourceType}?_count={_exportResourceCount}";

                // Add custom date range if given.
                if (searchRanges[i].Start is not null)
                {
                    nextLink += $"&_lastUpdated=ge{searchRanges[i].Start.Value.ToString("o")}";
                }
                if (searchRanges[i].End is not null)
                {
                    nextLink += $"&_lastUpdated=lt{searchRanges[i].End.Value.ToString("o")}";
                }

                exportTasks.Add(context.CallActivityWithRetryAsync<DataPageResult>(
                            nameof(ExportAllOrchestrator_GetAndWriteDataPage),
                            _exportAllRetryOptions,
                            new DataPageRequest(FhirRequestPath: nextLink, InstanceId: $"{context.InstanceId}-{i}", ResourceType: options.ResourceType, Audience: options.Audience)));
            }

            // Continue as long as there are pending tasks
            while (exportTasks.Count > 0)
            {
                // Wait for the next task to complete
                Task<DataPageResult> completedTask = await Task.WhenAny(exportTasks);

                // Remove the completed task from the list
                exportTasks.Remove(completedTask);

                // Get the result of the completed sub-orchestrator
                DataPageResult fhirResult = await completedTask;

                if (fhirResult.ResourceCount < 0)
                {
                    retVal["error"] = fhirResult.Error;
                    context.SetCustomStatus(retVal);
                    return retVal;
                }

                if (fileTracker.ContainsKey(fhirResult.BlobUrl))
                {
                    fileTracker[fhirResult.BlobUrl] = fileTracker[fhirResult.BlobUrl] + fhirResult.ResourceCount;
                }
                else
                {
                    fileTracker[fhirResult.BlobUrl] = fhirResult.ResourceCount;
                }

                // Attempt to add output array - untested.
                retVal["output"] = new JArray();
                foreach (var item in fileTracker)
                {
                    var fileInfo = new JObject();
                    fileInfo["type"] = options.ResourceType;
                    fileInfo["url"] = item.Key;
                    fileInfo["count"] = item.Value;
                    ((JArray)retVal["output"]).Add(fileInfo);
                }

                // Update durable function status
                retVal["exportFilesCompleted"] = fileTracker.Keys.Count;
                retVal["exportResourceCount"] = fileTracker.Values.Sum();
                context.SetCustomStatus(retVal);

                if (fhirResult.NextLink != null)
                {
                    exportTasks.Add(context.CallActivityWithRetryAsync<DataPageResult>(
                                nameof(ExportAllOrchestrator_GetAndWriteDataPage),
                                _exportAllRetryOptions,
                                new DataPageRequest(FhirRequestPath: fhirResult.NextLink, InstanceId: fhirResult.InstanceId, ResourceType: options.ResourceType, Audience: options.Audience)));
                }
            }

            // Report completed export
            retVal["exportCompleted"] = context.CurrentUtcDateTime;

            // Remove details from the custom status to avoid payload bloat / duplication.
            var completedStatus = new JObject();
            completedStatus["status"] = "Success";
            context.SetCustomStatus(completedStatus);

            logger.LogInformation($"Completed orchestration with ID = '{context.InstanceId}'.");
            return retVal;
        }

        [FunctionName(nameof(ExportAllOrchestrator_GetCountsForListOfDateRanges))]
        public async Task<List<(DateTime Start, DateTime End, int Count)>> ExportAllOrchestrator_GetCountsForListOfDateRanges(
            [ActivityTrigger] GetCountsForListOfDateRangesRequest input,
            ILogger logger)
        {
            JObject requestBody = new();
            requestBody["resourceType"] = "Bundle";
            requestBody["type"] = "batch";

            JArray entries = new();

            foreach (var item in input.SearchRangeList)
            {
                JObject request = new JObject();
                request["method"] = "GET";
                request["url"] = $"{input.ResourceType}?_lastUpdated=ge{item.Start.ToString("o")}&_lastUpdated=lt{item.End.ToString("o")}&_summary=count";

                JObject entry = new JObject();
                entry["request"] = request;

                entries.Add(entry);
            }

            requestBody["entry"] = entries;

            logger.LogInformation($"Running parallelization logic bundle: {requestBody}");

            var response = await FHIRUtils.CallFHIRServer("", requestBody.ToString(), HttpMethod.Post, logger, null);

            logger.LogInformation($"Parallelization logic response: {response.Content}");

            if (response.Success && !string.IsNullOrEmpty(response.Content))
            {
                List<(DateTime start, DateTime end, int count)> resp = new();

                var result = JObject.Parse(response.Content);

                var entry = result["entry"];

                if (entry is null || entry is not JArray entryArray || input.SearchRangeList.Count != ((JArray)entry).Count)
                {
                    string exceptionMessage = $"Did not get matching result set back for {nameof(ExportAllOrchestrator_GetCountsForListOfDateRanges)}. EntryExists: {entry is not null}";
                    if (entry is not null)
                    {
                        exceptionMessage += $", Entry Type: {entry.GetType()}";
                    }

                    if (entry.GetType() == typeof(JArray))
                    {
                        exceptionMessage += $", EntryCount: {((JArray)entry).Count}";
                    }
                    throw new Exception(exceptionMessage);
                }

                for (int i = 0; i < input.SearchRangeList.Count; i++)
                {
                    var singleEntry = ((JArray)entry)[i];

                    resp.Add((input.SearchRangeList[i].Start, input.SearchRangeList[i].End, (int)singleEntry["resource"]["total"]));
                }

                return resp;
            }

            string message = $"{nameof(ExportAllOrchestrator_GetCountsForListOfDateRanges)}: FHIR Server Call Failed: {response.Status} Content:{response.Content}";
            logger.LogError(message);
            throw new Exception(message);
        }

        [FunctionName(nameof(ExportAllOrchestrator_GetAndWriteDataPage))]
        public async Task<DataPageResult> ExportAllOrchestrator_GetAndWriteDataPage(
            [ActivityTrigger] DataPageRequest input,
            [DurableClient] IDurableEntityClient ec,
            ILogger logger)
        {
            logger.LogInformation($"Fetching page of resources using query {input.FhirRequestPath}");

            var response = await FHIRUtils.CallFHIRServer(input.FhirRequestPath, "", HttpMethod.Get, logger, input.Audience);

            if (response.Success && !string.IsNullOrEmpty(response.Content))
            {
                // Parse the content and try to find the continuation token.
                var result = JObject.Parse(response.Content);

                if (((JArray)result["entry"]).Count < 1)
                {
                    throw new Exception($"Zero result bundle returned for query {input.FhirRequestPath}. Ensure your inputs will return data.");
                }

                var nextLinkObject = result["link"].FirstOrDefault(link => (string)link["relation"] == "next");
                var nextLinkUrl = nextLinkObject != null ? nextLinkObject.Value<string>("url") : null;

                // Write bundle resources to NDJSON using the file manager - this will prevent many small files.
                ExportOrchestrator.ConvertToNDJSONResponse? ndjsonResult = null;

                try
                {
                    ndjsonResult = await ExportOrchestrator.ConvertToNDJSON(result, input.InstanceId, input.ResourceType, ec, logger);
                }
                catch (Exception ex)
                {
                    string exceptionMessage = $"Unhandled error occurred in ConvertToNDJSON. Exception: {ex.Message}, InnerException: {ex.InnerException.Message}, Trace: {ex.StackTrace}";
                    return new DataPageResult(null, -1, null, input.InstanceId, exceptionMessage);
                }

                // Placeholder in case any issues are found writing to NDJSON.
                if (ndjsonResult is null)
                {
                    return new DataPageResult(null, -1, null, input.InstanceId, "ConvertToNDJSON returned null unexpectedly");
                }

                logger.LogInformation($"ExportAll: FHIR Server Call Succeeded: {response.Status} Next: {nextLinkUrl}, Count: {ndjsonResult.Value.ResourceCount}");

                return new DataPageResult(nextLinkUrl, ndjsonResult.Value.ResourceCount, ndjsonResult.Value.BlobUrl, input.InstanceId, null);
            }
            
            string message = $"ExportAll: FHIR Server Call Failed: {response.Status} Content:{response.Content} Query:{input.FhirRequestPath}";
            logger.LogError(message);
            return new DataPageResult(null, -1, null, input.InstanceId, message); ;
        }

        private static List<List<(DateTime start, DateTime end)>> GetSearchRanges(DateTime start, DateTime end, int rangeSizeInSeconds)
        {
            List<(DateTime start, DateTime end)> searchRanges = new();

            for (DateTime currentStart = start; currentStart < end; currentStart = currentStart.AddSeconds(rangeSizeInSeconds))
            {
                DateTime currentEnd = currentStart.AddSeconds(rangeSizeInSeconds);

                searchRanges.Add((currentStart, currentEnd));

                if (currentEnd >= end)
                {
                    break;
                }
            }

            List<List<(DateTime start, DateTime end)>> parallelSearchRanges = new();
            for (int i = 0; i < searchRanges.Count; i += _parallelSearchBundleSize)
            {
                int remainingElements = searchRanges.Count - i;
                if (remainingElements > 0)
                {
                    var range = searchRanges.GetRange(i, Math.Min(_parallelSearchBundleSize, remainingElements));
                    parallelSearchRanges.Add(range);
                }
            }

            return parallelSearchRanges;
        }

        private static ExportAllOrchestratorOptions ParseOptions(string inputs, DateTime executionTime)
        {
            // Get the input from the request to the function.
            JObject requestParameters;
            try
            {
                requestParameters = JObject.Parse(inputs);
            }
            catch (JsonReaderException jre)
            {
                throw new ArgumentException($"Not a valid JSON Object from starter input:{jre.Message}");
            }

            // Get and validate the time range for the export if given.
            string sinceStr = (string)requestParameters["_since"], tillStr = (string)requestParameters["_till"];
            DateTime since = default, till = default;

            if ((sinceStr is not null && !DateTime.TryParse(sinceStr, out since)) ||
                (tillStr is not null && !DateTime.TryParse(tillStr, out till)))
            {
                throw new ArgumentException($"Invalid input for _since  or _till parameter. _since: {sinceStr ?? string.Empty} _till: {tillStr ?? string.Empty}");
            }

            string baseAddress = requestParameters["_baseAddress"].ToString();
            if (baseAddress is null)
            {
                baseAddress = string.Empty;
            }
            else if (!baseAddress.EndsWith('/'))
            {
                baseAddress = baseAddress + "/";
            }

            ExportAllOrchestratorOptions options = new(
                ResourceType: requestParameters["_type"].ToString(),
                Since: since == default ? null : since,
                Till: till == default ? null : till,
                BaseAddress: baseAddress,
                Audience: requestParameters["_audience"].ToString(),
                ParallelizationCount: null,
                ParallelSearchInSecondsInterval: null,
                ParallelSearchRanges: null); ;

            // We only allow execution for a certain resource type.
            if (options.ResourceType is null)
            {
                throw new ArgumentException("_type is null. It must be provided to execute this function.");
            }

            if (requestParameters["_parallelizationCount"] is not null)
            {
                if (!int.TryParse((string)requestParameters["_parallelizationCount"], out int parallelizationCountParsed) || parallelizationCountParsed < 1 || parallelizationCountParsed > _maxParallelizationCount)
                {
                    throw new ArgumentException($"Invalid parallelization count interval received: {requestParameters["_parallelizationCount"]}");
                }

                options.ParallelizationCount = parallelizationCountParsed;
            }

            if (requestParameters["_parallelSearchInSecondsInterval"] is not null)
            {
                if (!int.TryParse((string)requestParameters["_parallelSearchInSecondsInterval"], out int parallelSearchInSecondsInterval) || parallelSearchInSecondsInterval < 1)
                {
                    throw new ArgumentException($"Invalid parallel search count interval received: {requestParameters["_parallelSearchInSecondsInterval"]}");
                }

                options.ParallelSearchInSecondsInterval = parallelSearchInSecondsInterval;
            }

            if ((options.ParallelizationCount is not null && options.ParallelSearchInSecondsInterval is null || options.Since is null) ||
                (options.ParallelizationCount is null && options.ParallelSearchInSecondsInterval is not null || options.Since is null))
            {
                throw new ArgumentException($"Invalid request: _parallelizationCount, _parallelSearchInSecondsInterval, and _since must be specified to use parallelization");
            }
            else
            {
                options.ParallelSearchRanges = GetSearchRanges(options.Since.Value, options.Till ?? executionTime, options.ParallelSearchInSecondsInterval.Value);
            }

            return options;
        }
        private List<(DateTime Start, DateTime End, int Count)> FlattenCountsByParallelizationCount(
            List<(DateTime Start, DateTime End, int Count)>[] input, 
            int parallelizationCount)
        {

            // Flatten the array of lists into a single list
            var flatList = input.SelectMany(x => x).OrderBy(x => x.Start).ToList();

            // Initialize the combined list
            var combinedList = new List<(DateTime Start, DateTime End, int Count)>();

            var targetCountPerEachParallelExecution = flatList.Sum(x => x.Count) / parallelizationCount;

            // Iterate through the flat list to combine adjacent ranges
            foreach (var tuple in flatList)
            {
                // Check if the last added range can be combined with the current tuple
                if (combinedList.Count > 0 &&
                    combinedList.Last().End == tuple.Start &&
                    combinedList.Last().Count + tuple.Count <= targetCountPerEachParallelExecution)
                {
                    var last = combinedList.Last();
                    combinedList.RemoveAt(combinedList.Count - 1); // Remove the last tuple
                    combinedList.Add((last.Start, tuple.End, last.Count + tuple.Count)); // Add the combined tuple
                }
                else
                {
                    // Add the current tuple as-is
                    combinedList.Add(tuple);
                }
            }

            return combinedList;
        }
    }

    public record struct DataPageRequest(string FhirRequestPath, string InstanceId, string ResourceType, string Audience);

    public record struct DataPageResult(string NextLink, int ResourceCount, string BlobUrl, string InstanceId, string Error);

    public record struct ResultUpdateRequest(string InstanceId, string ResourceType, int ResourceCount, string NextLink);

    public record struct WriteCompletionStatusRequest(string InstanceId, JObject StatusObject);

    public record struct ExportAllOrchestratorOptions(string ResourceType, DateTime? Since, DateTime? Till, string BaseAddress, string Audience, int? ParallelizationCount, int? ParallelSearchInSecondsInterval, List<List<(DateTime Start, DateTime End)>> ParallelSearchRanges);

    public record struct GetCountsForListOfDateRangesRequest(string ResourceType, List<(DateTime Start, DateTime End, int Count)> SearchRangeList);
}