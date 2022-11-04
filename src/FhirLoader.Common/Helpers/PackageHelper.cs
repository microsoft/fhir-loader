using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using static FhirLoader.Common.Helpers.PackageTypeHelper;

namespace FhirLoader.Common.Helpers
{
    public class PackageHelper
    {
        private string _packagePath;
        private const string PACKAGE_INDEX_FILENAME = ".index.json";
        private const string packagejson = "package.json";

        // private const string searchParameter = "searchparameter";

        /// <summary>
        /// Initializes a new instance of the <see cref="PackageHelper"/> class.
        /// </summary>
        /// <param name="packagePath">Path to the FHIR package.</param>
        public PackageHelper(string packagePath)
        {
            _packagePath = packagePath;
        }

        /// <summary>
        /// Check if the .index.json and package.json files exist in the given path.
        /// </summary>
        /// <returns>Boolean signaling if the path has the required files</returns>
        public bool ValidateRequiredFiles() => File.Exists(Path.Combine(_packagePath, PACKAGE_INDEX_FILENAME)) && File.Exists(Path.Combine(_packagePath, packagejson));

        /// <summary>
        /// Check if the given type in the package.json is valid or not.
        /// It returns true if the type matches with Conformance, fhir.ig, or Core, otherwise false.
        /// </summary>
        /// <param name="packageType">String of the package type.</param>
        /// <returns>Bool signaling if the package type is valid.</returns>
        public bool IsValidPackageType(out string? packageType)
        {
            JObject data = JObject.Parse(File.ReadAllText(Path.Combine(_packagePath, packagejson)));

            packageType = data.GetValue("type")?.Value<string>();
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

        /// <summary>
        /// Read the .index.json and return the list file name.
        /// </summary>
        /// <returns>List of files in the FHIR Package.</returns>
        public IEnumerable<string> GetPackageFiles()
        {
            List<string> packageFiles = new();
            JObject indexFile = JObject.Parse(File.ReadAllText(Path.Combine(_packagePath, PACKAGE_INDEX_FILENAME)));

            if (indexFile.ContainsKey("files") && indexFile["files"]!.Type == JTokenType.Array)
            {
                JArray files = (JArray)indexFile["files"]!;
                return files
                    .Where(f => f.Type == JTokenType.Object && ((JObject)f).ContainsKey("filename"))
                    .Select(t => Path.Combine(_packagePath, t["filename"]?.Value<string>()!));
            }

            return Enumerable.Empty<string>();
        }

        /*
        public IList<string> GetPackageFiles(List<string> searchParamList)
        {
            List<string> files = new();
            string packageType = string.Empty;

            JObject indexFile = JObject.Parse(File.ReadAllText(Path.Combine(_packagePath, PACKAGE_INDEX_FILENAME)));

            if (indexFile.ContainsKey("files") && indexFile["files"]!.Type == JTokenType.Array)
            {
                // if resourcetype is Search parameter and it exists in metadata, dont add it to files
                foreach (var item in (JArray)indexFile["files"])
                {
                    if (
                        item["resourceType"]?.Value<string>()?.ToLower() == searchParameter &&
                        searchParamList.Contains(item["url"]?.Value<string>()))
                    {
                        continue;
                    }
                    else
                    {
                        files.Add(Path.Combine(_packagePath, item["filename"]?.Value<string>()));
                    }
                }

            }

            return files;
        }
        */

        /*
        public List<string> GetSearchParams(JObject? metadata, ILogger logger)
        {
            var searchParams = new List<string>();
            try
            {
                if (metadata != null && metadata.Count > 0)
                {
                    var resources =
                    from p in metadata["rest"][0]["resource"]
                    select p;

                    if (resources != null)
                    {
                        foreach (JObject resource in resources)
                        {
                            if (resource.ContainsKey("searchParam"))
                            {
                                var searchParam =
                                from p in resource["searchParam"]
                                select p;
                                if (searchParam != null)
                                {
                                    foreach (var param in searchParam)
                                    {
                                        if (param != null && param is JObject)
                                        {
                                            searchParams.Add(Convert.ToString(param["definition"]));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                logger.LogInformation($"Error while reading serach params from metadata.");
            }

            return searchParams;
        }
        */
    }
}
