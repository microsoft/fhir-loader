// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace FhirLoader.Tool
{
    public class ProcessedResource
    {
        public string? ResourceText { get; set; }

        public int ResourceCount { get; set; }

        public string? ResourceFileName { get; set; }

        public string? ResourceType { get; set; }

        public bool IsBundle { get; set; } = true;

        public string? ResourceId { get; set; }
    }
}
