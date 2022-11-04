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

        public PackageHelper(string packagePath)
        {
            _packagePath = packagePath;
        }

        /// <summary>
        /// Check if the .index.json and package.json files exist in the given path.
        /// </summary>
        /// <returns></returns>
        public bool ValidateRequiredFiles() => File.Exists(Path.Combine(_packagePath, PACKAGE_INDEX_FILENAME)) && File.Exists(Path.Combine(_packagePath, packagejson));

        /// <summary>
        /// Check if the given type in the package.json is valid or not.
        /// It returns true if the type matches with Conformance, fhir.ig, or Core, otherwise false.
        /// </summary>
        /// <param name="packageType"></param>
        /// <returns></returns>
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
    }
}
