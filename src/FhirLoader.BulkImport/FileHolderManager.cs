using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace FHIRBulkImport
{
    public static class FileHolderManager
    {
        public static async Task<JObject> CountLinesInBlob(string saconnectionString, string instanceid, string blobname,ILogger log)
        {
            var appendBlobSource = await StorageUtils.GetAppendBlobClient(saconnectionString, $"export/{instanceid}", $"{blobname}");
            appendBlobSource.Seal();
            int count = 0;
            using (var stream = appendBlobSource.OpenRead())
            {
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine();
                        count++;
                    }
                }
            }
            JObject rslt = new JObject();
            rslt["type"] = blobname.Split("-")[0];
            rslt["url"] = appendBlobSource.Uri.ToString();
            rslt["count"] = count;
            return rslt;
        }
        public static async Task<List<string>> GetFileNames(string instanceid,ILogger log)
        {
            List<string> retVal = new List<string>();
            string key = instanceid;
            try
            {
                // Create a BlobServiceClient object which will be used to create a container client
                BlobServiceClient blobServiceClient = new BlobServiceClient(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"));

                //Create a unique name for the container
             

                // Create the container and return a container client object
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("export");

                await foreach (BlobItem blobItem in containerClient.GetBlobsAsync(prefix:$"{instanceid}/"))
                {
                    string name = blobItem.Name.Replace($"{instanceid}/","");
                    if (name.EndsWith(".ndjson"))
                    {
                        retVal.Add(name);
                    }
                }
                
            }
            catch (Exception e)
            {
                log.LogError($"CountFiles:Exception {e.StackTrace}");
            }
            return retVal;
        }
        public static async Task<bool> WriteAppendBlobAsync(string instanceId, string resourceType, int fileno, string block, ILogger log)
        {
            var filename = resourceType + "-" + fileno + ".ndjson";
            return await WriteAppendBlobAsync(instanceId, filename, block, log);
        }
        public static async Task<bool> WriteAppendBlobAsync(string instanceId, string filename, string block, ILogger log)
        {
                var client = await StorageUtils.GetAppendBlobClient(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"), $"export/{instanceId}", filename);
                return await WriteAppendBlobAsync(client, block, log);
        }
        public static async Task<bool> WriteAppendBlobAsync(AppendBlobClient client, string block, ILogger log)
        {
            try
            {

                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(block)))
                {
                    await client.AppendBlockAsync(ms);
                }
                return true;
            }
            catch (Exception e)
            {
                log.LogError($"WriteAppendBlobAsync Exception: {e.Message}\r\n{e.StackTrace}");
                return false;
            }
        }



    }
   
    
 }

