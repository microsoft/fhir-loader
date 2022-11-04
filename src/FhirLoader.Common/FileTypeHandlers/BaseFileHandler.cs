using Microsoft.Extensions.Logging;

namespace FhirLoader.Common.FileTypeHandlers
{
    public abstract class BaseFileHandler
    {
        public BaseFileHandler(string fileName, int bundleSize)
        {
            FileName = fileName;
            BundleSize = bundleSize;
        }

        public readonly string FileName;

        public readonly int BundleSize;

        public abstract IEnumerable<ProcessedResource>? FileAsResourceList { get; }
    }
}
