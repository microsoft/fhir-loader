// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FhirLoader.Tool.FileType;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FhirLoader.Tool.FileTypeHandlers
{
    public class BulkDataFile
    {
        private readonly string _fileName;
        private readonly int _bundleSize;
        private readonly Stream _inputStream;

        public BulkDataFile(Stream inputStream, string fileName, int bundleSize)
        {
            _fileName = fileName;
            _bundleSize = bundleSize;
            _inputStream = inputStream;
        }

        public IEnumerable<BaseProcessedResource> ConvertToBundles()
        {
            using (var reader = new StreamReader(_inputStream))
            {
                while (!reader.EndOfStream)
                {
                    List<string> page = new List<string>();

                    for (int i = 0; i < _bundleSize; i++)
                    {
                        if (!reader.EndOfStream)
                        {
                            var line = reader.ReadLine();
                            if (line is not null && line.StartsWith("{", StringComparison.OrdinalIgnoreCase))
                            {
                                page.Add(line);
                            }
                        }
                    }

                    yield return BuildBundle(page);
                }
            }
        }

        private BaseProcessedResource BuildBundle(IEnumerable<string> page)
        {
            try
            {
                var resourceChunk = page.Select(x => JObject.Parse(x));
                var bundle = JObject.FromObject(new
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
                                method = r.ContainsKey("id") ? "PUT" : "POST",
                                url = r.ContainsKey("id") ? $"{r["resourceType"]}/{r["id"]}" : r["resourceType"],
                            },
                        },
                });

                var count = bundle.ContainsKey("entry") ? bundle["entry"]!.Count() : 0;

                return new ProcessedBundle()
                {
                    ResourceText = bundle.ToString(Formatting.Indented),
                    ResourceCount = count,
                    ResourceFileName = _fileName,
                };
            }
            catch (Exception ex)
            {
#pragma warning disable CA2201
                throw new Exception($"Error converting NDJSON file to Bundle {_fileName}.", ex);
            }
        }
    }
}
