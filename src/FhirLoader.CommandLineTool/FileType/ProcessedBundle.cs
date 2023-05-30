﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace FhirLoader.CommandLineTool.FileType
{
    public class ProcessedBundle : BaseProcessedResource
    {
        public ProcessedBundle()
        {
            ResourceType = "Bundle";
        }
    }
}