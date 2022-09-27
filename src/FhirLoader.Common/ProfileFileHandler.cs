using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FhirLoader.Common
{
    public class ProfileFileHandler : FhirFileHandler
    {
        private readonly Stream _inputStream;
        private readonly ILogger _logger;
        private IEnumerable<ProcessedResource>? _bundles;

        public ProfileFileHandler(Stream inputStream, string fileName, int bundleSize, ILogger logger) : base(fileName, bundleSize)
        {
            _inputStream = inputStream;
            _logger = logger;
        }

        public override IEnumerable<ProcessedResource> FileAsBundles
        {
            get
            {
                if (_bundles is null)
                    _bundles = ConvertToBundles();

                return _bundles;
            }
        }

        public IEnumerable<ProcessedResource> ConvertToBundles()
        {
            JObject bundle;

            // We must read the full file
            using (StreamReader reader = new StreamReader(_inputStream))
                bundle = JObject.Parse(reader.ReadToEnd());

            yield return new ProcessedResource
            {
                ResourceFileName = FileName,
                ResourceText = bundle.ToString(Formatting.Indented),
                ResourceCount = 1,
                ResourceType = bundle.GetValue("resourceType")?.Value<string>(),
                IsBundle = false
            };


        }
    }
}
