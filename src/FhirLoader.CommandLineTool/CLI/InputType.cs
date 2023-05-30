// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace FhirLoader.CommandLineTool.CLI
{
    internal enum InputType
    {
        /// <summary>
        /// FHIR files on the local file system inside a folder.
        /// </summary>
        LocalFolder,

        /// <summary>
        /// FHIR files in an Azure Blob Storage container or folder.
        /// </summary>
        Blob,

        /// <summary>
        /// FHIR files in a NPM package.
        /// </summary>
        LocalPackage,
    }
}
