// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FhirLoader.Common.FileTypeHandlers;
using FhirLoader.Common.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FhirLoader.Common.SourceHandlers
{
    public static class FhirPathResolver
    {
        public static IEnumerable<BaseFileHandler> FromAzureBlobUri(string blobPath, ILogger logger)
        {
            logger.LogInformation($"Searching {blobPath} for FHIR files.");

            var blobPathUri = new Uri(blobPath);
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

            logger.LogInformation($"Found {inputBundles.Count} FHIR bundles and {inputBulkfiles.Count} FHIR bulk data files.");

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

        public static IEnumerable<BaseFileHandler> LoadFromLocalFilePath(string bundlePath, int bundleSize, ILogger _logger)
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

        public static IEnumerable<BaseFileHandler> LoadFhirPackageFromLocalPath(string packagePath, int bundleSize, JObject? metadata, ILogger logger)
        {
            logger.LogInformation($"Searching {packagePath} for FHIR files.");

            string? packageType;

            PackageHelper helper = new(packagePath);
            if (!helper.ValidateRequiredFiles())
            {
                logger.LogError($"Provided package path does not have .index.json and/or package.json file. Skipping the loading process.");
                throw new ArgumentException("Provided package path does not have .index.json and/or package.json file. Skipping the loading process.");
            }
            if (!helper.IsValidPackageType(out packageType))
            {
                logger.LogError($"Package type {packageType} is not valid. Skipping the loading process.");
                throw new ArgumentException($"Package type {packageType} is not valid. Skipping the loading process.");
            }

            var searchParamList = helper.GetSearchParams(metadata, logger);
            var packageFiles = helper.GetPackageFiles(searchParamList);

            logger.LogInformation($"Found {packageFiles.Count()} FHIR package files. Sending as individual resources...");
            foreach (var filePath in packageFiles)
            {
                var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var safeFileStream = Stream.Synchronized(fileStream);
                yield return new SingleResourceFileHandler(safeFileStream, filePath, 1, logger);

            }
        }

        /// <summary>
        /// Read the file content and return the resource type.
        /// </summary>
        /// <param name="filepath"></param>
        /// <returns></returns>
        private static string? CheckResourceType(string filepath)
        {
            var data = JObject.Parse(File.ReadAllText(filepath));
            return data.GetValue("resourceType")?.Value<string>();
        }
    }
}
