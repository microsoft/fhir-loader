using System;
using System.Collections.Generic;
using System.Text;

namespace FHIRBulkImport
{
    class TransientError : Exception
    {
        public TransientError(string message) :base (message)
        {
           
        }
    }
}
