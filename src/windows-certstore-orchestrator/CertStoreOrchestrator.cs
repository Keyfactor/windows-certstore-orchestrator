// Copyright 2021 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.
using Keyfactor.Platform.Extensions.Agents;
using Keyfactor.Platform.Extensions.Agents.Delegates;
using Keyfactor.Platform.Extensions.Agents.Enums;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security;
using System.Security.Cryptography.X509Certificates;

namespace Keyfactor.Extensions.Orchestrator.Windows
{
    [Job(JobTypes.INVENTORY)]
    public class CertStoreInventory : AgentJob
    {
        public override AnyJobCompleteInfo processJob(AnyJobConfigInfo config, SubmitInventoryUpdate submitInventory, SubmitEnrollmentRequest submitEnrollmentRequest, SubmitDiscoveryResults sdr)
        {
            StoreConfiguation = JsonConvert.DeserializeObject<CertStoreConfig>(config.Store.Properties.ToString(), new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
            return PerformInventory(config, submitInventory);
        }

        private AnyJobCompleteInfo PerformInventory(AnyJobConfigInfo config, SubmitInventoryUpdate submitInventory)
        {
            try
            {
                //https://docs.microsoft.com/en-us/windows/win32/seccrypto/system-store-locations

                Logger.Trace($"Begin Inventory for Cert Store {$@"\\{config.Store.ClientMachine}\{config.Store.StorePath}"}");


                List<AgentCertStoreInventoryItem> inventoryItems = new List<AgentCertStoreInventoryItem>();

                SecureString pw = new NetworkCredential(config.Server.Username, config.Server.Password).SecurePassword;
                WSManConnectionInfo connInfo = new WSManConnectionInfo(new Uri($"http://{config.Store.ClientMachine}:5985/wsman"));

                connInfo.IncludePortInSPN = StoreConfiguation.SPNPortFlag;
                connInfo.Credential = new PSCredential(config.Server.Username, pw);

                using (Runspace runspace = RunspaceFactory.CreateRunspace(connInfo))
                {
                    runspace.Open();
                    PowerShellCertStore psCertStore = new PowerShellCertStore(config.Store.ClientMachine, config.Store.StorePath, runspace);

                    if (psCertStore.Certificates.Count == 0)
                    {
                        return new AnyJobCompleteInfo() { Status = 3, Message = $"No certificates found in {config.Store.StorePath} certificate store on {config.Store.ClientMachine}" };
                    }

                    foreach (var cert in psCertStore.Certificates)
                    {
                        var thumbPrint = $"{cert.Thumbprint}";

                        var status = config.Store.Inventory.Any(c => c.Thumbprints.Contains(thumbPrint)) ? AgentInventoryItemStatus.Unchanged : AgentInventoryItemStatus.New;

                        if (string.IsNullOrEmpty(thumbPrint))
                            continue;

                        inventoryItems.Add(
                            new AgentCertStoreInventoryItem()
                            {
                                Certificates = new string[] { cert.CertificateData },
                                Alias = thumbPrint,
                                PrivateKeyEntry = cert.HasPrivateKey,
                                UseChainLevel = false,
                                ItemStatus = status
                            }
                        );

                    }
                    runspace.Close();
                }

                submitInventory.Invoke(inventoryItems);
                return new AnyJobCompleteInfo() { Status = 2, Message = "Inventory Complete" };
            }
            catch (PSCertStoreException psEx)
            {

                return new AnyJobCompleteInfo() { Status = 4, Message = psEx.Message };
            }
            catch (Exception ex)
            {
                return new AnyJobCompleteInfo() { Status = 4, Message = ex.Message };
            }
        }
    }

    [Job(JobTypes.MANAGEMENT)]
    public class CertStoreManagement : AgentJob
    {
        public override AnyJobCompleteInfo processJob(AnyJobConfigInfo config, SubmitInventoryUpdate submitInventory, SubmitEnrollmentRequest submitEnrollmentRequest, SubmitDiscoveryResults sdr)
        {
            StoreConfiguation = JsonConvert.DeserializeObject<CertStoreConfig>(config.Store.Properties.ToString(), new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
            AnyJobCompleteInfo complete = new AnyJobCompleteInfo()
            {
                Status = 4,
                Message = "Invalid Management Operation"
            };

            switch (config.Job.OperationType)
            {
                case AnyJobOperationType.Add:
                    complete = PerformAddition(config);
                    break;
                case AnyJobOperationType.Remove:
                    complete = PerformRemoval(config);
                    break;
            }
            return complete;
        }

        private AnyJobCompleteInfo PerformAddition(AnyJobConfigInfo config)
        {
            try
            {
                WSManConnectionInfo connInfo = new WSManConnectionInfo(new Uri($"http://{config.Store.ClientMachine}:5985/wsman"));
                connInfo.IncludePortInSPN = StoreConfiguation.SPNPortFlag;
                SecureString pw = new NetworkCredential(config.Server.Username, config.Server.Password).SecurePassword;
                connInfo.Credential = new PSCredential(config.Server.Username, pw);

                X509Certificate2 x509Cert = new X509Certificate2(Convert.FromBase64String(config.Job.EntryContents), config.Job.PfxPassword, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

                Logger.Trace($"Begin Add for Cert Store {$@"\\{config.Store.ClientMachine}\{config.Store.StorePath}"}");
                using (Runspace runspace = RunspaceFactory.CreateRunspace(connInfo))
                {
                    runspace.Open();
                    PowerShellCertStore psCertStore = new PowerShellCertStore(config.Store.ClientMachine, config.Store.StorePath, runspace);
                    psCertStore.AddCertificate(config);
                }
                return new AnyJobCompleteInfo() { Status = 2, Message = "Addition of certificate complete" };

            }
            catch (Exception ex)
            {

                Logger.Error(ex);
                return new AnyJobCompleteInfo() { Status = 4, Message = $"Addition of certificate to {config.Store.StorePath} on server {config.Store.ClientMachine} failed : {ex.Message}" };
            }

        }
        private AnyJobCompleteInfo PerformRemoval(AnyJobConfigInfo config)
        {
            Logger.Trace($"Begin Removal for Cert Store {$@"\\{config.Store.ClientMachine}\{config.Store.StorePath}"}");
            try
            {
                WSManConnectionInfo connInfo = new WSManConnectionInfo(new Uri($"http://{config.Store.ClientMachine}:5985/wsman"));
                connInfo.IncludePortInSPN = StoreConfiguation.SPNPortFlag;
                SecureString pw = new NetworkCredential(config.Server.Username, config.Server.Password).SecurePassword;
                connInfo.Credential = new PSCredential(config.Server.Username, pw);

                using (Runspace runspace = RunspaceFactory.CreateRunspace(connInfo))
                {
                    runspace.Open();
                    PowerShellCertStore psCertStore = new PowerShellCertStore(config.Store.ClientMachine, config.Store.StorePath, runspace);
                    psCertStore.RemoveCertificate(config.Job.Alias);
                    runspace.Close();
                }

                return new AnyJobCompleteInfo { Status = 2, Message = $"Successfully removed {config.Job.Alias} from {config.Store.StorePath} on {config.Store.ClientMachine}" };

            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return new AnyJobCompleteInfo() { Status = 4, Message = $"Failed removal of certificate from {config.Store.StorePath} on server {config.Store.ClientMachine}: {ex.Message}" };
            }
        }
    }
}
