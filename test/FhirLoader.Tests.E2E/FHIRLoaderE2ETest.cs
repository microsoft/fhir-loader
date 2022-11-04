using FhirLoader.Common;
using FhirLoader.Common.Helpers;
using FhirLoader.Tool;
using FHIRLoader.Tool.Tests.E2E.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Reflection;

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
                FolderPath = @"../../../TestData/bundle",
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
                FolderPath = @"../../../../TestData/ndjson",
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

        /// <summary>
        /// Test Case for .Index Json.
        /// By passing the package path,it will check if .index json exists or not.
        /// Throw an exception if the file does not exist.
        /// </summary>
        [Fact]
        public async void LocalFhir_packagewithoutindexjson_Test()
        {
            CommandOptions commandOptions = new()
            {
                FhirUrl = _config.FhirURL,
                PackagePath = @"../../../TestData/packagewithoutindexjson",
                BatchSize = _config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = _config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Action throwingAction = () =>
            {
                throw new ArgumentException();
            };
            Assert.Throws<ArgumentException>(throwingAction);


        }

        /// <summary>
        /// Test Case for Package json.
        /// By passing the package path,it will check if package json exists or not.
        /// Throw an exception if the file does not exist.
        /// </summary>
        [Fact]
        public async void LocalFhir_packagewithoutpackagejson_Test()
        {
            CommandOptions commandOptions = new()
            {
                FhirUrl = _config.FhirURL,
                PackagePath = @"../../../TestData/packagewithoutpackagejson",
                BatchSize = _config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = _config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Action throwingAction = () =>
            {
                throw new ArgumentException();
            };
            Assert.Throws<ArgumentException>(throwingAction);


        }

        /// <summary>
        /// Test Case for wrong package folder.
        /// By passing a wrong path or other nested folder inside the package folder (e.g example, openapi),
        /// it will check if.index and package json exist, otherwise throw an exception.
        /// </summary>
        [Fact]
        public async void LocalFhir_ifpassingwrongpath_Test()
        {
            CommandOptions commandOptions = new()
            {
                FhirUrl = _config.FhirURL,
                PackagePath = @"../../../TestData/package/example",
                BatchSize = _config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = _config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Action throwingAction = () =>
            {
                throw new ArgumentException();
            };
            Assert.Throws<ArgumentException>(throwingAction);


        }

        /// <summary>
        /// Test Case for Package Files.
        /// By passing Package Path,it will read all the files inside the package folder and process them to the Fhir URL.
        /// </summary>
        [Fact]
        public async void LocalFhir_package_Test()
        {
            CommandOptions commandOptions = new()
            {
                FhirUrl = _config.FhirURL,
                PackagePath = @"../../../TestData/package",
                BatchSize = _config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = _config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Assert.Equal(0, response);
        }

        /// <summary>
        /// Check that the json file has the resource type bundle.
        /// The tool will skip those files which do not have the resource type value as "bundle".
        /// </summary>
        [Fact]
        public void CheckIfFileswithoutbundleresourceTypeExist_Test()
        {
            var inputBundles = Directory
               .EnumerateFiles(@"../../../TestData/bundle", "*", SearchOption.AllDirectories)
               .Where(s => s.EndsWith(".json"));
            int i = 0;
            foreach (var filePath in inputBundles.OrderBy(x => x))
            {
                if (filePath.EndsWith(".json"))
                {
                    JObject data = JObject.Parse(File.ReadAllText(filePath));
                    if (data.GetValue("resourceType")?.Value<string>()?.ToLower() != "bundle")
                    {
                        i++;
                    }
                }
            }
            if (i > 0)
            {
                Assert.Fail("Files that do not have a resource type as  'bundle' will be skipped.");
            }

        }

        /// <summary>
        /// Test case to check type in package json.
        /// Check to see if the type in package.json matches values such as Conformance, fhir.ig, and Core.�
        /// returns true if matched, otherwise false.
        /// </summary>
        [Fact]
        public async void checkIfPackageTypeExists_Test()
        {

            CommandOptions commandOptions = new()
            {
                FhirUrl = _config.FhirURL,
                PackagePath = @"../../../TestData/missingpackagetype",
                BatchSize = _config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = _config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Action throwingAction = () =>
            {
                throw new ArgumentException("Package type is not valid. Skipping the loading process.");
            };
            Assert.Throws<ArgumentException>(throwingAction);

        }

        /// <summary>
        /// Test Case for Search paramater resource type.
        /// By passing the package path,
        /// 1. First load the metadata response.
        /// 2. Load the all defination property from metadata response.
        /// 3. Compare it with .index.json file's url property.
        ///    It will skip the resource if it is a search parameter and it exists in the SearchParameter list from metadata.
        /// 4. The rest of all resources will be posted to the server.
        /// 5. A ReIndex api call will be initiated to index the search parameter..
        /// Throw an exception if the file does not exist.
        /// </summary>
        [Fact]
        public async void LocalFhir_packageSearchparamater_Test()
        {
            CommandOptions commandOptions = new()
            {
                FhirUrl = _config.FhirURL,
                PackagePath = @"../../../TestData/searchparamaterpackage",
                BatchSize = _config.Batchsize,//Must be betweeen 1 & 500,Default 500
                Concurrency = _config.Concurrency//Must be betweeen 1 & 50,Default 50
            };
            int response = await Program.Run(commandOptions);
            Assert.Equal(0, response);
        }

        /// <summary>
        ///Skipping the resources if it is a search parameter and it exists in the search parameter list from metadata. 
        /// </summary>
        [Fact]
        public async Task LocalFhir_SkippSearchparamater_Test()
        {
            ILogger<FHIRLoaderE2ETest> logger = ApplicationLogging.Instance.CreateLogger<FHIRLoaderE2ETest>();
            var client = new FhirResourceClient(_config.FhirURL, _config.Concurrency, false, logger);
            JObject? metadata = await client.Get("/metadata");
            PackageHelper helper = new(@"../../../TestData/searchparamaterpackage");
            var searchParamList = helper.GetSearchParams(metadata, logger);

            int i = 0;
            JObject jobject = JObject.Parse(File.ReadAllText(@"../../../TestData/searchparamaterpackage/.index.json"));
            if (jobject.Count > 0)
            {
                // if resourcetype is Search parameter and it exists in metadata, dont add it to files
                foreach (var item in jobject["files"])
                {
                    if (item["resourceType"]?.Value<string>()?.ToLower() == "searchparameter" && searchParamList.Contains(item["url"]?.Value<string>()))
                    {
                        i++;
                    }
                }

            }
            if (i > 0)
            {
                Assert.Fail("Skipping the resources if it is a search parameter and it exists in the search parameter list from metadata.");
            }
        }


    }
}