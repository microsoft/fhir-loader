using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace FhirLoader.Common
{
    public class BulkFileHandler : FhirFileHandler
    {
        private readonly Stream _inputStream;

        private IEnumerable<ProcessedResource>? _bundles;

        public BulkFileHandler(Stream inputStream, string fileName, int bundleSize) : base(fileName, bundleSize)
        {
            _inputStream = inputStream;
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

        private IEnumerable<ProcessedResource> ConvertToBundles()
        {

            using (var reader = new StreamReader(_inputStream))
            {
                while (!reader.EndOfStream)
                {
                    List<string> page = new List<string>();

                    for (int i = 0; i < BundleSize; i++)
                    {
                        if (!reader.EndOfStream)
                        {
                            var line = reader.ReadLine();
                            if (line is not null && line.StartsWith("{"))
                                page.Add(line);
                        }
                    }
                    yield return BuildBundle(page);
                }
            }
        }

        private ProcessedResource BuildBundle(IEnumerable<string> page)
        {
            try
            {
                var resourceChunk = page.Select(x => JObject.Parse(x));
                var bundle = JObject.FromObject(new
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
                            method = r.ContainsKey("id") ? "PUT" : "POST",
                            url = r.ContainsKey("id") ? $"{r["resourceType"]}/{r["id"]}" : r["resourceType"]
                        }
                    }
                });

                var count = bundle.ContainsKey("entry") ? bundle["entry"]!.Count() : 0;

                return new ProcessedResource
                {
                    ResourceText = bundle.ToString(Formatting.Indented),
                    ResourceCount = count,
                    ResourceFileName = FileName,
                    ResourceType = "Bundle"
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"Error converting NDJSON file to Bundle {FileName}.", ex);
            }
            
        }
    }
}

