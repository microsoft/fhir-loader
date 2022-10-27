using Microsoft.Extensions.Logging;

namespace FhirLoader.Common
{
    public abstract class FhirFileHandler
    {
        public FhirFileHandler(string fileName, int bundleSize)
        {
            FileName = fileName;
            BundleSize = bundleSize;    
        }

        public readonly string FileName;

        public readonly int BundleSize;

        public abstract IEnumerable<ProcessedResource>? FileAsResourceList { get; }
    }
}
