using System.Text;
using System.Text.RegularExpressions;
using FhirLoader.Tool.FileTypeHandlers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FhirLoader.Tool.Tests.Unit
{
    public class BulkDataFileTests
    {
        private string GenerateTestFhirResource(bool addId)
        {
            var resource = new JObject();
            resource["resourceType"] = "test";

            if (addId)
            {
                resource["id"] = Guid.NewGuid();
            }

            return resource.ToString(Formatting.None);
        }

        private Stream GenerateTestBulkDataFile(int count, bool addId = true)
        {
            List<string> resources = new List<string>();

            for (int i = 0; i < count; i++)
            {
                resources.Add(GenerateTestFhirResource(addId));
            }

            return new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', resources)));
        }

        [Fact]
        public void GivenASmallBulkDataFile_WhenConvertingToProcessedResource_SingleBundleIsReturned()
        {
            var fileName = "Test1.ndjson";
            var stream = GenerateTestBulkDataFile(10);

            var bulk = new BulkDataFile(stream, fileName, 20);
            var resources = bulk.ConvertToBundles().ToList();

            Assert.Single(resources);
            Assert.Equal(10, resources[0].ResourceCount);
        }

        [Fact]
        public void GivenAMediumBulkDataFile_WhenConvertingToProcessedResource_SingleBundleIsReturned()
        {
            var fileName = "Test1.ndjson";
            var stream = GenerateTestBulkDataFile(30);

            var bulk = new BulkDataFile(stream, fileName, 20);
            var resources = bulk.ConvertToBundles().ToList();

            Assert.Equal(2, resources.Count());
            Assert.Equal(20, resources[0].ResourceCount);
            Assert.Equal(10, resources[1].ResourceCount);
        }

        [Fact]
        public void GivenABulkDataFileWithIds_WhenConvertingToProcessedResource_BundleMethodIsPut()
        {
            var fileName = "Test1.ndjson";
            var stream = GenerateTestBulkDataFile(10);

            var bulk = new BulkDataFile(stream, fileName, 20);
            var resources = bulk.ConvertToBundles().ToList();

            Assert.Single(resources);
            var count = Regex.Matches(resources[0].ResourceText!, "PUT").Count;
            Assert.Equal(10, count);
        }
    }
}
