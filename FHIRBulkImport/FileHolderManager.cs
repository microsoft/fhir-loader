using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;

namespace FHIRBulkImport
{
    public static class FileHolderManager
    {
        private static Dictionary<string, AppendBlobInfoHolder> _holders = new Dictionary<string, AppendBlobInfoHolder>();
        private static readonly object _cacheLock = new object();
        private static int CountLinesInBlob(string saconnectionString, string instanceid, string blobname,ILogger log)
        {
            var appendBlobSource = StorageUtils.GetAppendBlobClientSync(saconnectionString, $"export/{instanceid}", $"{blobname}.xndjson");
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
            log.LogInformation($"There are {count} resources in {blobname}");
            return count;
        }
        public static JArray CountFiles(string instanceid,ILogger log)
        {
            JArray retVal = new JArray();
            string key = instanceid;
            lock (_cacheLock)
            {
                var enumerator = _holders.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current.Key.StartsWith(key))
                    {
                        var curvalue = enumerator.Current.Value;
                        JArray arr = curvalue.FileInfo;
                        int fn = 1;
                        foreach (JToken t in arr)
                        {
                            int filetotal = CountLinesInBlob(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"), curvalue.instanceId, $"{curvalue.ResourceType}-{fn}",log);
                            t["count"] = filetotal;
                            fn++;
                            retVal.Add(t);
                        }
                        _holders.Remove(enumerator.Current.Key);
                    }
                }
            }
            return retVal;
        }
        public static AppendBlobInfo GetCurrentHolderClient(string instanceId, string resourceType)
        {
            AppendBlobInfoHolder holder = null;
            string key = $"{instanceId}-{resourceType}";
            lock (_cacheLock)
            {
                if (!_holders.ContainsKey(key))
                {
                    holder = new AppendBlobInfoHolder();
                    holder.LastFileNum = 1;
                    holder.ResourceType = resourceType;
                    holder.instanceId = instanceId;
                    holder.FileInfo = new JArray();
                    var filename = resourceType + "-" + holder.LastFileNum + ".xndjson";
                    var client = StorageUtils.GetAppendBlobClientSync(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"), $"export/{instanceId}", filename);
                    holder.CurrentClient = client;
                    JObject info = new JObject();
                    info["type"] = resourceType;
                    info["url"] = holder.CurrentClient.Uri.ToString();
                    info["count"] = 0;
                    info["sizebytes"] = 0;
                    info["commitedblocks"] = 0;
                    holder.FileInfo.Add(info);
                    _holders[key] = holder;
                }
                else
                {
                    holder = _holders[key];
                    long maxfilesizeinbytes = Utils.GetIntEnvironmentVariable("FBI-MAXFILESIZEMB", "-1") * 1024000;
                    var props = holder.CurrentClient.GetProperties();
                    holder.FileInfo[holder.LastFileNum - 1]["sizebytes"] = props.Value.ContentLength;
                    holder.FileInfo[holder.LastFileNum - 1]["commitedblocks"] = props.Value.BlobCommittedBlockCount;
                    if (props.Value.BlobCommittedBlockCount > 49500 || (maxfilesizeinbytes > 0 && props.Value.ContentLength >= maxfilesizeinbytes))
                    {

                        holder.LastFileNum++;
                        var filename = resourceType + "-" + holder.LastFileNum + ".xndjson";
                        var client = StorageUtils.GetAppendBlobClientSync(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"), $"export/{instanceId}", filename);
                        holder.CurrentClient = client;
                        JObject info = new JObject();
                        info["type"] = resourceType;
                        info["url"] = holder.CurrentClient.Uri.ToString();
                        info["count"] = 0;
                        info["sizebytes"] = 0;
                        info["commitedblocks"] = 0;
                        holder.FileInfo.Add(info);
                    }
                }
                return new AppendBlobInfo(holder.CurrentClient, holder.LastFileNum);
            }
           

        }
       
    }
    public class AppendBlobInfo
    {
        public AppendBlobInfo(Azure.Storage.Blobs.Specialized.AppendBlobClient client,int filenum)
        {
            this.CurrentClient = client;
            this.fileno = filenum;
        }
        public Azure.Storage.Blobs.Specialized.AppendBlobClient CurrentClient { get; set; }
        public int fileno { get; set; }
    }
    public class AppendBlobInfoHolder
    {
        public string instanceId { get; set; }
        public Azure.Storage.Blobs.Specialized.AppendBlobClient CurrentClient { get; set; }
        public int LastFileNum { get; set; }
        public string ResourceType { get; set; }
        public JArray FileInfo { get; set; }

    }
 }

