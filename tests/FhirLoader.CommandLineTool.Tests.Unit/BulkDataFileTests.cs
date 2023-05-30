// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FhirLoader.CommandLineTool.FileTypeHandlers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FhirLoader.CommandLineTool.Tests.Unit
{
    /// <summary>
    /// Class for testing the BulkDataFiles.
    /// </summary>
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

        /// <summary>
        /// Tests the conversion of a small bunk finle to a bundle.
        /// </summary>
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

        /// <summary>
        /// Tests the conversion of a medium bunk finle to a bundle.
        /// </summary>
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

        /// <summary>
        /// Tests the method of the bundle to ensure it is PUL when there are ids in the bulk file.
        /// </summary>
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
