using FhirLoader.Tool;
using FHIRLoader.Tool.Tests.E2E.Configuration;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using Xunit.Abstractions;

namespace FHIRLoader.Tool.Tests.E2E
{
    public class FHIRLoaderE2ETest
    {

        private static MyServiceConfig config;

        public FHIRLoaderE2ETest()
        {            
            IConfigurationBuilder builder = new ConfigurationBuilder()
                .AddUserSecrets(Assembly.GetExecutingAssembly(), true);
            IConfigurationRoot root = builder.Build();
            config = new MyServiceConfig();
            root.Bind(config);

        }

        /// <summary>
        /// E2E Test Case for bundle Files.
        /// By passing FolderPath,it will read all the files and processed to the Fhir URL. 
        /// </summary>
        [Fact]
        public async void LocalFhir_bundle_Test()
        {
            CommandOptions commandOptions = new()
            {
                FhirUrl = config.FhirURL,
                FolderPath = @"../../../../TestSamples\bundle",
                BatchSize = config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Assert.Equal(1, response);
        }

        /// <summary>
        /// E2E Test Case for NDJSon Files.
        /// By passing FolderPath,it will read all the files and processed to the Fhir URL. 
        /// </summary>
        [Fact]
        public async void LocalFhir_ndjson_Test()
        {
            CommandOptions commandOptions = new()
            {
                FhirUrl = config.FhirURL,
                FolderPath = @"../../../../TestSamples\ndjson",
                BatchSize = config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Assert.Equal(1, response);
        }

        /// <summary>
        /// E2E Test Case for bundle Files.
        /// By passing BlobPath,it will read all the files and processed to the Fhir URL. 
        /// </summary>
        [Fact]
        public async void BlobFhir_Bundle_Test()
        {
            CommandOptions commandOptions = new()
            {
                FhirUrl = config.FhirURL,
                BlobPath = config.BlobBundlePath,
                BatchSize = config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Assert.Equal(1, response);
        }

        /// <summary>
        /// E2E Test Case for ndjson Files.
        /// By passing BlobPath, it will read all the files and processed to the Fhir URL. 
        /// </summary>
        [Fact]
        public async void BlobFhir_ndjson_Test()
        {
            CommandOptions commandOptions = new()
            {
                FhirUrl = config.FhirURL,
                BlobPath = config.BlobndJsonPath,
                BatchSize = config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Assert.Equal(1, response);
        }
    }
}