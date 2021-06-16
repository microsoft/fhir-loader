using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
namespace FHIRBulkImport
{
    public static class StorageUtils
    {
        public static CloudBlobClient GetCloudBlobClient(string saconnectionString)
        {
            var storageAccount = CloudStorageAccount.Parse(saconnectionString);
            return storageAccount.CreateCloudBlobClient();
        }
        public static async Task WriteStringToBlob(CloudBlobClient blobClient,string containerName,string filePath,string contents, ILogger log)
        {
            var sourceContainer = blobClient.GetContainerReference(containerName);
            await sourceContainer.CreateIfNotExistsAsync();
            CloudBlockBlob sourceBlob = sourceContainer.GetBlockBlobReference(filePath);
            await sourceBlob.UploadTextAsync(contents);
        }
        public static async Task Delete(CloudBlobClient blobClient, string sourceContainerName, string name,ILogger log)
        {
            try
            {
              
                // details of our source file
                var sourceFilePath = name;

           
                var sourceContainer = blobClient.GetContainerReference(sourceContainerName);
                
                CloudBlockBlob sourceBlob = sourceContainer.GetBlockBlobReference(sourceFilePath);
               if (await sourceBlob.ExistsAsync()) await sourceBlob.DeleteAsync();
            }
            catch (Exception e)
            {
                log.LogError($"Error Moving file {name}:{e.Message}");
            }
        }
        /*Moves Source File in Container to Destination Container and Deletes Source - Same Storage Account*/
        public static async Task MoveTo(CloudBlobClient blobClient, string sourceContainerName,string destContainerName, string name, string destName, ILogger log)
        {
            try
            {
               
                // details of our source file
                var sourceFilePath = name;

                // details of where we want to copy to
                var destFilePath = destName;
                
                var sourceContainer = blobClient.GetContainerReference(sourceContainerName);
                var destContainer = blobClient.GetContainerReference(destContainerName);
                await destContainer.CreateIfNotExistsAsync();
                CloudBlockBlob sourceBlob = sourceContainer.GetBlockBlobReference(sourceFilePath);
                CloudBlockBlob destBlob = destContainer.GetBlockBlobReference(destFilePath);
                await destBlob.StartCopyAsync(sourceBlob);
                await sourceBlob.DeleteAsync();
            }
            catch (Exception e)
            {
                log.LogError($"Error Moving file {name} to {destName}:{e.Message}");
            }
        }
    }
}
