using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace FhirLoader.Common
{
    public class SourceFileHandler
    {
        ILogger _logger;

        public SourceFileHandler(ILogger logger)
        {
            _logger = logger;
        }

        public IEnumerable<FhirFileHandler> LoadFromBlobPath(string blobPath, int bundleSize)
        {
            _logger.LogInformation($"Searching {blobPath} for FHIR files.");

            var blobPathUri = new Uri(blobPath);
            var blobUriParsed = new BlobUriBuilder(blobPathUri);
            var accountUrl = new Uri($"https://{blobUriParsed.Host}");

            BlobServiceClient blobServiceClient = new BlobServiceClient(serviceUri: accountUrl);
            var containerClient = blobServiceClient.GetBlobContainerClient(blobUriParsed.BlobContainerName);

            // Get list of bulk files and bundler
            // Using the orig Uri as BlobUriBuilder drops the last slash
            string blobPrefix = "";
            if (blobPathUri.Segments.Length >= 2)
            {
                blobPrefix = string.Join('/', blobPathUri.Segments.Skip(2));
            }

            // TODO - change to async.
            IEnumerable<BlobItem> blobsPrefixFiltered = containerClient.GetBlobs(prefix: blobPrefix);

            List<BlobItem> inputBundles = blobsPrefixFiltered.Where(_ => _.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
            List<BlobItem> inputBulkfiles = blobsPrefixFiltered.Where(_ => _.Name.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase)).ToList();

            _logger.LogInformation($"Found {inputBundles.Count()} FHIR bundles and {inputBulkfiles.Count()} FHIR bulk data files.");

            foreach (var blob in inputBundles.Concat(inputBulkfiles).OrderBy(_ => _.Name))
            {
                var blobClient = containerClient.GetBlobClient(blob.Name);
                var blobStream = blobClient.DownloadStreaming().Value.Content;
                var safeBlobStream = Stream.Synchronized(blobStream);

                if (blob.Name.EndsWith(".json"))
                    yield return new BundleFileHandler(safeBlobStream, blob.Name, bundleSize, _logger);
                else if (blob.Name.EndsWith(".ndjson"))
                    yield return new BulkFileHandler(safeBlobStream, blob.Name, bundleSize);
            }
        }

        public IEnumerable<FhirFileHandler> LoadFromFilePath(string bundlePath, int bundleSize)
        {
            _logger.LogInformation($"Searching {bundlePath} for FHIR files.");

            var inputBundles = Directory
                .EnumerateFiles(bundlePath, "*", SearchOption.AllDirectories)
                .Where(s => s.EndsWith(".json"));
            var inputBulkfiles = Directory
                .EnumerateFiles(bundlePath, "*", SearchOption.AllDirectories)
                .Where(s => s.EndsWith(".ndjson"));

            _logger.LogInformation($"Found {inputBundles.Count()} FHIR bundles and {inputBulkfiles.Count()} FHIR bulk data files.");

            foreach (var filePath in inputBundles.Concat(inputBulkfiles).OrderBy(x => x))
            {
                var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var safeFileStream = Stream.Synchronized(fileStream);

                if (filePath.EndsWith(".json"))
                {
                    if (CheckResourceType(filePath)?.ToLower() == "bundle")
                    {
                        yield return new BundleFileHandler(safeFileStream, filePath, bundleSize, _logger);
                    }
                }
                else if (filePath.EndsWith(".ndjson"))
                {
                    yield return new BulkFileHandler(safeFileStream, filePath, bundleSize);
                }
            }
        }

        public IEnumerable<FhirFileHandler> LoadFromPackagePath(string packagePath, int bundleSize, bool bundlePackageFiles)
        {
            _logger.LogInformation($"Searching {packagePath} for FHIR files.");

            string? packageType;

            PackageHelper helper = new(packagePath);
            if (!helper.ValidateRequiredFiles())
            {
                _logger.LogError($"Provided package path does not have .index.json and/or package.json file. Skipping the loading process.");
                throw new ArgumentException("Provided package path does not have .index.json and/or package.json file. Skipping the loading process.");
            }
            if (!helper.IsValidPackageType(out packageType))
            {
                _logger.LogError($"Package type {packageType} is not valid. Skipping the loading process.");
                throw new ArgumentException($"Package type {packageType} is not valid. Skipping the loading process.");
            }

            IEnumerable<string> packageFiles = helper.GetPackageFiles();

            // Test code to determine if we can optimize creation of package resources
            if (bundlePackageFiles)
            {
                _logger.LogInformation($"Found {packageFiles.Count()} FHIR package files. Sending in bundles...");

                while (true)
                {
                    var fileChunk = packageFiles.OrderBy(x => x).Take(bundleSize);
                    packageFiles = packageFiles.Skip(bundleSize).ToList();

                    if (fileChunk.Count() == 0)
                        break;

                    var resourcesAsBundle = JObject.FromObject(new
                    {
                        resourceType = "Bundle",
                        type = "batch",
                        entry =
                        from r in fileChunk.Select(x => JObject.Parse(File.ReadAllText(x)))
                        select new
                        {
                            resource = r,
                            request = new
                            {
                                method = r.SelectToken("id") is not null ? "PUT" : "POST",
                                url = r.SelectToken("id") is not null ? $"{r["resourceType"]}/{r["id"]}" : r["resourceType"]
                            }
                        }
                    });

                    // Convert compiled bundle to stream
                    // #TODO - can write to temp path on disk until it's ready to send
                    byte[] byteArray = Encoding.UTF8.GetBytes(resourcesAsBundle.ToString());
                    MemoryStream stream = new MemoryStream(byteArray);
                    yield return new ResourceFileHandler(stream, string.Join(',', fileChunk), fileChunk.Count(), _logger);
                }
            }
            else
            {
                _logger.LogInformation($"Found {packageFiles.Count()} FHIR package files. Sending as individual resources...");
                foreach (var filePath in packageFiles)
                {
                    var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    var safeFileStream = Stream.Synchronized(fileStream);
                    yield return new ResourceFileHandler(safeFileStream, filePath, 1, _logger);

                }
            }
        }

        /// <summary>
        /// Read the file content and return the resource type.
        /// </summary>
        /// <param name="filepath"></param>
        /// <returns></returns>
        private static string? CheckResourceType(string filepath)
        {
            JObject data = JObject.Parse(File.ReadAllText(filepath));
            return data.GetValue("resourceType")?.Value<string>();
        }
    }
}
