// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;

namespace FhirLoader.CommandLineTool.FileSource
{
#pragma warning disable SA1602
    public enum PackageType
    {
        [Description("Conformance")]
        Conformance,

        [Description("fhir.ig")]
        FhirImplementationGuide,

        [Description("fhir.core")]
        FhirCore,
    }
#pragma warning restore SA1602
}
