// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FhirLoader.CommandLineTool.FileTypeHandlers;
using Microsoft.Extensions.Logging;

namespace FhirLoader.CommandLineTool.FileType
{
    // Creates FHIR Files from the name and stream of the file
    public static class ProcessedResourceFactory
    {
        public static IEnumerable<BaseProcessedResource> ProcessedResourceFromFileStream(Stream data, string fileName, int bundleSize, ILogger logger)
        {
            // Handle Bulk Data Files
            if (fileName.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase))
            {
                var bulkFile = new BulkDataFile(data, fileName, bundleSize);
                return bulkFile.ConvertToBundles();
            }

            // Handles standard FHIR files
            if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var resource = new FhirResourceFile(data, fileName, bundleSize, logger);
                return resource.ConvertToResourceCollection();
            }

            return Enumerable.Empty<BaseProcessedResource>();
        }
    }
}
