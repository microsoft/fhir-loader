// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FhirLoader.Tool.FileTypeHandlers;
using Microsoft.Extensions.Logging;

namespace FhirLoader.Tool.FileSource
{
    public class AzureBlobSource : BaseFileSource
    {
        public AzureBlobSource(string blobPath, ILogger logger)
            : base(blobPath, logger)
        {
        }

        internal override IEnumerable<(string Name, Stream Data)> GetFiles()
        {
            var blobPathUri = new Uri(Path);
            var blobUriParsed = new BlobUriBuilder(blobPathUri);
            var accountUrl = new Uri($"https://{blobUriParsed.Host}");

            var blobServiceClient = new BlobServiceClient(serviceUri: accountUrl);
            var containerClient = blobServiceClient.GetBlobContainerClient(blobUriParsed.BlobContainerName);

            // Get list of bulk files and bundler
            // Using the orig Uri as BlobUriBuilder drops the last slash
            string blobPrefix = string.Empty;
            if (blobPathUri.Segments.Length >= 2)
            {
                blobPrefix = string.Join('/', blobPathUri.Segments.Skip(2));
            }

            // TODO - change to async.
            IEnumerable<BlobItem> blobsPrefixFiltered = containerClient.GetBlobs(prefix: blobPrefix);

            var inputBundles = blobsPrefixFiltered.Where(_ => _.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
            var inputBulkfiles = blobsPrefixFiltered.Where(_ => _.Name.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase)).ToList();

            // _logger.LogInformation($"Found {inputBundles.Count} FHIR bundles and {inputBulkfiles.Count} FHIR bulk data files.");
            foreach (var blob in inputBundles.Concat(inputBulkfiles).OrderBy(_ => _.Name))
            {
                var blobClient = containerClient.GetBlobClient(blob.Name);
                var blobStream = blobClient.DownloadStreaming().Value.Content;
                var safeBlobStream = Stream.Synchronized(blobStream);

                yield return (blob.Name, safeBlobStream);
            }
        }
    }
}
