using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FhirLoader.Common
{
    public class ProcessedBundle
    {
        public string? BundleText;
        public int BundleCount;
        public string? BundleFileName;
        public string? ResourceType;
        public string? BundleUri;
    }
}
