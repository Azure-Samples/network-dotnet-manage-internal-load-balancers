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

                var loadBalancer3 = azure.LoadBalancers.Define(loadBalancerName3)
                        .WithRegion(Region.USEast)
                        .WithExistingResourceGroup(rgName)
                        // Add one rule that uses above backend and probe
                        .DefineLoadBalancingRule(TcpLoadBalancingRule)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(privateFrontEndName)
                            .FromFrontendPort(OracleSQLNodePort)
                            .ToBackend(backendPoolName3)
                            .WithProbe(HttpProbe)
                            .Attach()
                        // Add two nat pools to enable direct VM connectivity for
                        //  SSH to port 22 and TELNET to port 23
                        .DefineInboundNatRule(NatRule6000to22forVM3)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(privateFrontEndName)
                            .FromFrontendPort(6000)
                            .ToBackendPort(22)
                            .Attach()
                        .DefineInboundNatRule(NatRule6001to23forVM3)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(privateFrontEndName)
                            .FromFrontendPort(6001)
                            .ToBackendPort(23)
                            .Attach()
                        .DefineInboundNatRule(NatRule6002to22forVM4)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(privateFrontEndName)
                            .FromFrontendPort(6002)
                            .ToBackendPort(22)
                            .Attach()
                        .DefineInboundNatRule(NatRule6003to23forVM4)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(privateFrontEndName)
                            .FromFrontendPort(6003)
                            .ToBackendPort(23)
                            .Attach()
                        // Explicitly define the frontend
                        .DefinePrivateFrontend(privateFrontEndName)
                            .WithExistingSubnet(network, "Back-end")
                            .WithPrivateIPAddressStatic("172.16.3.5")
                            .Attach()
                        // Add one probes - one per rule
                        .DefineHttpProbe("httpProbe")
                            .WithRequestPath("/")
                            .Attach()
                        .Create();

                // Print load balancer details
                Utilities.Log("Created an internal load balancer");
                Utilities.PrintLoadBalancer(loadBalancer3);

                //=============================================================
                // Create two network interfaces in the backend subnet
                //  associate network interfaces to NAT rules, backend pools

                Utilities.Log("Creating two network interfaces in the backend subnet ...");
                Utilities.Log("- And associating network interfaces to backend pools and NAT rules");

                var networkInterfaceCreatables2 = new List<ICreatable<INetworkInterface>>();

                ICreatable<INetworkInterface> networkInterface3Creatable;
                ICreatable<INetworkInterface> networkInterface4Creatable;

                networkInterface3Creatable = azure.NetworkInterfaces.Define(networkInterfaceName3)
                        .WithRegion(Region.USEast)
                        .WithNewResourceGroup(rgName)
                        .WithExistingPrimaryNetwork(network)
                        .WithSubnet("Back-end")
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithExistingLoadBalancerBackend(loadBalancer3, backendPoolName3)
                        .WithExistingLoadBalancerInboundNatRule(loadBalancer3, NatRule6000to22forVM3)
                        .WithExistingLoadBalancerInboundNatRule(loadBalancer3, NatRule6001to23forVM3);

                networkInterfaceCreatables2.Add(networkInterface3Creatable);

                networkInterface4Creatable = azure.NetworkInterfaces.Define(networkInterfaceName4)
                        .WithRegion(Region.USEast)
                        .WithNewResourceGroup(rgName)
                        .WithExistingPrimaryNetwork(network)
                        .WithSubnet("Back-end")
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithExistingLoadBalancerBackend(loadBalancer3, backendPoolName3)
                        .WithExistingLoadBalancerInboundNatRule(loadBalancer3, NatRule6002to22forVM4)
                        .WithExistingLoadBalancerInboundNatRule(loadBalancer3, NatRule6003to23forVM4);

                networkInterfaceCreatables2.Add(networkInterface4Creatable);

                var networkInterfaces2 = azure.NetworkInterfaces.Create(networkInterfaceCreatables2.ToArray());

                // Print network interface details
                Utilities.Log("Created two network interfaces");
                Utilities.Log("Network Interface THREE -");
                Utilities.PrintNetworkInterface(networkInterfaces2.ElementAt(0));
                Utilities.Log();
                Utilities.Log("Network Interface FOUR -");
                Utilities.PrintNetworkInterface(networkInterfaces2.ElementAt(1));

                //=============================================================
                // Create an availability set

                Utilities.Log("Creating an availability set ...");

                var availSet2 = azure.AvailabilitySets.Define(availSetName)
                        .WithRegion(Region.USEast)
                        .WithNewResourceGroup(rgName)
                        .WithFaultDomainCount(2)
                        .WithUpdateDomainCount(4)
                        .Create();

                Utilities.Log("Created first availability set: " + availSet2.Id);
                Utilities.PrintAvailabilitySet(availSet2);

                //=============================================================
                // Create two virtual machines and assign network interfaces

                Utilities.Log("Creating two virtual machines in the frontend subnet ...");
                Utilities.Log("- And assigning network interfaces");

                var virtualMachineCreatables2 = new List<ICreatable<IVirtualMachine>>();
                ICreatable<IVirtualMachine> virtualMachine3Creatable;
                ICreatable<IVirtualMachine> virtualMachine4Creatable;

                virtualMachine3Creatable = azure.VirtualMachines.Define(vmName3)
                        .WithRegion(Region.USEast)
                        .WithExistingResourceGroup(rgName)
                        .WithExistingPrimaryNetworkInterface(networkInterfaces2.ElementAt(0))
                        .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer16_04_Lts)
                        .WithRootUsername(UserName)
                        .WithSsh(SshKey)
                        .WithSize(VirtualMachineSizeTypes.Parse("Standard_D2a_v4"))
                        .WithExistingAvailabilitySet(availSet2);

                virtualMachineCreatables2.Add(virtualMachine3Creatable);

                virtualMachine4Creatable = azure.VirtualMachines.Define(vmName4)
                        .WithRegion(Region.USEast)
                        .WithExistingResourceGroup(rgName)
                        .WithExistingPrimaryNetworkInterface(networkInterfaces2.ElementAt(1))
                        .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer16_04_Lts)
                        .WithRootUsername(UserName)
                        .WithSsh(SshKey)
                        .WithSize(VirtualMachineSizeTypes.Parse("Standard_D2a_v4"))
                        .WithExistingAvailabilitySet(availSet2);

                virtualMachineCreatables2.Add(virtualMachine4Creatable);

                var t1 = DateTime.UtcNow;
                var virtualMachines = azure.VirtualMachines.Create(virtualMachineCreatables2.ToArray());

                var t2 = DateTime.UtcNow;
                Utilities.Log($"Created 2 Linux VMs: (took {(t2 - t1).TotalSeconds} seconds)");
                Utilities.Log();

                // Print virtual machine details
                Utilities.Log("Virtual Machine THREE -");
                Utilities.PrintVirtualMachine(virtualMachines.ElementAt(0));
                Utilities.Log();
                Utilities.Log("Virtual Machine FOUR - ");
                Utilities.PrintVirtualMachine(virtualMachines.ElementAt(1));

                //=============================================================
                // Update a load balancer
                //  configure TCP idle timeout to 15 minutes

                Utilities.Log("Updating the load balancer ...");

                loadBalancer3.Update()
                        .UpdateLoadBalancingRule(TcpLoadBalancingRule)
                            .WithIdleTimeoutInMinutes(15)
                            .Parent()
                            .Apply();

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

                var loadBalancer4 = azure.LoadBalancers.Define(loadBalancerName4)
                        .WithRegion(Region.USEast)
                        .WithExistingResourceGroup(rgName)

                        // Add one rule that uses above backend and probe
                        .DefineLoadBalancingRule(TcpLoadBalancingRule)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(privateFrontEndName)
                            .FromFrontendPort(OracleSQLNodePort)
                            .ToBackend(backendPoolName3)
                            .WithProbe(HttpProbe)
                            .Attach()

                        // Add two nat pools to enable direct VM connectivity for
                        //  SSH to port 22 and TELNET to port 23
                        .DefineInboundNatRule(NatRule6000to22forVM3)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(privateFrontEndName)
                            .FromFrontendPort(6000)
                            .ToBackendPort(22)
                            .Attach()
                        .DefineInboundNatRule(NatRule6001to23forVM3)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(privateFrontEndName)
                            .FromFrontendPort(6001)
                            .ToBackendPort(23)
                            .Attach()
                        .DefineInboundNatRule(NatRule6002to22forVM4)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(privateFrontEndName)
                            .FromFrontendPort(6002)
                            .ToBackendPort(22)
                            .Attach()
                        .DefineInboundNatRule(NatRule6003to23forVM4)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(privateFrontEndName)
                            .FromFrontendPort(6003)
                            .ToBackendPort(23)
                            .Attach()

                        // Explicitly define the frontend
                        .DefinePrivateFrontend(privateFrontEndName)
                            .WithExistingSubnet(network, "Back-end")
                            .WithPrivateIPAddressStatic("172.16.3.15")
                            .Attach()

                        // Add one probes - one per rule
                        .DefineHttpProbe("httpProbe")
                            .WithRequestPath("/")
                            .Attach()
                        .Create();

                // Print load balancer details
                Utilities.Log("Created an internal load balancer");
                Utilities.PrintLoadBalancer(loadBalancer4);

                //=============================================================
                // List load balancers

                var loadBalancers = azure.LoadBalancers.List();

                Utilities.Log("Walking through the list of load balancers");

                foreach (var loadBalancer in loadBalancers)
                {
                    Utilities.PrintLoadBalancer(loadBalancer);
                }

                //=============================================================
                // Remove a load balancer

                Utilities.Log("Deleting load balancer " + loadBalancerName4
                        + "(" + loadBalancer4.Id + ")");
                azure.LoadBalancers.DeleteById(loadBalancer4.Id);
                Utilities.Log("Deleted load balancer" + loadBalancerName4);
            }
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
            var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
            var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
            var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
            ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            ArmClient client = new ArmClient(credential, subscription);

            await RunSample(client);

            try
            {
                //=================================================================
                // Authenticate
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}