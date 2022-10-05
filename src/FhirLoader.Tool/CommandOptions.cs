using CommandLine;
using CommandLine.Text;


namespace FhirLoader.Tool
{
    public class CommandOptions
    {
        private const string DEFAULT_BUNDLE_SIZE = "500";
        private const string BUNDLE_SIZE_MIN = "1";
        private const string BUNDLE_SIZE_MAX = "500";

        private const string CONCURRENCY_MIN = "1";
        private const string CONCURRENCY_MAX = "50";
        private const string DEFAULT_CONCURRENCY = "8";

        [Option("folder", Required = false, HelpText = "Folder path to FHIR data to load.")]
        public string? FolderPath { get; set; }

        [Option("blob", Required = false, HelpText = "Url to public blob storage container with FHIR data to load. ")]
        public string? BlobPath { get; set; }

        [Option("fhir", Required = true, HelpText = "Base URL of your FHIR server.")]
        public string? FhirUrl { get; set; }

        [Option("batch", Required = false, HelpText = $"Size of bundles to split large files into when sending resources. Defaults to {DEFAULT_BUNDLE_SIZE}. Must be between {BUNDLE_SIZE_MIN} and {BUNDLE_SIZE_MAX}"),]
        public int? BatchSize { get; set; }

        [Option("concurrency", Required = false, HelpText = $"Number of bundles to send in parallel. Defaults to {DEFAULT_CONCURRENCY}. Must be between {CONCURRENCY_MIN} and {CONCURRENCY_MAX}."),]
        public int? Concurrency { get; set; }

        [Option("tenant-id", Required = false, HelpText = "Tenant ID of your FHIR server (not needed for your default directory)."),]
        public string? TenantId { get; set; }

        [Option("debug", Required = false, Default = false, HelpText = "Print more detailed information to the console.")]
        public bool Debug { get; set; }

        [Usage(ApplicationAlias = "applied-fhir-loader")]
        public static IEnumerable<Example> Examples
        {
            get
            {
                return new List<Example>() {
                    new Example("Load synthea files to an Azure Health Data Services FHIR service", new CommandOptions { FolderPath = "~/synthea/fhir", FhirUrl = "https://workspace-fhirservice.fhir.azurehealthcareapis.com/" }),
                    new Example("Control the size of bundles sent", new CommandOptions { FolderPath = "~/synthea/fhir", FhirUrl = "https://workspace-fhirservice.fhir.azurehealthcareapis.com/", BatchSize = 100 }),
                    new Example("Use another tenant ID other than your default", new CommandOptions{ FolderPath = "~/synthea/fhir", FhirUrl = "https://workspace-fhirservice.fhir.azurehealthcareapis.com/", TenantId = "12345678-90ab-cdef-1234-567890abcdef" }),
                };
            }
        }

        public void Validate()
        {
            // Set Defaults
            Concurrency = Concurrency ?? Convert.ToInt32(DEFAULT_CONCURRENCY);
            BatchSize = BatchSize ?? Convert.ToInt32(DEFAULT_BUNDLE_SIZE);

            // Ensure the folder path exists
            if (FolderPath is not null && !Directory.Exists(FolderPath))
            {
                throw new ArgumentException($"Path {FolderPath} could not be found or is not a directory.");
            }

            if (BatchSize < Convert.ToInt32(BUNDLE_SIZE_MIN) || BatchSize > Convert.ToInt32(BUNDLE_SIZE_MAX))
            {
                throw new ArgumentValidationException($"Batch {BatchSize} must be an integer between {BUNDLE_SIZE_MIN} and {BUNDLE_SIZE_MAX}");
            }

            if (Concurrency < Convert.ToInt32(CONCURRENCY_MIN) || Concurrency > Convert.ToInt32(CONCURRENCY_MAX))
            {
                throw new ArgumentValidationException($"Concurrency {Concurrency} must be an integer between {CONCURRENCY_MIN} and {CONCURRENCY_MAX}");
            }
        }
    }

    // https://github.com/commandlineparser/commandline/issues/146#issuecomment-523514501
    public class ArgumentValidationException : Exception
    {
        public ArgumentValidationException(string message) : base(message) { }
    }
}
