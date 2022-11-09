// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FhirLoader.Tool.FileTypeHandlers;
using Microsoft.Extensions.Logging;

namespace FhirLoader.Tool.FileSource
{
    public class LocalFolderSource : BaseFileSource
    {
        public LocalFolderSource(string folderPath, ILogger logger)
            : base(folderPath, logger)
        {
        }

        internal override IEnumerable<(string Name, Stream Data)> GetFiles()
        {
            Logger.LogInformation($"Searching {Path} for FHIR files.");

            var inputBundles = Directory
                .EnumerateFiles(Path, "*", SearchOption.AllDirectories)
                .Where(s => s.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

            var inputBulkfiles = Directory
                .EnumerateFiles(Path, "*", SearchOption.AllDirectories)
                .Where(s => s.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase));

            Logger.LogInformation($"Found {inputBundles.Count()} FHIR bundles and {inputBulkfiles.Count()} FHIR bulk data files.");

            foreach (var filePath in inputBundles.Concat(inputBulkfiles).OrderBy(x => x))
            {
                var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var safeFileStream = Stream.Synchronized(fileStream);

                // #TODO - return the filename not the full path.
                yield return (filePath, safeFileStream);
            }
        }
    }
}
