// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Compute;
using System.Net.NetworkInformation;

namespace ManageInternalLoadBalancer
{
    public class Program
    {
        private static ResourceIdentifier? _resourceGroupId = null;
        private static readonly string HttpProbe = "httpProbe";
        private static readonly string TcpLoadBalancingRule = "tcpRule";
        private static readonly string NatRule6000to22forVM3 = "nat6000to22forVM3";
        private static readonly string NatRule6001to23forVM3 = "nat6001to23forVM3";
        private static readonly string NatRule6002to22forVM4 = "nat6002to22forVM4";
        private static readonly string NatRule6003to23forVM4 = "nat6003to23forVM4";
        private static readonly int OracleSQLNodePort = 1521;

        /**
         * Azure Network sample for managing internal load balancers -
         *
         * High-level ...
         *
         * - Create an internal load balancer that receives network traffic on
         *   port 1521 (Oracle SQL Node Port) and sends load-balanced traffic
         *   to two virtual machines
         *
         * - Create NAT rules for SSH and TELNET access to virtual
         *   machines behind the load balancer
         *
         * - Create a health probe
         *
         * Details ...
         *
         * Create an internal facing load balancer with ...
         * - A frontend private IP address
         * - One backend address pool which contains network interfaces for the virtual
         *   machines to receive 1521 (Oracle SQL Node Port) network traffic from the load balancer
         * - One load balancing rule fto map port 1521 on the load balancer to
         *   ports in the backend address pool
         * - One probe which contains HTTP health probe used to check availability
         *   of virtual machines in the backend address pool
         * - Two inbound NAT rules which contain rules that map a public port on the load
         *   balancer to a port for a specific virtual machine in the backend address pool
         *   - this provides direct VM connectivity for SSH to port 22 and TELNET to port 23
         *
         * Create two network interfaces in the backend subnet ...
         * - And associate network interfaces to backend pools and NAT rules
         *
         * Create two virtual machines in the backend subnet ...
         * - And assign network interfaces
         *
         * Update an existing load balancer, configure TCP idle timeout
         * Create another load balancer
         * List load balancers
         * Remove an existing load balancer.
         */
        public static async Task RunSample(ArmClient client)
        {
            string rgName = Utilities.CreateRandomName("NetworkSampleRG");
            string vnetName = Utilities.CreateRandomName("vnet");
            string loadBalancerName3 = Utilities.CreateRandomName("balancer3-");
            string loadBalancerName4 = Utilities.CreateRandomName("balancer4-");
            string networkInterfaceName3 = Utilities.CreateRandomName("nic3");
            string networkInterfaceName4 = Utilities.CreateRandomName("nic4");
            string availSetName = Utilities.CreateRandomName("av2");
            string vmName3 = Utilities.CreateRandomName("lVM3");
            string vmName4 = Utilities.CreateRandomName("lVM4");
            string privateFrontEndName = loadBalancerName3 + "-BE";
            string backendPoolName3 = loadBalancerName3 + "-BAP3";

            try
            {
                // Get default subscription
                SubscriptionResource subscription = await client.GetDefaultSubscriptionAsync();

                // Create a resource group in the EastUS region
                Utilities.Log($"Creating resource group...");
                ArmOperation<ResourceGroupResource> rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.WestUS));
                ResourceGroupResource resourceGroup = rgLro.Value;
                _resourceGroupId = resourceGroup.Id;
                Utilities.Log("Created a resource group with name: " + resourceGroup.Data.Name);

                //=============================================================
                // Create a virtual network with a frontend and a backend subnets
                Utilities.Log("Creating virtual network with a frontend and a backend subnets...");

