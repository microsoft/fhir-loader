// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace FhirLoader.Tool.FileSource
{
    public interface IFileSource
    {
        public IEnumerable<(string Name, Stream Data)> Files { get; }
    }
}
