using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.IO;

// Azure Management dependencies
using Microsoft.Rest.Azure.Authentication;
using Microsoft.Azure.Management.Compute;
using Microsoft.Azure.Management.Compute.Models;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.ResourceManager.Models;
using Microsoft.Azure.Management.Storage;
using Microsoft.Azure.Management.Storage.Models;
using Microsoft.Azure.Management.Network;
using Microsoft.Azure.Management.Network.Models;

namespace ConsoleApplication
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
            var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
            var secret = Environment.GetEnvironmentVariable("AZURE_SECRET");
            var subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");
            if(new List<string>{ tenantId, clientId, secret, subscriptionId }.Any(i => String.IsNullOrEmpty(i))) {
                Console.WriteLine("Please provide ENV vars for AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_SECRET and AZURE_SUBSCRIPTION_ID.");
            }
            else
            {
                RunSample(tenantId, clientId, secret, subscriptionId).Wait();                
            }
        }

        public static async Task RunSample(string tenantId, string clientId, string secret, string subscriptionId)
        {
            // Build the service credentials and Azure Resource Manager clients
            var serviceCreds = await ApplicationTokenProvider.LoginSilentAsync(tenantId, clientId, secret);
            var resourceClient = new ResourceManagementClient(serviceCreds);
            resourceClient.SubscriptionId = subscriptionId;
            var computeClient = new ComputeManagementClient(serviceCreds);
            computeClient.SubscriptionId = subscriptionId;
            var storageClient = new StorageManagementClient(serviceCreds);
            storageClient.SubscriptionId = subscriptionId;
            var networkClient = new NetworkManagementClient(serviceCreds);
            networkClient.SubscriptionId = subscriptionId;

            var resourceGroupName = "sample-dotnet-loadbalancer-group";
            var westus = "westus";

            Write("Creating resource group: {0}", westus);
            resourceClient.ResourceGroups.CreateOrUpdate(resourceGroupName, new ResourceGroup { Location = westus});

            Random r = new Random();
            int postfix = 145699; //r.Next(0, 1000000);
            var storageAccountName = String.Format("dotnetstor{1}", resourceGroupName, postfix);
            Write("Creating a premium storage account with encryption off named {0} in resource group {1}", storageAccountName, resourceGroupName);
            var storCreateParams = new StorageAccountCreateParameters {
                Location = westus,
                Sku = new Microsoft.Azure.Management.Storage.Models.Sku(SkuName.PremiumLRS, SkuTier.Premium),
                Kind = Microsoft.Azure.Management.Storage.Models.Kind.Storage,
                Encryption = new Encryption(new EncryptionServices(new EncryptionService(false))),
            };
            var storageAccount = storageClient.StorageAccounts.Create(resourceGroupName, storageAccountName, storCreateParams);

            Write("Creating a virtual network for the load balanced VMs"); 
            var vnetCreateParams = new VirtualNetwork {
                Location = westus,
                AddressSpace = new AddressSpace{ AddressPrefixes = new []{ "10.0.0.0/16" } },
                DhcpOptions = new DhcpOptions{ DnsServers = new []{ "8.8.8.8" } },
                Subnets = new List<Subnet>{ new Subnet{ Name = "dotnetsubnet", AddressPrefix = "10.0.0.0/24" } }
            };
            var vnet = networkClient.VirtualNetworks.CreateOrUpdate(resourceGroupName, "sample-dotnet-vnet", vnetCreateParams);

            Write("Creating a public IP address for the load balancer");            
            var publicIpCreateParams = new PublicIPAddress {
                Location = westus,
                PublicIPAllocationMethod = IPAllocationMethod.Dynamic,
                DnsSettings = new PublicIPAddressDnsSettings{ DomainNameLabel = "sample-dotnet-domain-name-label" }
            };
            var pubIp = networkClient.PublicIPAddresses.CreateOrUpdate(resourceGroupName, "sample-dotnet-pubip", publicIpCreateParams);

            var lbName = "sample-loadbalancer";
            Write("Building the frontend IP configuration to expose the public IP"); 
            var frontendIPConfig = new FrontendIPConfiguration{
                Id = ResourceId(resourceGroupName, lbName, "frontendIPConfigurations", "sample-frontend-config"),
                Name = "sample-frontend-config",
                PrivateIPAllocationMethod = IPAllocationMethod.Dynamic,
                PublicIPAddress = pubIp
            };

            Write("Building the backend IP address pool for the load balancer"); 
            var backendAddressPool = new BackendAddressPool{
                Id = ResourceId(resourceGroupName, lbName, "backendAddressPools", "sample-backend-pool"),
                Name = "sample-backend-pool"
            };

            Write("Building the health probe for the load balancer, which will probe the http://publicIPAddress/canary to determine the health of the applicaiton."); 
            var probe = new Probe{
                Id = ResourceId(resourceGroupName, lbName, "probes", "sample-probe"),
                Name = "sample-probe",
                Protocol = ProbeProtocol.Http,
                Port = 80,
                IntervalInSeconds = 15,
                NumberOfProbes = 4,
                RequestPath = "/canary"
            };

            Write("Creating a load balancer exposing port 80 with inbound nat rules for port 21, 23 mapping back to port 22 on two backend VMs"); 
            var lb = networkClient.LoadBalancers.CreateOrUpdate(resourceGroupName, lbName, new LoadBalancer{
                Location = westus,
                FrontendIPConfigurations = new List<FrontendIPConfiguration>{frontendIPConfig},
                BackendAddressPools = new List<BackendAddressPool>{backendAddressPool},
                Probes = new List<Probe>{probe},
                LoadBalancingRules = new List<LoadBalancingRule>{
                    new LoadBalancingRule{
                        Name = "sample-http-rule",
                        Protocol = ProbeProtocol.Tcp,
                        FrontendPort = 80,
                        BackendPort = 80,
                        IdleTimeoutInMinutes = 4,
                        EnableFloatingIP = false,
                        LoadDistribution = LoadDistribution.Default,
                        FrontendIPConfiguration = frontendIPConfig,
                        BackendAddressPool = backendAddressPool,
                        Probe = probe
                    }
                },
                InboundNatRules = new List<InboundNatRule>{
                    new InboundNatRule{
                        Name = "sample-inbound-ssh-rule1",
                        Protocol = ProbeProtocol.Tcp,
                        FrontendPort = 21,
                        BackendPort = 22,
                        EnableFloatingIP = false,
                        IdleTimeoutInMinutes = 4,
                        FrontendIPConfiguration = frontendIPConfig,
                    },
                    new InboundNatRule{
                        Name = "sample-inbound-ssh-rule2",
                        Protocol = ProbeProtocol.Tcp,
                        FrontendPort = 23,
                        BackendPort = 22,
                        EnableFloatingIP = false,
                        IdleTimeoutInMinutes = 4,
                        FrontendIPConfiguration = frontendIPConfig,
                    }
                }
            });

            Write("Create an Availability set for the load balanced VMs");
            var availabilitySet = computeClient.AvailabilitySets.CreateOrUpdate(resourceGroupName, "sample-availabilitySet", new AvailabilitySet{ Location = westus });

            // Create the Virtual Machine given these parameters
            var vms = new List<VirtualMachine>();
            vms.Add(CreateVM(computeClient, networkClient, westus, resourceGroupName, "firstvm", storageAccount, vnet.Subnets.First(), lb.BackendAddressPools.First(), lb.InboundNatRules.First(), availabilitySet));
            vms.Add(CreateVM(computeClient, networkClient, westus, resourceGroupName, "secondvm", storageAccount, vnet.Subnets.First(), lb.BackendAddressPools.First(), lb.InboundNatRules.Last(), availabilitySet));

            Write("Listing all of the resources within the group");
            resourceClient.ResourceGroups.ListResources(resourceGroupName).ToList().ForEach(resource => {
                Write("\tName: {0}, Id: {1}", resource.Name, resource.Id);
            });
            Write(Environment.NewLine);

            // Export the resource group template
            ExportResourceGroupTemplate(resourceClient, resourceGroupName);
 
            vms.ForEach(vm => {
               Write("Connect to your new virtual machine via: `ssh -p {0} {1}@{2}`. Admin Password is: {3}", 
               vm.Name == "sample-dotnet-vm-firstvm" ? 21 : 23, 
               vm.OsProfile.AdminUsername, 
               pubIp.DnsSettings.Fqdn, 
               vm.OsProfile.AdminPassword); 
            });

            Write("Press any key to continue and delete the sample resources");
            Console.ReadLine();

            Write("Deleting resource group {0}", resourceGroupName);
            resourceClient.ResourceGroups.Delete(resourceGroupName);
        }

        private static VirtualMachine CreateVM(
            ComputeManagementClient computeClient, 
            NetworkManagementClient networkClient, 
            string location, 
            string resourceGroupName, 
            string vmName, 
            StorageAccount storageAccount, 
            Subnet subnet, 
            BackendAddressPool backendAddressPool, 
            InboundNatRule inboundNatRule,
            AvailabilitySet availabilitySet)
        {
            // Create the network interface
            Write("Creating a network interface for the VM {0}", vmName);            
            var vnetNicCreateParams = new NetworkInterface {
                Location = location,
                IpConfigurations = new List<NetworkInterfaceIPConfiguration>{ 
                    new NetworkInterfaceIPConfiguration { 
                        Name = "sample-dotnet-nic-" + vmName,
                        PrivateIPAllocationMethod = IPAllocationMethod.Dynamic,
                        Subnet = subnet,
                        LoadBalancerBackendAddressPools = new List<BackendAddressPool>{ backendAddressPool },
                        LoadBalancerInboundNatRules = new List<InboundNatRule>{ inboundNatRule }
                    } 
                }
            };
            var nic = networkClient.NetworkInterfaces.CreateOrUpdate(resourceGroupName, "sample-dotnet-nic-" + vmName, vnetNicCreateParams);

            Write("Creating a Ubuntu 14.04.3 Standard DS1 V2 virtual machine w/ a public IP");
            // Create the virtual machine
            var vmCreateParams = new VirtualMachine{
                Location = location,
                OsProfile = new OSProfile {
                    ComputerName = vmName,
                    AdminUsername = "notAdmin",
                    AdminPassword = "Pa$$w0rd92"
                },
                HardwareProfile = new HardwareProfile{ VmSize = VirtualMachineSizeTypes.StandardDS1V2 },
                StorageProfile = new StorageProfile{
                    ImageReference = new ImageReference {
                        Publisher = "Canonical",
                        Offer = "UbuntuServer",
                        Sku = "14.04.3-LTS",
                        Version = "latest"
                    },
                    OsDisk = new OSDisk {
                        Name = "sample-os-disk-" + vmName,
                        Caching = CachingTypes.None,
                        CreateOption = DiskCreateOptionTypes.FromImage,
                        Vhd = new VirtualHardDisk{
                            Uri = String.Format("https://{0}.blob.core.windows.net/dotnetcontainer/{1}.vhd", storageAccount.Name, vmName)
                        }
                    }
                },
                NetworkProfile = new NetworkProfile {
                    NetworkInterfaces = new List<NetworkInterfaceReference>{ 
                        new NetworkInterfaceReference {
                            Id = nic.Id,
                            Primary = true
                        }
                    }
                },
                AvailabilitySet = new Microsoft.Azure.Management.Compute.Models.SubResource{
                    Id = availabilitySet.Id
                }
            };
            var sshPubLocation = Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".ssh", "id_rsa.pub");
            if(File.Exists(sshPubLocation)){
                Write("Found SSH public key in {0}. Disabling password and enabling SSH Authentication.", sshPubLocation);
                var pubKey = File.ReadAllText(sshPubLocation);
                Write("Using public key: {0}", pubKey);
                vmCreateParams.OsProfile.LinuxConfiguration = new LinuxConfiguration {
                    DisablePasswordAuthentication = true,
                    Ssh = new SshConfiguration{ 
                        PublicKeys = new List<SshPublicKey>{ 
                            new SshPublicKey{ 
                                KeyData = pubKey, 
                                Path = "/home/notAdmin/.ssh/authorized_keys" 
                            } 
                        } 
                    }
                };
            }
            var vm = computeClient.VirtualMachines.CreateOrUpdate(resourceGroupName, "sample-dotnet-vm-" + vmName, vmCreateParams);
            Write("Your Linux Virtual Machine is built.");
            return vm;
        }

        private static void ExportResourceGroupTemplate(ResourceManagementClient resourceClient, string resourceGroupName)
        {
            Write("Exporting the resource group template for {0}", resourceGroupName);
            Write(Environment.NewLine);
            var exportResult = resourceClient.ResourceGroups.ExportTemplate(
                resourceGroupName, 
                new ExportTemplateRequest{ 
                    Resources = new List<string>{"*"}
                });
            Write("{0}", exportResult.Template);
            Write(Environment.NewLine);
        }

        private static string ResourceId(string resourceGroupName, string lbName, string subType, string subTypeName)
        {
            return string.Format(
                "/subscriptions/{0}/resourceGroups/{1}/providers/Microsoft.Network/loadBalancers/{2}/{3}/{4}", 
                Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID"),
                resourceGroupName,
                lbName,
                subType,
                subTypeName
                );
        }

        private static void Write(string format, params object[] items) 
        {
            Console.WriteLine(String.Format(format, items));
        }
    }
}
