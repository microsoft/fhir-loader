// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using CommandLine;
using CommandLine.Text;
using FhirLoader.Tool.Extensions;

namespace FhirLoader.Tool.CLI
{
    public class CommandOptions
    {
        public const string DefaultBundleSize = "500";
        public const string BundleSizeMin = "1";
        public const string BundleSizeMax = "500";

        public const string ConcurrencyMin = "1";
        public const string ConcurrencyMax = "50";
        public const string DefaultConcurrency = "8";

        [Option("folder", Required = false, HelpText = "Folder path to FHIR data to load.")]
        public string? FolderPath { get; set; }

        [Option("blob", Required = false, HelpText = "Url to public blob storage container with FHIR data to load. ")]
        public string? BlobPath { get; set; }

        [Option("package", Required = false, HelpText = "Package path to FHIR data to load.")]
        public string? PackagePath { get; set; }

        [Option("skip-errors", Required = false, Default = false, HelpText = "Continue sending resources on HTTP error.")]
        public bool SkipErrors { get; set; }

        [Option("strip-text", Required = false, Default = true, HelpText = "Strip the text of resources.")]
        public bool StripText { get; set; }

        [Option("fhir", Required = true, HelpText = "Base URL of your FHIR server.")]
        public string? FhirUrl { get; set; }

        [Option("batch", Required = false, HelpText = $"Size of bundles to split large files into when sending resources. Defaults to {DefaultBundleSize}. Must be between {BundleSizeMin} and {BundleSizeMax}"),]
        public int? BatchSize { get; set; }

        [Option("concurrency", Required = false, HelpText = $"Number of bundles to send in parallel. Defaults to {DefaultConcurrency}. Must be between {ConcurrencyMin} and {ConcurrencyMax}."),]
        public int? Concurrency { get; set; }

        [Option("tenant-id", Required = false, HelpText = "Specific tenant id of your FHIR Server (should not be needed)."),]
        public string? TenantId { get; set; }

        [Option("debug", Required = false, Default = false, HelpText = "Print more detailed information to the console.")]
        public bool Debug { get; set; }

        [Usage(ApplicationAlias = "applied-fhir-loader")]
        public static IEnumerable<Example> Examples
        {
            get
            {
                return new List<Example>() 
                {
                    new Example("Load synthea files to an Azure Health Data Services FHIR service", new CommandOptions { FolderPath = "~/synthea/fhir", FhirUrl = "https://workspace-fhirservice.fhir.azurehealthcareapis.com/" }),
                    new Example("Control the size of bundles sent", new CommandOptions { FolderPath = "~/synthea/fhir", FhirUrl = "https://workspace-fhirservice.fhir.azurehealthcareapis.com/", BatchSize = 100 }),
                    new Example("Use another tenant ID other than your default", new CommandOptions{ FolderPath = "~/synthea/fhir", FhirUrl = "https://workspace-fhirservice.fhir.azurehealthcareapis.com/", TenantId = "12345678-90ab-cdef-1234-567890abcdef" }),
                };
            }
        }

        public void Validate()
        {
            // Set Defaults
            Concurrency ??= Convert.ToInt32(DefaultConcurrency, NumberFormatInfo.InvariantInfo);
            BatchSize ??= Convert.ToInt32(DefaultBundleSize, NumberFormatInfo.InvariantInfo);

            // Ensure the folder path exists
            try
            {
                if (FolderPath is not null)
                {
                    FolderPath = FolderPath!.ResolveDirectoryPath();
                }
            }
            catch (DirectoryNotFoundException)
            {
                throw new ArgumentException($"Path {FolderPath} could not be found or is not a directory.");
            }

            // Ensure the package path exists
            try
            {
                if (PackagePath is not null)
                {
                    PackagePath = PackagePath!.ResolveDirectoryPath();
                }
            }
            catch (DirectoryNotFoundException)
            {
                throw new ArgumentException($"Path {PackagePath} could not be found or is not a directory.");
            }

            if (BatchSize < Convert.ToInt32(BundleSizeMin, NumberFormatInfo.InvariantInfo) || BatchSize > Convert.ToInt32(BundleSizeMax, NumberFormatInfo.InvariantInfo))
            {
                throw new ArgumentValidationException($"Batch {BatchSize} must be an integer between {BundleSizeMin} and {BundleSizeMax}");
            }

            if (Concurrency < Convert.ToInt32(ConcurrencyMin, NumberFormatInfo.InvariantInfo) || Concurrency > Convert.ToInt32(ConcurrencyMax, NumberFormatInfo.InvariantInfo))
            {
                throw new ArgumentValidationException($"Concurrency {Concurrency} must be an integer between {ConcurrencyMin} and {ConcurrencyMax}");
            }
        }
    }
}
