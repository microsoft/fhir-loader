// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FhirLoader.Tool.FileTypeHandlers
{
    public class SingleResourceFile : BaseFhirFile
    {
        private readonly Stream _inputStream;
        private readonly int _resourceCount;
        private readonly ILogger _logger;
        private IEnumerable<ProcessedResource>? _bundles;

        public SingleResource(Stream inputStream, string fileName, int resourceCount, ILogger logger) : base(fileName, resourceCount)
        {
            _inputStream = inputStream;
            _resourceCount = resourceCount;
            _logger = logger;
        }

        private IEnumerable<ProcessedResource> ConvertToBundles()
        {
            JObject bundle;
            // We must read the full file
            using (StreamReader reader = new StreamReader(_inputStream))
                bundle = JObject.Parse(reader.ReadToEnd());

            var resourceType = bundle.GetValue("resourceType")?.Value<string>();

            yield return new ProcessedResource
            {
                ResourceFileName = FileName,
                ResourceText = bundle.ToString(Formatting.Indented),
                ResourceCount = _resourceCount,
                ResourceType = resourceType,
                IsBundle = resourceType == "Bundle",
                ResourceId = bundle.GetValue("id")?.Value<string>()
            };
        }
    }
}
