// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using FhirLoader.Tool.FileTypeHandlers;
using FhirLoader.Tool.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using static FhirLoader.Tool.Helpers.PackageTypeHelper;

namespace FhirLoader.Tool.FileSource
{
    public class FhirPackageSource : BaseFileSource
    {
        private const string PackageIndexFileName = ".index.json";
        private const string PackageJsonFileName = "package.json";
        private const string SearchParameterResourceType = "SearchParameter";

        public FhirPackageSource(string packagePath, ILogger logger)
            : base(packagePath, logger)
        {
        }

        internal override IEnumerable<(string Name, Stream Data)> GetFiles()
        {
            Logger.LogInformation($"Searching {Path} for FHIR package files.");

            string? packageType;

            if (!PackageHasNeededMetadataFiles())
            {
                Logger.LogError($"Provided package path does not have .index.json and/or package.json file. Skipping the loading process.");
                throw new ArgumentException("Provided package path does not have .index.json and/or package.json file. Skipping the loading process.");
            }

            if (!IsValidPackageType(out packageType))
            {
                Logger.LogError($"Package type {packageType} is not valid. Skipping the loading process.");
                throw new ArgumentException($"Package type {packageType} is not valid. Skipping the loading process.");
            }

            // TODO - this does not work with US Core.
            // var searchParamList = helper.GetSearchParams(metadata, Logger);
            // var packageFiles = helper.GetPackageFiles(searchParamList);
            var packageFiles = GetPackageFiles();

            Logger.LogInformation($"Found {packageFiles.Count()} FHIR package files. Sending as individual resources...");
            foreach (var filePath in packageFiles)
            {
                var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var safeFileStream = Stream.Synchronized(fileStream);

                // #TODO - this should return the filename only, not the full path.
                yield return (filePath, safeFileStream);
            }
        }

        private IEnumerable<string> GetPackageFiles()
        {
            List<string> packageFiles = new();
            JObject indexFile = JObject.Parse(File.ReadAllText(System.IO.Path.Combine(Path, PackageIndexFileName)));

            if (indexFile.ContainsKey("files") && indexFile["files"]!.Type == JTokenType.Array)
            {
                JArray files = (JArray)indexFile["files"]!;
                return files
                    .Where(f => f.Type == JTokenType.Object && ((JObject)f).ContainsKey("filename"))
                    .Select(t => System.IO.Path.Combine(Path, t["filename"]?.Value<string>()!));
            }

            return Enumerable.Empty<string>();
        }

        private IEnumerable<string> GetPackageFiles(List<string> searchParamList)
        {
            List<string> files = new();

            JObject indexFile = JObject.Parse(File.ReadAllText(System.IO.Path.Combine(Path, PackageIndexFileName)));

            if (indexFile.ContainsKey("files") && indexFile["files"] is not null && indexFile["files"]!.Type == JTokenType.Array)
            {
                // if resourcetype is Search parameter and it exists in metadata, dont add it to files
                foreach (var item in (JArray)indexFile["files"]!)
                {
                    if (item is not null && item.Type == JTokenType.Object)
                    {
                        JObject jItem = (JObject)item;

                        if (
                            jItem["resourceType"]?.Value<string>()?.ToLower(CultureInfo.InvariantCulture) == SearchParameterResourceType &&
                            searchParamList.Contains(jItem["url"]?.Value<string>() ?? string.Empty))
                        {
                            continue;
                        }
                        else if (jItem.ContainsKey("fileName"))
                        {
                            files.Add(System.IO.Path.Combine(Path, jItem["filename"]!.ToString()));
                        }
                    }
                }
            }

            return files;
        }

        /// <summary>
        /// Check if the .index.json and package.json files exist in the given path.
        /// </summary>
        /// <returns>Boolean signaling if the path has the required files.</returns>
        public bool PackageHasNeededMetadataFiles()
        {
            string indexPath = System.IO.Path.Combine(Path, PackageIndexFileName);
            string packagePath = System.IO.Path.Combine(Path, PackageJsonFileName);

            return File.Exists(indexPath) && File.Exists(packagePath);
        }

        /// <summary>
        /// Check if the given type in the package.json is valid or not.
        /// It returns true if the type matches with Conformance, fhir.ig, or Core, otherwise false.
        /// </summary>
        /// <param name="packageType">String of the package type.</param>
        /// <returns>Bool signaling if the package type is valid.</returns>
        public bool IsValidPackageType(out string? packageType)
        {
            JObject data = JObject.Parse(File.ReadAllText(System.IO.Path.Combine(Path, PackageJsonFileName)));

            packageType = data.GetValue("type", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            if (!string.IsNullOrEmpty(packageType) && CheckPackageType(packageType))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Method that check passed value is exitst or not in PackageType Enum.
        /// If a match is found, return true; otherwise, return false. .
        /// </summary>
        /// <param name="packageType">String of the package type.</param>
        /// <returns>Enum of the package type.</returns>
        private static bool CheckPackageType(string packageType)
        {
            return Enum.GetValues(typeof(PackageType))
                  .Cast<PackageType>()
                  .Select(x => GetDisplayName<PackageType>(x))
                  .ToList().Contains(packageType);
        }
    }
}
