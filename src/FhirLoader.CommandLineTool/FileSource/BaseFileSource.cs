// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FhirLoader.CommandLineTool.FileTypeHandlers;
using Microsoft.Extensions.Logging;

namespace FhirLoader.CommandLineTool.FileSource
{
    public abstract class BaseFileSource : IFileSource
    {
        private IEnumerable<(string Name, Stream Data)>? _files;

        public BaseFileSource(string path, ILogger logger)
        {
            Logger = logger;
            Path = path;
        }

        internal string Path { get; }

        internal ILogger Logger { get; }

        public IEnumerable<(string Name, Stream Data)> Files
        {
            get
            {
                if (_files == null)
                {
                    _files = GetFiles();
                }

                return _files;
            }
        }

        internal abstract IEnumerable<(string Name, Stream Data)> GetFiles();
    }
}
