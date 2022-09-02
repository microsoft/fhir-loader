using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FHIRLoader.Tool.Tests.E2E.Configuration
{
    internal class MyServiceConfig
    {
        public string FhirURL { get; set; }

        public int Batchsize { get; set; }

        public int Concurrency { get; set; }

        public string BlobBundlePath { get; set; }

        public string BlobndJsonPath { get; set; }
    }
}
