---
services: Network
platforms: .Net
author: selvasingh
---

# Getting Started with Network - Manage Internal Load Balancer - in .Net #

          Azure Network sample for managing internal load balancers -
         
          High-level ...
         
          - Create an internal load balancer that receives network traffic on
            port 1521 (Oracle SQL Node Port) and sends load-balanced traffic
            to two virtual machines
         
          - Create NAT rules for SSH and TELNET access to virtual
            machines behind the load balancer
         
          - Create a health probe
         
          Details ...
         
          Create an internal facing load balancer with ...
          - A frontend private IP address
          - One backend address pool which contains network interfaces for the virtual
            machines to receive 1521 (Oracle SQL Node Port) network traffic from the load balancer
          - One load balancing rule fto map port 1521 on the load balancer to
            ports in the backend address pool
          - One probe which contains HTTP health probe used to check availability
            of virtual machines in the backend address pool
          - Two inbound NAT rules which contain rules that map a public port on the load
            balancer to a port for a specific virtual machine in the backend address pool
            - this provides direct VM connectivity for SSH to port 22 and TELNET to port 23
         
          Create two network interfaces in the backend subnet ...
          - And associate network interfaces to backend pools and NAT rules
         
          Create two virtual machines in the backend subnet ...
          - And assign network interfaces
         
          Update an existing load balancer, configure TCP idle timeout
          Create another load balancer
          List load balancers
          Remove an existing load balancer.


## Running this Sample ##

To run this sample:

Set the environment variable `AZURE_AUTH_LOCATION` with the full path for an auth file. See [how to create an auth file](https://github.com/Azure/azure-sdk-for-net/blob/Fluent/AUTH.md).

    git clone https://github.com/Azure-Samples/network-dotnet-manage-internal-load-balancers.git

    cd network-dotnet-manage-internal-load-balancers

    dotnet restore

    dotnet run

## More information ##

[Azure Management Libraries for C#](https://github.com/Azure/azure-sdk-for-net/tree/Fluent)
[Azure .Net Developer Center](https://azure.microsoft.com/en-us/develop/net/)
If you don't have a Microsoft Azure subscription you can get a FREE trial account [here](http://go.microsoft.com/fwlink/?LinkId=330212)

---

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.