                VirtualNetworkData vnetInput = new VirtualNetworkData()
                {
                    Location = resourceGroup.Data.Location,
                    AddressPrefixes = { "172.16.0.0/16" },
                    Subnets =
                    {
                        new SubnetData() { Name = "Front-end", AddressPrefix = "172.16.1.0/24"},
                        new SubnetData() { Name = "Back-end", AddressPrefix = "172.16.3.0/24"},
                    },
                };
                var vnetLro = await resourceGroup.GetVirtualNetworks().CreateOrUpdateAsync(WaitUntil.Completed, vnetName, vnetInput);
                VirtualNetworkResource vnet = vnetLro.Value;

                Utilities.Log($"Created a virtual network: {vnet.Data.Name}");

                //=============================================================
                // Create an internal load balancer
                // Create a frontend IP address
                // Two backend address pools which contain network interfaces for the virtual
                //  machines to receive HTTP and HTTPS network traffic from the load balancer
                // Two load balancing rules for HTTP and HTTPS to map public ports on the load
                //  balancer to ports in the backend address pool
                // Two probes which contain HTTP and HTTPS health probes used to check availability
                //  of virtual machines in the backend address pool
                // Two inbound NAT rules which contain rules that map a public port on the load
                //  balancer to a port for a specific virtual machine in the backend address pool
                //  - this provides direct VM connectivity for SSH to port 22 and TELNET to port 23

                Utilities.Log("Creating an internal facing load balancer with ...");
                Utilities.Log("- A private IP address");
                Utilities.Log("- One backend address pool which contain network interfaces for the virtual\n"
                        + "  machines to receive 1521 network traffic from the load balancer");
                Utilities.Log("- One load balancing rules for 1521 to map public ports on the load\n"
                        + "  balancer to ports in the backend address pool");
                Utilities.Log("- One probe which contains HTTP health probe used to check availability\n"
                        + "  of virtual machines in the backend address pool");
                Utilities.Log("- Two inbound NAT rules which contain rules that map a port on the load\n"
                        + "  balancer to a port for a specific virtual machine in the backend address pool\n"
                        + "  - this provides direct VM connectivity for SSH to port 22 and TELNET to port 23");

