// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace FhirLoader.Common
{
    public class ProcessedResource
    {
        public string? ResourceText;
        public int ResourceCount;
        public string? ResourceFileName;
        public string? ResourceType;
        public bool IsBundle = true;
        public string? ResourceId;
    }
}
