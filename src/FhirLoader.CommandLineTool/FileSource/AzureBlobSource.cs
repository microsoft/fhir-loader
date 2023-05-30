// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace FhirLoader.CommandLineTool.FileSource
{
    public class AzureBlobSource : BaseFileSource
    {
        private readonly Uri _blobPath;

        public AzureBlobSource(Uri blobPath, ILogger logger)
            : base(blobPath.ToString(), logger)
        {
            _blobPath = blobPath;
        }

        internal override IEnumerable<(string Name, Stream Data)> GetFiles()
        {
            BlobUriBuilder blobUriParsed = new(_blobPath);
            Uri accountUrl = new($"https://{blobUriParsed.Host}");

            BlobServiceClient blobServiceClient = new(serviceUri: accountUrl);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(blobUriParsed.BlobContainerName);

            // Get list of bulk files and bundler
            // Using the orig Uri as BlobUriBuilder drops the last slash
            string blobPrefix = string.Empty;
            if (_blobPath.Segments.Length >= 2)
            {
                blobPrefix = string.Join('/', _blobPath.Segments.Skip(2));
            }

            // TODO - change to async.
            IEnumerable<BlobItem> blobsPrefixFiltered = containerClient.GetBlobs(prefix: blobPrefix);

            var inputBundles = blobsPrefixFiltered.Where(_ => _.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
            var inputBulkfiles = blobsPrefixFiltered.Where(_ => _.Name.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase)).ToList();

            // _logger.LogInformation($"Found {inputBundles.Count} FHIR bundles and {inputBulkfiles.Count} FHIR bulk data files.");
            foreach (var blob in inputBundles.Concat(inputBulkfiles).OrderBy(_ => _.Name))
            {
                BlobClient blobClient = containerClient.GetBlobClient(blob.Name);
                Stream blobStream = blobClient.DownloadStreaming().Value.Content;
                Stream safeBlobStream = Stream.Synchronized(blobStream);

                yield return (blob.Name, safeBlobStream);
            }
        }
    }
}
