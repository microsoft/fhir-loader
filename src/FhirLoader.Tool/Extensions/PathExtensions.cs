// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
