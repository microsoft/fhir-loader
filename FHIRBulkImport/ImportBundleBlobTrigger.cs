using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace FHIRBulkImport
{
    [Disable("FBI-DISABLE-BLOBTRIGGER")]
    public static class ImportBundleBlobTrigger
    {
        [FunctionName("ImportBundleBlobTrigger")]
        public static async Task Run([BlobTrigger("bundles/{name}", Connection = "FBI-STORAGEACCT")]Stream myBlob, string name, ILogger log)
        {
            await ImportUtils.ImportBundle(myBlob, name, log);
        }
    }
}
