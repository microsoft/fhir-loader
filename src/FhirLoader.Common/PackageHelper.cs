using Newtonsoft.Json.Linq;
using System.ComponentModel;
using static FhirLoader.Common.PackageTypeHelper;

namespace FhirLoader.Common
{
    public class PackageHelper
    {
        private string _packagePath;
        private const string indexjson = ".index.json";
        private const string packagejson = "package.json";

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
        public IList<string> GetPackageFiles()
        {
            IList<string> files = new List<string>();
            string packageType = string.Empty;
            JObject jobject = JObject.Parse(File.ReadAllText($"{_packagePath}\\{indexjson}"));
            if (jobject.Count > 0)
            {
                files = jobject["files"]!.Select(t => $"{_packagePath}\\{t["filename"]?.Value<string>()}").ToList();
            }

            return files;
        }        

    }

}
