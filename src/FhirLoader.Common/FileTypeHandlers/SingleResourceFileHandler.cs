using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FhirLoader.Common.FileTypeHandlers
{
    public class SingleResourceFileHandler : BaseFileHandler
    {
        private readonly Stream _inputStream;
        private readonly int _resourceCount;
        private readonly ILogger _logger;
        private IEnumerable<ProcessedResource>? _bundles;

        public SingleResourceFileHandler(Stream inputStream, string fileName, int resourceCount, ILogger logger) : base(fileName, resourceCount)
        {
            _inputStream = inputStream;
            _resourceCount = resourceCount;
            _logger = logger;
        }

        public override IEnumerable<ProcessedResource> FileAsResourceList
        {
            get
            {
                if (_bundles is null)
                    _bundles = ConvertToBundles();

                return _bundles;
            }
        }

        private IEnumerable<ProcessedResource> ConvertToBundles()
        {
            JObject bundle;
            // We must read the full file
            using (StreamReader reader = new StreamReader(_inputStream))
                bundle = JObject.Parse(reader.ReadToEnd());

            var resourceType = bundle.GetValue("resourceType")?.Value<string>();

            yield return new ProcessedResource
            {
                ResourceFileName = FileName,
                ResourceText = bundle.ToString(Formatting.Indented),
                ResourceCount = _resourceCount,
                ResourceType = resourceType,
                IsBundle = resourceType == "Bundle",
                ResourceId = bundle.GetValue("id")?.Value<string>()
            };
        }
    }
}
