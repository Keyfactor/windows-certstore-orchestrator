using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.Windows
{
    [Serializable]
    internal class PSCertStoreException : Exception
    {
        public PSCertStoreException()
        {
        }

        public PSCertStoreException(string message) : base(message)
        {
        }

        public PSCertStoreException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected PSCertStoreException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
