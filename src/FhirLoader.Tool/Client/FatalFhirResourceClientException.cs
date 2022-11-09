// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;

namespace FhirLoader.Tool.Client
{
    public class FatalFhirResourceClientException : Exception
    {
        public FatalFhirResourceClientException(string message, HttpStatusCode? code)
            : base(message)
        {
            Code = code;
        }

        public FatalFhirResourceClientException(string message, Exception inner, HttpStatusCode? code = null)
            : base(message, inner)
        {
            Code = code;
        }

        public FatalFhirResourceClientException()
        {
        }

        public FatalFhirResourceClientException(string message)
            : base(message)
        {
        }

        public FatalFhirResourceClientException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public HttpStatusCode? Code { get; set; }
    }
}
