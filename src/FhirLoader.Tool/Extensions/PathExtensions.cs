using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.XPath;

namespace FhirLoader.Tool.Extensions
{
    public static class PathExtensions
    {
        public static string ResolveDirectoryPath(this string path)
        {
            string origPath = path;

            path = Environment.ExpandEnvironmentVariables(path);

            if (Directory.Exists(path))
            {
                return path;
            }

            if (Directory.Exists(Path.GetFullPath(path)))
            {
                return Path.GetFullPath(path);
            }

            throw new DirectoryNotFoundException($"Could not find directory {origPath}.");
        }
    }
}
