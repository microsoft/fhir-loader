// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace FhirLoader.CommandLineTool.CLI
{
    // https://github.com/commandlineparser/commandline/issues/146#issuecomment-523514501
    public class ArgumentValidationException : Exception
    {
        public ArgumentValidationException(string message)
            : base(message)
        {
        }
    }
}
