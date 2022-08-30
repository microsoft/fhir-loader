using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace FhirLoader.Common
{
    public class BulkFileHandler : IFileHandler
    {
        private readonly Stream _inputStream;
        private readonly ILogger _logger;
        private readonly int _bundleSize;
        private readonly string _fileName;

        public BulkFileHandler(Stream inputStream, string fileName, int bundleSize, ILogger logger)
        {
            _inputStream = inputStream;
            _bundleSize = bundleSize;
            _logger = logger;
            _fileName = fileName;
        }

        public IEnumerable<(string bundle, int count)> ConvertToBundles()
        {
            IEnumerable<string> page;
            int count = 0;

            using (var reader = new StreamReader(_inputStream))
            {
                var lines = StreamAsLines(reader);
                var pageNumber = 0;

                while (true)
                {
                    (page, count) = NextPage(lines!, pageNumber);
                    if (count == 0)
                        break;

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

                    yield return (bundle.ToString(Formatting.Indented), count);
                }
            }
        }

        private IEnumerable<string> StreamAsLines(StreamReader reader)
        {
            while(!reader.EndOfStream)
            {
                var line = reader.ReadLine(); 
                if (line != null)
                    yield return line;
            }
        }

        private (IEnumerable<string> page, int count) NextPage(IEnumerable<string> lines, int pageNumber)
        {
            var page = lines.Skip(pageNumber * _bundleSize).Take(_bundleSize);
            pageNumber++;

            int count;
            if (!page.TryGetNonEnumeratedCount(out count))
                count = page.Count();

            return (page, count);
        }
    }
}

