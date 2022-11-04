﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FhirLoader.Common.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FhirLoader.Common.FileTypeHandlers
{
    public class BundleFileHandler : BaseFileHandler
    {
        private readonly Stream _inputStream;
        private readonly ILogger _logger;

        private IEnumerable<ProcessedResource>? _bundles;

        public BundleFileHandler(Stream inputStream, string fileName, int bundleSize, ILogger logger) : base(fileName, bundleSize)
        {
            _inputStream = inputStream;
            _logger = logger;
        }

        public override IEnumerable<ProcessedResource> FileAsResourceList
        {
            get
            {
                if (_bundles is null)
                    _bundles = ConvertToResourceCollection();

                return _bundles;
            }
        }

        private IEnumerable<ProcessedResource> ConvertToResourceCollection()
        {
            JObject bundle;

            // We must read the full file to resolve any refs
            using (StreamReader reader = new StreamReader(_inputStream))
                bundle = JObject.Parse(reader.ReadToEnd());

            try
            {
                SyntheaReferenceResolver.ConvertUUIDs(bundle);
            }
            catch
            {
                _logger.LogError($"Failed to resolve references in input file {FileName}.");
                throw;
            }

            // Convert collection bundles generated by Synthea to batch
            if (bundle["type"]!.ToString() == "collection")
            {
                bundle["type"] = "batch";
            }

            var bundleResources = bundle.SelectTokens("$.entry[*].resource");
            if (bundleResources.Count() <= BundleSize)
            {
                yield return new ProcessedResource
                {
                    ResourceFileName = FileName,
                    ResourceText = bundle.ToString(Formatting.Indented),
                    ResourceCount = bundleResources.Count(),
                };
            }

            while (true)
            {
                var resourceChunk = bundleResources.Take(BundleSize);
                bundleResources = bundleResources.Skip(BundleSize);

                if (resourceChunk.Count() == 0)
                    break;

                var newBundle = JObject.FromObject(new
                {
                    resourceType = "Bundle",
                    type = "batch",
                    entry =
                    from r in resourceChunk
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

                yield return new ProcessedResource
                {
                    ResourceFileName = FileName,
                    ResourceText = newBundle.ToString(Formatting.Indented),
                    ResourceCount = resourceChunk.Count(),
                    ResourceType = "Bundle"
                };
            }
        }
    }
}
