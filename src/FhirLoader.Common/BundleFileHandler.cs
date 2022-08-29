using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace Applied.FhirLoader
{
    public class BundleFileHandler : IFileHandler
    {
        private readonly Stream _inputStream;
        private readonly ILogger _logger;
        private readonly int _bundleSize;
        private readonly string _fileName;

        public BundleFileHandler(Stream inputStream, string fileName, int bundleSize, ILogger logger)
        {
            _inputStream = inputStream;
            _bundleSize = bundleSize;
            _logger = logger;
            _fileName = fileName;
        }

        public IEnumerable<(string bundle, int count)> ConvertToBundles()
        {
            JObject bundle;

            // We must read the full file to resolve any refs
            using (StreamReader reader = new StreamReader(_inputStream))
                bundle = JObject.Parse(reader.ReadToEnd());

            try
            {
                SyntheaReferenceResolver.ConvertUUIDs(bundle);
            }
            catch
            {
                _logger.LogError($"Failed to resolve references in input file {_fileName}.");
                throw;
            }

            var bundleResources = bundle.SelectTokens("$.entry[*].resource");
            if (bundleResources.Count() <= _bundleSize)
            {
                yield return (bundle.ToString(Formatting.Indented), bundleResources.Count());
            }
            
            while (true)
            {
                var resourceChunk = bundleResources.Take(_bundleSize);
                bundleResources = bundleResources.Skip(_bundleSize);

                if (resourceChunk.Count() == 0)
                    break;

                var newBundle = JObject.FromObject(new
                {
                    resourceType = "Bundle",
                    type = "batch",
                    entry =
                    from r in resourceChunk
                    select new
                    {
                        resource = r,
                        request = new
                        {
                            method = r.SelectToken("id") is not null ? "PUT" : "POST",
                            url = r.SelectToken("id") is not null ? $"{r["resourceType"]}/{r["id"]}" : r["resourceType"]
                        }
                    }
                });

                yield return (newBundle.ToString(Formatting.Indented), resourceChunk.Count());
            }
        }
    }
}
