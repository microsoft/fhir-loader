using Newtonsoft.Json.Linq;
using static FhirLoader.Common.PackageTypeHelper;

namespace FhirLoader.Common
{
    public class PackageHelper
    {
        private string _packagePath;
        private const string indexjson = ".index.json";
        private const string packagejson = "package.json";
        private const string searchParameter = "searchparameter";

        public PackageHelper(string packagePath)
        {
            _packagePath = packagePath;
        }

        /// <summary>
        /// Check if the .index.json and package.json files exist in the given path.
        /// </summary>
        /// <returns></returns>
        public bool ValidateRequiredFiles()
        {
            if (!File.Exists($"{_packagePath}\\{indexjson}") || !File.Exists($"{_packagePath}\\{packagejson}"))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Check if the given type in the package.json is valid or not.
        /// It returns true if the type matches with Conformance, fhir.ig, or Core, otherwise false.
        /// </summary>
        /// <param name="packageType"></param>
        /// <returns></returns>
        public bool IsValidPackageType(out string? packageType)
        {
            JObject data = JObject.Parse(File.ReadAllText($"{_packagePath}/{packagejson}"));
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
        /// <param name="packageType"></param>
        /// <returns></returns>
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
        /// <returns></returns>
        public IList<string> GetPackageFiles(List<string> searchParamList)
        {
            IList<string> files = new List<string>();
            string packageType = string.Empty;

            JObject jobject = JObject.Parse(File.ReadAllText($"{_packagePath}\\{indexjson}"));
            if (jobject.Count > 0)
            {
                // if resourcetype is Search parameter and it exists in metadata, dont add it to files
                foreach (var item in jobject["files"])
                {
                    if (item["resourceType"]?.Value<string>()?.ToLower() == searchParameter && searchParamList.Contains(item["url"]?.Value<string>()))
                    {
                        continue;
                    }
                    else { files.Add($"{_packagePath}\\{item["filename"]?.Value<string>()}"); }
                }

            }
            return files;
        }

        public List<string> GetSearchParams(JObject? metadata)
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
                                        if (param != null)
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
            catch (Exception ex)
            {
                //_logger.LogInformation($"Error while reading serach params from metadata.");
            }
            return searchParams;
        }

    }

}
