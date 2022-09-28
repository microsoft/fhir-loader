using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FhirLoader.Common
{
    public class ProcessedResource
    {
        public string? ResourceText;
        public int ResourceCount;
        public string? ResourceFileName;
        public string? ResourceType;
        public bool IsBundle = true;
        public string? ResourceId;
    }
}
