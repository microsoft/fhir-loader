using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs.Specialized;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage;
using System.Threading;
using System.IO;

namespace FHIRBulkImport
{
    public static class StorageUtils
    {
     
        public static AppendBlobClient GetAppendBlobClientSync(string saconnectionString, string container, string blobname)
        {
            var retVal = new AppendBlobClient(saconnectionString, container, blobname);
            if (!retVal.Exists())
            {
                retVal.Create();
            }

            return retVal;
        }
        public static async Task<AppendBlobClient> GetAppendBlobClient(string saconnectionString,string container, string blobname)
        {
            var retVal =  new AppendBlobClient(saconnectionString, container, blobname);
            if (!await retVal.ExistsAsync())
            {
                await retVal.CreateAsync();
            }
        
            return retVal;
        }
        public static CloudBlobClient GetCloudBlobClient(string saconnectionString)
        {
            var storageAccount = CloudStorageAccount.Parse(saconnectionString);
            return storageAccount.CreateCloudBlobClient();
        }
         
        public static async Task WriteStringToBlob(CloudBlobClient blobClient,string containerName,string filePath,string contents, ILogger log)
        {
            var sourceContainer = blobClient.GetContainerReference(containerName);
            if (!await sourceContainer.ExistsAsync())
            {
                await sourceContainer.CreateAsync();
            }
            var sourceBlob = sourceContainer.GetBlobReference(filePath);

            if (sourceBlob is CloudBlockBlob sourceBlockBlob)
            {
                await sourceBlockBlob.UploadTextAsync(contents);
            }

            if (sourceBlob is CloudAppendBlob sourceAppendBlob)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(contents);

                // Write the bytes to the blob
                using MemoryStream stream = new MemoryStream(bytes);
                await sourceAppendBlob.AppendBlockAsync(stream);
            }

            throw new Exception($"Cannot write string to blob. Blob type must be block or append blob. Type: {sourceBlob.GetType()}");
        }
        public static async Task<System.IO.Stream> GetStreamForBlob(CloudBlobClient blobClient, string containerName, string filePath, ILogger log)
        {
            var sourceContainer = blobClient.GetContainerReference(containerName);
            CloudBlob sourceBlob = sourceContainer.GetBlobReference(filePath);
            if (await sourceBlob.ExistsAsync())
            {
                return await sourceBlob.OpenReadAsync();
            }
            return null;
        }
        public static async Task Delete(CloudBlobClient blobClient, string sourceContainerName, string name,ILogger log)
        {
            try
            {
              
                // details of our source file
                var sourceFilePath = name;

           
                var sourceContainer = blobClient.GetContainerReference(sourceContainerName);
                
                CloudBlob sourceBlob = sourceContainer.GetBlobReference(sourceFilePath);
               if (await sourceBlob.ExistsAsync()) await sourceBlob.DeleteAsync();
            }
            catch (Exception e)
            {
                log.LogError($"Error Deleting file {name}:{e.Message}");
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
                if (!await destContainer.ExistsAsync())
                {
                    await destContainer.CreateAsync();
                }

                CloudBlob sourceBlob = sourceContainer.GetBlobReference(sourceFilePath);
                CloudBlob destBlob = destContainer.GetBlobReference(destFilePath);
                
                string copyid = await destBlob.StartCopyAsync(sourceBlob.Uri);
                //fetch current attributes
                await destBlob.FetchAttributesAsync();
                //waiting for completion
                int copyretries = 5;
                while (destBlob.CopyState.Status == CopyStatus.Pending && copyretries > 1)
                {
                    await Task.Delay(500);
                    await destBlob.FetchAttributesAsync();
                    copyretries--;
                }
                if (destBlob.CopyState.Status != CopyStatus.Success)
                {
                    log.LogError($"Copy failed file {name} to {destName}!");
                    await destBlob.AbortCopyAsync(copyid);
                    return;
                }
                await sourceBlob.DeleteAsync();
            }
            catch (Exception e)
            {
                log.LogError($"Error Moving file {name} to {destName}:{e.Message}");
            }
        }
        

    }
   
}
