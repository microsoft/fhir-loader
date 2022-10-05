﻿using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;


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
            IEnumerable<BlobItem> blobsPrefixFiltered = containerClient.GetBlobs();
            if (blobPathUri.Segments.Length >= 2)
                blobsPrefixFiltered = blobsPrefixFiltered.Where(_ => _.Name.StartsWith(string.Join('/', blobPathUri.Segments.Skip(2))));

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
                    yield return new BundleFileHandler(safeFileStream, filePath, bundleSize, _logger);
                }
                else if (filePath.EndsWith(".ndjson"))
                {
                    yield return new BulkFileHandler(safeFileStream, filePath, bundleSize);
                }
            }
        }
    }
}