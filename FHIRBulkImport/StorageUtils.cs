using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs.Specialized;
using Microsoft.WindowsAzure.Storage;
using System.Threading;
using System.IO;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Core;
using Azure.Storage.Blobs.Models;
using Azure;

namespace FHIRBulkImport
{
    public static class StorageUtils
    {
     
        public static AppendBlobClient GetAppendBlobClientSync(string saconnectionString, string container, string blobname)
        {
            string uriString = $"{saconnectionString}/{container}/{blobname}";
            Uri uri = new Uri(uriString);

            var retVal = new AppendBlobClient(uri, new DefaultAzureCredential());

            if (!retVal.Exists())
            {
                retVal.Create();
            }

            return retVal;
        }
        public static async Task<AppendBlobClient> GetAppendBlobClient(string saconnectionString,string container, string blobname)
        {
            string uriString = $"{saconnectionString}/{container}/{blobname}";
            Uri uri = new Uri(uriString);

            var retVal = new AppendBlobClient(uri, new DefaultAzureCredential());

            if (!await retVal.ExistsAsync())
            {
                await retVal.CreateAsync();
            }
        
            return retVal;
        }
        public static BlobServiceClient GetCloudBlobClient(string saconnectionString)
        {
            var credential = new DefaultAzureCredential();

            BlobClientOptions blobOpts = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2019_02_02);
            blobOpts.Retry.Delay = TimeSpan.FromSeconds(5);
            blobOpts.Retry.Mode = RetryMode.Fixed;
            blobOpts.Retry.MaxRetries = 3;

            return new BlobServiceClient(new Uri(saconnectionString), credential, blobOpts);
        }
         
        public static async Task WriteStringToBlob(BlobServiceClient blobClient,string containerName,string filePath,string contents, ILogger log)
        {
            var sourceContainer = blobClient.GetBlobContainerClient(containerName);
            if (!await sourceContainer.ExistsAsync())
            {
                await sourceContainer.CreateAsync();
            }

            BlobClient sourceBlob = sourceContainer.GetBlobClient(filePath);

            BlobProperties properties = await sourceBlob.GetPropertiesAsync();
            BlobType blobType = properties.BlobType;
            if (await sourceBlob.ExistsAsync())
            {
                if (blobType == BlobType.Block)
                {
                    var cbb = sourceContainer.GetBlobClient(filePath);
                    await cbb.UploadAsync(contents);
                    return;
                }

                if (blobType == BlobType.Append)
                {
                    AppendBlobClient cab = sourceContainer.GetAppendBlobClient(filePath);
                    byte[] bytes = Encoding.UTF8.GetBytes(contents);

                    // Write the bytes to the blob
                    using MemoryStream stream = new MemoryStream(bytes);
                    await cab.AppendBlockAsync(stream);
                    return;
                }
                throw new Exception($"Cannot write string to blob. Blob type must be block or append blob. Type: {sourceBlob.GetType()}");
            }

            BlobClient newblob = sourceContainer.GetBlobClient(filePath);
            await newblob.UploadAsync(contents);


        }
        public static async Task<System.IO.Stream> GetStreamForBlob(BlobServiceClient blobClient, string containerName, string filePath, ILogger log)
        {
            var sourceContainer = blobClient.GetBlobContainerClient(containerName);
            BlobClient sourceBlob = sourceContainer.GetBlobClient(filePath);
            if (await sourceBlob.ExistsAsync())
            {
                return await sourceBlob.OpenReadAsync();
            }
            return null;
        }
        public static async Task Delete(BlobServiceClient blobClient, string sourceContainerName, string name,ILogger log)
        {
            try
            {
              
                // details of our source file
                var sourceFilePath = name;

           
                var sourceContainer = blobClient.GetBlobContainerClient(sourceContainerName);

                BlobClient sourceBlob = sourceContainer.GetBlobClient(sourceFilePath);
               if (await sourceBlob.ExistsAsync()) await sourceBlob.DeleteAsync();
            }
            catch (Exception e)
            {
                log.LogError($"Error Deleting file {name}:{e.Message}");
            }
        }
        /*Moves Source File in Container to Destination Container and Deletes Source - Same Storage Account*/
        public static async Task MoveTo(BlobServiceClient blobClient, string sourceContainerName,string destContainerName, string name, string destName, ILogger log)
        {
            try
            {
               
                // details of our source file
                var sourceFilePath = name;

                // details of where we want to copy to
                var destFilePath = destName;
                
                var sourceContainer = blobClient.GetBlobContainerClient(sourceContainerName);
                var destContainer = blobClient.GetBlobContainerClient(destContainerName);
                if (!await destContainer.ExistsAsync())
                {
                    await destContainer.CreateAsync();
                }

                BlobClient sourceBlob = sourceContainer.GetBlobClient(sourceFilePath);
                BlobClient destBlob = destContainer.GetBlobClient(destFilePath);

                CopyFromUriOperation copyOperation = await destBlob.StartCopyFromUriAsync(sourceBlob.Uri);

                //waiting for completion
                await copyOperation.WaitForCompletionAsync();
             
                Response response = await copyOperation.UpdateStatusAsync();
                // Parse the response to find x-ms-copy-status header
                if (response.Headers.TryGetValue("x-ms-copy-status", out string value))
                    Console.WriteLine($"Copy status: {value}");
            
                if (value != "Success")
                {
                    log.LogError($"Copy failed file {name} to {destName}!");
                    await destBlob.AbortCopyFromUriAsync(copyOperation.Id);
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
