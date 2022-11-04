using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FhirLoader.Common
{
    public class FatalFhirResourceClientException : Exception
    {
        public HttpStatusCode? Code;

        public FatalFhirResourceClientException(string message, HttpStatusCode? code) : base(message)
        {
            Code = code;
        }
        public FatalFhirResourceClientException(string message, Exception inner, HttpStatusCode? code = null) : base(message, inner)
        {
            Code = code;
        }
    }
}