                var frontendIPConfigurationId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName3}/frontendIPConfigurations/{privateFrontEndName}");
                var backendAddressPoolId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName3}/backendAddressPools/{backendPoolName3}");
                LoadBalancerData loadBalancerInput = new LoadBalancerData()
                {
                    Location = resourceGroup.Data.Location,
                    Sku = new LoadBalancerSku()
                    {
                        Name = LoadBalancerSkuName.Standard,
                        Tier = LoadBalancerSkuTier.Regional,
                    },
                    // Explicitly define the frontend
                    FrontendIPConfigurations =
                    {
                        new FrontendIPConfigurationData()
                        {
                            Name = privateFrontEndName,
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            Subnet = new SubnetData(){ Id = vnet.Data.Subnets.First(item => item.Name == "Back-end").Id},
                            PrivateIPAddress = "172.16.3.5"
                        }
                    },
                    BackendAddressPools =
                    {
                        new BackendAddressPoolData()
                        {
                            Name = backendPoolName3
                        }
                    },
                    // Add one rule that uses above backend and probe
                    LoadBalancingRules =
                    {
                        new LoadBalancingRuleData()
                        {
                            Name = TcpLoadBalancingRule,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            BackendAddressPoolId = backendAddressPoolId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = OracleSQLNodePort,
                            BackendPort = OracleSQLNodePort,
                            EnableFloatingIP = false,
                            IdleTimeoutInMinutes = 15,
                            ProbeId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName3}/probes/{HttpProbe}"),
                        }
                    },
                    // Add one probes - one per rule
                    Probes =
                    {
                        new ProbeData()
                        {
                            Name = HttpProbe,
                            Protocol = ProbeProtocol.Http,
                            Port = 80,
                            IntervalInSeconds = 10,
                            NumberOfProbes = 2,
                            RequestPath = "/",
                        }
                    },
                    // Add two nat pools to enable direct VM connectivity for
                    //  SSH to port 22 and TELNET to port 23
                    InboundNatRules =
                    {
                        new InboundNatRuleData()
                        {
                            Name = NatRule6000to22forVM3,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 6000,
                            BackendPort = 22,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        },
                        new InboundNatRuleData()
                        {
                            Name = NatRule6001to23forVM3,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 6001,
                            BackendPort = 23,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        },
                        new InboundNatRuleData()
                        {
                            Name = NatRule6002to22forVM4,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 6002,
                            BackendPort = 22,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        },
                        new InboundNatRuleData()
                        {
                            Name = NatRule6003to23forVM4,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 6003,
                            BackendPort = 23,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        }
                    },
                };
                var loadBalancerLro3 = await resourceGroup.GetLoadBalancers().CreateOrUpdateAsync(WaitUntil.Completed, loadBalancerName3, loadBalancerInput);
                LoadBalancerResource loadBalancer3 = loadBalancerLro3.Value;

                Utilities.Log($"Created a load balancer: {loadBalancer3.Data.Name}");

                //=============================================================
                // Create two network interfaces in the backend subnet
                //  associate network interfaces to NAT rules, backend pools

                Utilities.Log("Creating two network interfaces in the backend subnet ...");
                Utilities.Log("- And associating network interfaces to backend pools and NAT rules");

                var nicInput3 = new NetworkInterfaceData()
                {
                    Location = resourceGroup.Data.Location,
                    IPConfigurations =
                    {
                        new NetworkInterfaceIPConfigurationData()
                        {
                            Name = "default-config",
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            Subnet = new SubnetData()
                            {
                                Id = vnet.Data.Subnets.First(item=>item.Name=="Back-end").Id
                            },
                            LoadBalancerBackendAddressPools =
                            {
                                new BackendAddressPoolData(){ Id = backendAddressPoolId },
                            },
                            LoadBalancerInboundNatRules =
                            {
                                new InboundNatRuleData(){ Id = loadBalancer3.Data.InboundNatRules.First(item => item.Name == NatRule6000to22forVM3).Id },
                                new InboundNatRuleData(){ Id = loadBalancer3.Data.InboundNatRules.First(item => item.Name == NatRule6001to23forVM3).Id },
                            }
                        }
                    }
                };
                var networkInterfaceLro3 = await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, networkInterfaceName3, nicInput3);
                NetworkInterfaceResource networkInterface3 = networkInterfaceLro3.Value;
                Utilities.Log($"Created network interface: {networkInterface3.Data.Name}");

                var nicInput4 = new NetworkInterfaceData()
                {
                    Location = resourceGroup.Data.Location,
                    IPConfigurations =
                    {
                        new NetworkInterfaceIPConfigurationData()
                        {
                            Name = "default-config",
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            Subnet = new SubnetData()
                            {
                                Id = vnet.Data.Subnets.First(item=>item.Name=="Back-end").Id
                            },
                            LoadBalancerBackendAddressPools =
                            {
                                new BackendAddressPoolData(){ Id = backendAddressPoolId },
                            },
                            LoadBalancerInboundNatRules =
                            {
                                new InboundNatRuleData(){ Id = loadBalancer3.Data.InboundNatRules.First(item => item.Name == NatRule6002to22forVM4).Id },
                                new InboundNatRuleData(){ Id = loadBalancer3.Data.InboundNatRules.First(item => item.Name == NatRule6003to23forVM4).Id },
                            }
                        }
                    }
                };
                var networkInterfaceLro4 = await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, networkInterfaceName4, nicInput4);
                NetworkInterfaceResource networkInterface4 = networkInterfaceLro4.Value;
                Utilities.Log($"Created network interface: {networkInterface4.Data.Name}");

                //=============================================================
                // Create an availability set

                Utilities.Log("Creating an availability set ...");

                AvailabilitySetData availabilitySetInput = new AvailabilitySetData(resourceGroup.Data.Location)
                {
                    PlatformFaultDomainCount = 2,
                    PlatformUpdateDomainCount = 4,
                };
                var availabilitySetLro = await resourceGroup.GetAvailabilitySets().CreateOrUpdateAsync(WaitUntil.Completed, availSetName, availabilitySetInput);
                AvailabilitySetResource availabilitySet = availabilitySetLro.Value;
                Utilities.Log($"Created first availability set: {availabilitySet.Data.Name}");

                //=============================================================
                // Create two virtual machines and assign network interfaces

                Utilities.Log("Creating two virtual machines in the frontend subnet ...");
                Utilities.Log("- And assigning network interfaces");

                // Create vm3
                Utilities.Log("Creating a new virtual machine...");
                VirtualMachineData vmInput3 = Utilities.GetDefaultVMInputData(resourceGroup, vmName3);
                vmInput3.NetworkProfile.NetworkInterfaces.Add(new VirtualMachineNetworkInterfaceReference() { Id = networkInterface3.Id, Primary = true });
                var vmLro3 = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, vmName3, vmInput3);
                VirtualMachineResource vm3 = vmLro3.Value;
                Utilities.Log($"Created virtual machine: {vm3.Data.Name}");

                // Create vm4
                Utilities.Log("Creating a new virtual machine...");
                VirtualMachineData vmInput4 = Utilities.GetDefaultVMInputData(resourceGroup, vmName4);
                vmInput4.NetworkProfile.NetworkInterfaces.Add(new VirtualMachineNetworkInterfaceReference() { Id = networkInterface4.Id, Primary = true });
                var vmLro4 = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, vmName4, vmInput4);
                VirtualMachineResource vm4 = vmLro4.Value;
                Utilities.Log($"Created virtual machine: {vm4.Data.Name}");

                //=============================================================
                // Update a load balancer
                //  configure TCP idle timeout to 15 minutes

                Utilities.Log("Updating the load balancer ...");

                LoadBalancerData updateLoadBalancerInput = loadBalancer3.Data;
                updateLoadBalancerInput.LoadBalancingRules.First(item => item.Name == TcpLoadBalancingRule).IdleTimeoutInMinutes = 15;
                loadBalancerLro3 = await resourceGroup.GetLoadBalancers().CreateOrUpdateAsync(WaitUntil.Completed, loadBalancerName3, loadBalancerInput);
                loadBalancer3 = loadBalancerLro3.Value;

                Utilities.Log("Update the load balancer with a TCP idle timeout to 15 minutes");

                //=============================================================
                // Create another internal load balancer
                // Create a frontend IP address
                // Two backend address pools which contain network interfaces for the virtual
                //  machines to receive HTTP and HTTPS network traffic from the load balancer
                // Two load balancing rules for HTTP and HTTPS to map public ports on the load
                //  balancer to ports in the backend address pool
                // Two probes which contain HTTP and HTTPS health probes used to check availability
                //  of virtual machines in the backend address pool
                // Two inbound NAT rules which contain rules that map a public port on the load
                //  balancer to a port for a specific virtual machine in the backend address pool
                //  - this provides direct VM connectivity for SSH to port 22 and TELNET to port 23

                Utilities.Log("Creating another internal facing load balancer with ...");
                Utilities.Log("- A private IP address");
                Utilities.Log("- One backend address pool which contain network interfaces for the virtual\n"
                        + "  machines to receive 1521 network traffic from the load balancer");
                Utilities.Log("- One load balancing rules for 1521 to map public ports on the load\n"
                        + "  balancer to ports in the backend address pool");
                Utilities.Log("- One probe which contains HTTP health probe used to check availability\n"
                        + "  of virtual machines in the backend address pool");
                Utilities.Log("- Two inbound NAT rules which contain rules that map a port on the load\n"
                        + "  balancer to a port for a specific virtual machine in the backend address pool\n"
                        + "  - this provides direct VM connectivity for SSH to port 22 and TELNET to port 23");

                frontendIPConfigurationId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName4}/frontendIPConfigurations/{privateFrontEndName}");
                backendAddressPoolId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName4}/backendAddressPools/{backendPoolName3}");
                loadBalancerInput = new LoadBalancerData()
                {
                    Location = resourceGroup.Data.Location,
                    Sku = new LoadBalancerSku()
                    {
                        Name = LoadBalancerSkuName.Standard,
                        Tier = LoadBalancerSkuTier.Regional,
                    },
                    // Explicitly define the frontend
                    FrontendIPConfigurations =
                    {
                        new FrontendIPConfigurationData()
                        {
                            Name = privateFrontEndName,
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            Subnet = new SubnetData(){ Id = vnet.Data.Subnets.First(item => item.Name == "Back-end").Id },
                            PrivateIPAddress = "172.16.3.15"
                        }
                    },
                    BackendAddressPools =
                    {
                        new BackendAddressPoolData()
                        {
                            Name = backendPoolName3
                        }
                    },
                    // Add one rule that uses above backend and probe
                    LoadBalancingRules =
                    {

                        new LoadBalancingRuleData()
                        {
                            Name = TcpLoadBalancingRule,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            BackendAddressPoolId = backendAddressPoolId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = OracleSQLNodePort,
                            BackendPort = OracleSQLNodePort,
                            EnableFloatingIP = false,
                            IdleTimeoutInMinutes = 15,
                            ProbeId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName4}/probes/{HttpProbe}"),
                        }
                    },
                    // Add one probes - one per rule
                    Probes =
                    {
                        new ProbeData()
                        {
                            Name = HttpProbe,
                            Protocol = ProbeProtocol.Http,
                            Port = 80,
                            IntervalInSeconds = 10,
                            NumberOfProbes = 4,
                            RequestPath = "/",
                        }
                    },
                    // Add two nat pools to enable direct VM connectivity for
                    //  SSH to port 22 and TELNET to port 23
                    InboundNatRules =
                    {
                        new InboundNatRuleData()
                        {
                            Name = NatRule6000to22forVM3,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 6000,
                            BackendPort = 22,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        },
                        new InboundNatRuleData()
                        {
                            Name = NatRule6001to23forVM3,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 6001,
                            BackendPort = 23,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        },
                        new InboundNatRuleData()
                        {
                            Name = NatRule6002to22forVM4,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 6002,
                            BackendPort = 22,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        },
                        new InboundNatRuleData()
                        {
                            Name = NatRule6003to23forVM4,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 6003,
                            BackendPort = 23,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        }
                    },
                };
                var loadBalancerLro4 = await resourceGroup.GetLoadBalancers().CreateOrUpdateAsync(WaitUntil.Completed, loadBalancerName4, loadBalancerInput);
                LoadBalancerResource loadBalancer4 = loadBalancerLro4.Value;

                Utilities.Log($"Created another balancer: {loadBalancer4.Data.Name}");

                //=============================================================
                // List load balancers

                Utilities.Log("Walking through the list of load balancers");

                await foreach (var loadBalancer in resourceGroup.GetLoadBalancers().GetAllAsync())
                {
                    Utilities.Log(loadBalancer.Data.Name);
                }

                //=============================================================
                // Remove a load balancer

                Utilities.Log("Deleting load balancer...");
                await loadBalancer4.DeleteAsync(WaitUntil.Completed);
                Utilities.Log("Deleted load balancer" + loadBalancerName4);
            }
            finally
            {
                try
                {
                    if (_resourceGroupId is not null)
                    {
                        Utilities.Log($"Deleting Resource Group...");
                        await client.GetResourceGroupResource(_resourceGroupId).DeleteAsync(WaitUntil.Completed);
                        Utilities.Log($"Deleted Resource Group: {_resourceGroupId.Name}");
                    }
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resources in Azure. No clean up is necessary");
                }
                catch (Exception ex)
                {
                    Utilities.Log(ex);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                //=================================================================
                // Authenticate

                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                await RunSample(client);
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}