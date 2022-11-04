// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace FhirLoader.Common.FileTypeHandlers
{
    public abstract class BaseFileHandler
    {
        public BaseFileHandler(string fileName, int bundleSize)
        {
            FileName = fileName;
            BundleSize = bundleSize;
        }

        public readonly string FileName;

        public readonly int BundleSize;

        public abstract IEnumerable<ProcessedResource>? FileAsResourceList { get; }
    }
}
