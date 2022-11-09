using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FhirLoader.Common.FileTypeHandlers;
using Microsoft.Extensions.Logging;

namespace FhirLoader.Common.SourceHandlers
{
    public class AzureBlobUri
    {
        private readonly ILogger _logger;

        public AzureBlobUri(ILogger logger)
        {
            _logger = logger;
        }

        public static IEnumerable<BaseFileHandler> LoadFromAzureBlobUri(string blobPath, int bundleSize, ILogger _logger)
        {
            _logger.LogInformation($"Searching {blobPath} for FHIR files.");

            var blobPathUri = new Uri(blobPath);
            var blobUriParsed = new BlobUriBuilder(blobPathUri);
            var accountUrl = new Uri($"https://{blobUriParsed.Host}");

            var blobServiceClient = new BlobServiceClient(serviceUri: accountUrl);
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

            var inputBundles = blobsPrefixFiltered.Where(_ => _.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
            var inputBulkfiles = blobsPrefixFiltered.Where(_ => _.Name.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase)).ToList();

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
    }
}
