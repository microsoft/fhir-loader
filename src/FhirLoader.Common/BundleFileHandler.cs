﻿using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace FhirLoader.Common
{
    public class BundleFileHandler : FhirFileHandler
    {
        private readonly Stream _inputStream;
        private readonly ILogger _logger;

        private IEnumerable<ProcessedResource>? _bundles;

        public BundleFileHandler(Stream inputStream, string fileName, int bundleSize, ILogger logger) : base(fileName, bundleSize)
        {
            _inputStream = inputStream;
            _logger = logger;
        }

        public override IEnumerable<ProcessedResource> FileAsBundles
        {
            get
            {
                if (_bundles is null)
                    _bundles = ConvertToBundles();

                return _bundles;
            }
        }

        public IEnumerable<ProcessedResource> ConvertToBundles()
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
