using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Azure.Storage.Blobs;

namespace FHIRBulkImport
{

    public class ImportCompressedFiles
    {
        private readonly TelemetryClient _telemetryClient;
        public ImportCompressedFiles(TelemetryConfiguration telemetryConfiguration)
        {
            _telemetryClient = new TelemetryClient(telemetryConfiguration);
        }
        [FunctionName("ImportCompressedFiles")]
        public static async Task Run([BlobTrigger("zip/{name}", Connection = "FBI-STORAGEACCT-IDENTITY")]Stream myBlob, string name, ILogger log)
        {
            try
            {
                int filecnt = 0;
                if (name.Split('.').Last().ToLower() == "zip")
                {

                    var blobClient = StorageUtils.GetCloudBlobClient(Utils.GetEnvironmentVariable("FBI-STORAGEACCT"));
                    var containerndjson = blobClient.GetBlobContainerClient("ndjson");
                    var containerbundles = blobClient.GetBlobContainerClient("bundles");

                    using (MemoryStream blobMemStream = new MemoryStream())
                    {
                        log.LogInformation($"ImportCompressedFiles: Decompressing {name} ...");
                        await myBlob.CopyToAsync(blobMemStream);
                        
                        using (ZipArchive archive = new ZipArchive(blobMemStream))
                        {
                            foreach (ZipArchiveEntry entry in archive.Entries)
                            {
                              
                                //Replace all NO digits, letters, or "-" by a "-" Azure storage is specific on valid characters
                                string validname = Regex.Replace(entry.Name, @"[^a-zA-Z0-9\-]", "-").ToLower();
                                //log.LogInformation($"ImportCompressedFiles: Now processing {entry.FullName} size {FormatSize(entry.Length)}");
                                BlobContainerClient destination = null;
                                if (validname.ToLower().EndsWith("ndjson"))
                                {
                                    destination = containerndjson;
                                    validname += ".ndjson";
                                }
                                else if (validname.ToLower().EndsWith("json"))
                                {
                                    destination = containerbundles;
                                    validname += ".json";
                                }
                                if (destination != null)
                                {
                                    BlobClient blockBlob = destination.GetBlobClient(validname);
                                    using (var fileStream = entry.Open())
                                    {
                                        await blockBlob.UploadAsync(fileStream);
                                        
                                    }
                                    log.LogInformation($"ImportCompressedFiles: Extracted {entry.FullName} to {destination.Name}/{validname}");
                                   
                                } else
                                {
                                    log.LogInformation($"ImportCompressedFiles: Entry {entry.FullName} skipped does not end in .ndjson or .json");
                                }
                                filecnt++;
                            }
                        }
                           
                    }
                    log.LogInformation($"ImportCompressedFiles: Completed Decompressing {name} extracted {filecnt} files...");
                    await StorageUtils.MoveTo(blobClient, "zip", "zipprocessed", name, name,log);
                }
            }
            catch (Exception ex)
            {
                log.LogInformation($"ImportCompressedFiles: Error! Something went wrong: {ex.Message}");

            }
        }
        static readonly string[] suffixes =
        { "Bytes", "KB", "MB", "GB", "TB", "PB" };
        public static string FormatSize(Int64 bytes)
        {
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1}{1}", number, suffixes[counter]);
        }
    
    }
}
