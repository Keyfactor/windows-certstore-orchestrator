// Copyright 2021 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.
using Keyfactor.Platform.Extensions.Agents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace Keyfactor.Extensions.Orchestrator.Windows
{
    class PowerShellCertStore
    {
        public string ServerName { get; set; }
        public string StorePath { get; set; }
        public Runspace Runspace { get; set; }
        public List<PSCertificate> Certificates { get; set; }
        public PowerShellCertStore(string serverName, string storePath, Runspace runspace)
        {
            ServerName = serverName;
            StorePath = storePath;
            Runspace = runspace;
            Initalize();
        }

        public void RemoveCertificate(string thumbprint)
        {
            try
            {
                using (PowerShell ps = PowerShell.Create())
                {
                    ps.Runspace = Runspace;
                    string removeScript = $@"
                        $ErrorActionPreference = 'Stop'
                        $certStore = New-Object System.Security.Cryptography.X509Certificates.X509Store('{StorePath}','LocalMachine')
                        $certStore.Open('MaxAllowed')
                        $certToRemove = $certStore.Certificates.Find(0,'{thumbprint}',$false)
                        if($certToRemove.Count -gt 0) {{
                            $certStore.Remove($certToRemove[0])
                        }}
                        $certStore.Close()
                        $certStore.Dispose()
                    ";

                    ps.AddScript(removeScript);

                    var certs = ps.Invoke();
                    if (ps.HadErrors)
                    {
                        throw new PSCertStoreException($"Error removing certificate in {StorePath} store on {ServerName}.");
                    }
                }
            }
            catch (Exception)
            {

                throw;
            }
        }

        private void Initalize()
        {
            Certificates = new List<PSCertificate>();
            try
            {
                using (PowerShell ps = PowerShell.Create())
                {

                    ps.Runspace = Runspace;
                    //todo: accept StoreType and Store Name enum for which to open
                    string certStoreScript = $@"
                                $certStore = New-Object System.Security.Cryptography.X509Certificates.X509Store('{StorePath}','LocalMachine')
                                $certStore.Open('ReadOnly')
                                $certs = $certStore.Certificates
                                $certStore.Close()
                                $certStore.Dispose()
                                foreach ( $cert in $certs){{ 
                                    $cert | Select-Object -Property Thumbprint, RawData, HasPrivateKey
                                }}";

                    ps.AddScript(certStoreScript);

                    var certs = ps.Invoke();

                    foreach (var c in certs)
                    {
                        Certificates.Add(new PSCertificate()
                        {
                            Thumbprint = $"{c.Properties["Thumbprint"]?.Value}",
                            HasPrivateKey = bool.Parse($"{c.Properties["HasPrivateKey"]?.Value}"),
                            RawData = (byte[])c.Properties["RawData"]?.Value
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                throw new PSCertStoreException($"Error listing {StorePath} certificate store on {ServerName}: {ex.Message}");
            }
        }

        public void AddCertificate(AnyJobConfigInfo config)
        {
            try
            {
                using (PowerShell ps = PowerShell.Create())
                {
                    ps.Runspace = Runspace;

                    //TODO: Also possible to add non keyed certs. Test case.
                    string funcScript = @"
                                        $ErrorActionPreference = ""Stop""

                                        function InstallPfxToMachineStore([byte[]]$bytes, [string]$password, [string]$storeName) {
                                            $certStore = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Store -ArgumentList $storeName, ""LocalMachine""
                                            $certStore.Open(5)
                                            $cert = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $bytes, $password, 18 <# Persist, Machine #>
                                            $certStore.Add($cert)
                                            $certStore.Close();
                                        }";

                    ps.AddScript(funcScript).AddStatement();
                    ps.AddCommand("InstallPfxToMachineStore")
                        .AddParameter("bytes", Convert.FromBase64String(config.Job.EntryContents))
                        .AddParameter("password", config.Job.PfxPassword)
                        .AddParameter("storeName", $@"\\{config.Store.ClientMachine}\{config.Store.StorePath}");

                    ps.Invoke();

                    if (ps.HadErrors)
                    {
                        throw new PSCertStoreException($"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ps.Streams.Error.ReadAll().First().ErrorDetails.Message}");
                    }
                }
            }
            catch (Exception)
            {

                throw;
            }

        }
    }
}
