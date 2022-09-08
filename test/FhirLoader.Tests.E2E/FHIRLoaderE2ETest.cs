using FhirLoader.Tool;
using FHIRLoader.Tool.Tests.E2E.Configuration;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using Xunit.Abstractions;

namespace FHIRLoader.Tool.Tests.E2E
{
    public class FHIRLoaderE2ETest
    {
        private readonly TestConfig _config;

        public FHIRLoaderE2ETest()
        {
            IConfigurationBuilder builder = new ConfigurationBuilder()
                .AddUserSecrets(Assembly.GetExecutingAssembly(), true)
                .AddEnvironmentVariables("TEST_");
            IConfigurationRoot root = builder.Build();
            TestConfig config = new TestConfig();
            root.Bind(config);

            _config = config;
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
                FhirUrl = _config.FhirURL,
                FolderPath = @"../../../../TestSamples/bundle",
                BatchSize = _config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = _config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Assert.Equal(0, response);
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
                FhirUrl = _config.FhirURL,
                FolderPath = @"../../../../TestSamples/ndjson",
                BatchSize = _config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = _config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Assert.Equal(0, response);
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
                FhirUrl = _config.FhirURL,
                BlobPath = _config.BlobBundlePath,
                BatchSize = _config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = _config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Assert.Equal(0, response);
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
                FhirUrl = _config.FhirURL,
                BlobPath = _config.BlobNDJsonPath,
                BatchSize = _config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = _config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Assert.Equal(0, response);
        }
    }
}