using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FhirLoader.Common
{
    public class ProfileFileHandler : FhirFileHandler
    {
        private readonly Stream _inputStream;
        private readonly ILogger _logger;
        private IEnumerable<ProcessedBundle>? _bundles;

        public ProfileFileHandler(Stream inputStream, string fileName, int bundleSize, ILogger logger) : base(fileName, bundleSize)
        {
            _inputStream = inputStream;
            _logger = logger;
        }

        public override IEnumerable<ProcessedBundle> FileAsBundles
        {
            get
            {
                if (_bundles is null)
                    _bundles = ConvertToBundles();

                return _bundles;
            }
        }

        public IEnumerable<ProcessedBundle> ConvertToBundles()
        {
            JObject bundle;

            // We must read the full file
            using (StreamReader reader = new StreamReader(_inputStream))
                bundle = JObject.Parse(reader.ReadToEnd());

            yield return new ProcessedBundle
            {
                BundleFileName = FileName,
                BundleText = bundle.ToString(Formatting.Indented),
                BundleCount = 1,
                BundleUri = bundle.GetValue("url")?.Value<string>(),
                ResourceType = bundle.GetValue("resourceType")?.Value<string>()
            };


        }
    }
}
