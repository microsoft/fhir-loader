using FhirLoader.Tool;

namespace FhirLoader.Tests.E2E
{
    public class UnitTest
    {
        private const string fhirURL = "https://workpacesdkfhir-sjbuildfhir.fhir.azurehealthcareapis.com/";
        private const int batchsize = 500;
        private const int concurrency = 50;

        /// <summary>
        /// E2E Test Case for bundle Files.
        /// By passing FolderPath,it will read all the files and processed to the Fhir URL. 
        /// </summary>
        [Fact]
        public async void LocalFhir_bundle_Test()
        {
            CommandOptions commandOptions = new()
            {
                FhirUrl = fhirURL,
                FolderPath = @"E:\Projects\fhir-loader\TestSamples\bundle",
                BatchSize = batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = concurrency//Must be betweeen 1 & 50,Default 50
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
                FhirUrl = fhirURL,
                FolderPath = @"E:\Projects\fhir-loader\TestSamples\ndjson",
                BatchSize = batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = concurrency//Must be betweeen 1 & 50,Default 50
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
                FhirUrl = fhirURL,
                BlobPath = "https://ahdssampledata.blob.core.windows.net/fhir/synthea-bundles-10/",
                BatchSize = batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = concurrency//Must be betweeen 1 & 50,Default 50
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
                FhirUrl = fhirURL,
                BlobPath = "https://ahdssampledata.blob.core.windows.net/fhir/synthea-ndjson-10/",
                BatchSize = batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = concurrency,//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Assert.Equal(1, response);
        }
    }
}