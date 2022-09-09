using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace FhirLoader.Common
{
    public class BulkFileHandler : FhirFileHandler
    {
        private readonly Stream _inputStream;

        private IEnumerable<ProcessedBundle>? _bundles;

        public BulkFileHandler(Stream inputStream, string fileName, int bundleSize) : base(fileName, bundleSize)
        {
            _inputStream = inputStream;
        }

        public override IEnumerable<ProcessedBundle> FileAsBundles {  get
            {
                if (_bundles is null)
                    _bundles = ConvertToBundles();

                return _bundles;
            } 
        }

        private IEnumerable<ProcessedBundle> ConvertToBundles()
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
                            if (line is not null)
                                page.Add(line);
                        }  
                    }
                    yield return BuildBundle(page);
                }
            }
        }

        private ProcessedBundle BuildBundle(IEnumerable<string> page)
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

            return new ProcessedBundle
            {
                BundleText = bundle.ToString(Formatting.Indented),
                BundleCount = count,
                BundleFileName = FileName,
            };
        }
    }
}

