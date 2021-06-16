using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;

namespace FHIRBulkImport
{
    public static class ProcessBundle
    {
        [FunctionName("ProcessBundle")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var resp = await FHIRUtils.CallFHIRServer("", FHIRUtils.TransformBundle(requestBody, log), HttpMethod.Post, log);
            string r = "{}";
            if (resp != null) r = resp.Content;
            int sc = (int)resp.Status;
            return new ContentResult() { Content = r, StatusCode = sc, ContentType = "application/json" };
        }
    }
}
