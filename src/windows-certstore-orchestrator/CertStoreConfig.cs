using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.Windows
{
    public class CertStoreConfig
    {
        /// <summary>
        /// This flag will determine if the Keberos connection to the remote machine will include the port number when sending the SPN. See <seealso cref="https://docs.microsoft.com/en-us/powershell/module/microsoft.powershell.core/new-pssessionoption?view=powershell-7.1"/>
        /// </summary>
        [JsonProperty("spnwithport")]
        [DefaultValue(false)]
        public bool SPNPortFlag { get; set; }

        public CertStoreConfig()
        {

        }
    }

}
