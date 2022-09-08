namespace FHIRLoader.Tool.Tests.E2E.Configuration
{
    public class TestConfig
    {
        public TestConfig()
        {
            FhirURL = "https://samplestest-fhirtest.fhir.azurehealthcareapis.com";
            BlobBundlePath = "https://ahdssampledata.blob.core.windows.net/fhir/synthea-bundles-10/";
            BlobNDJsonPath = "https://ahdssampledata.blob.core.windows.net/fhir/synthea-bundles-10/";
            Concurrency = 2;
            Batchsize = 300;
        }

        public string FhirURL { get; set; }

        public int Batchsize { get; set; }

        public int Concurrency { get; set; }

        public string BlobBundlePath { get; set; }

        public string BlobNDJsonPath { get; set; }
    }
}
