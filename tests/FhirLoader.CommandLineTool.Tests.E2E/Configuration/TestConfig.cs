// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace FhirLoader.CommandLineTool.Tests.E2E.Configuration
{
    /// <summary>
    /// Class for test configuration.
    /// </summary>
    public class TestConfig
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestConfig"/> class.
        /// </summary>
        public TestConfig()
        {
            FhirURL = "https://samplestest-fhirtest.fhir.azurehealthcareapis.com";
            BlobBundlePath = "https://ahdssampledata.blob.core.windows.net/fhir/synthea-bundles-10/";
            BlobNDJsonPath = "https://ahdssampledata.blob.core.windows.net/fhir/synthea-bundles-10/";
            Concurrency = 2;
            Batchsize = 300;
        }

        /// <summary>
        /// Gets or sets URI for the FHIR server.
        /// </summary>
        public string FhirURL { get; set; }

        /// <summary>
        /// Gets or sets the batch size.
        /// </summary>
        public int Batchsize { get; set; }

        /// <summary>
        /// Gets or sets the concurrency for the test execution.
        /// </summary>
        public int Concurrency { get; set; }

        /// <summary>
        /// Gets or sets the path to the bundles file in blob storage.
        /// </summary>
        public string BlobBundlePath { get; set; }

        /// <summary>
        /// Gets or sets the path to FHIR bulk files in blob storage.
        /// </summary>
        public string BlobNDJsonPath { get; set; }
    }
}
