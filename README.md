---
services: azure-storage
platforms: dotnet
author: devigned
---

# Getting Started with Azure Resource Manager for load balancers in .NET

This sample shows how to manage a load balancer using the Azure Resource Manager APIs for .NET.

You can use a load balancer to provide high availability for your workloads in Azure. An Azure load balancer is a Layer-4 (TCP, UDP) type load balancer that distributes incoming traffic among healthy service instances in cloud services or virtual machines defined in a load balancer set.

For a detailed overview of Azure load balancers, see [Azure Load Balancer overview](https://azure.microsoft.com/documentation/articles/load-balancer-overview/).

![alt tag](./lb.JPG)

This sample deploys an internet-facing load balancer. It then creates and deploys two Azure virtual machines behind the load balancer. For a detailed overview of internet-facing load balancers, see [Internet-facing load balancer overview](https://azure.microsoft.com/documentation/articles/load-balancer-internet-overview/).

To deploy an internet-facing load balancer, you'll need to create and configure the following objects.

- Front end IP configuration - contains public IP addresses for incoming network traffic. 
- Back end address pool - contains network interfaces (NICs) for the virtual machines to receive network traffic from the load balancer. 
- Load balancing rules - contains rules mapping a public port on the load balancer to port in the back end address pool.
- Inbound NAT rules - contains rules mapping a public port on the load balancer to a port for a specific virtual machine in the back end address pool.
- Probes - contains health probes used to check availability of virtual machines instances in the back end address pool.

You can get more information about load balancer components with Azure resource manager at [Azure Resource Manager support for Load Balancer](https://azure.microsoft.com/documentation/articles/load-balancer-arm/).

## Tasks performed in this sample

The sample performs the following tasks to create the load balancer and the load-balanced virtual machines: 

1. Create a resource group
2. Create a virtual network (vnet)
3. Create a subnet
4. Create a public IP
5. Build the load balancer payload
  1. Build a front-end IP pool
  2. Build a back-end address pool
  3. Build a health probe
  4. Build a load balancer rule
  5. Build inbound NAT rule 1
  6. Build inbound NAT rule 2
6. Create the load balancer with the above payload
7. Create an Availability Set
11. Create the first VM: Web1
	10. Find an Ubutnu VM image
	8. Create the network interface
12. Create the second VM: Web2
	10. Find an Ubutnu VM image
	8. Create the network interface
13. Delete the resource group and the resources created in the previous steps

## Run this sample

To run the sample, follow these steps:

1. If you don't already have a Microsoft Azure subscription, you can register for a [free trial account](http://go.microsoft.com/fwlink/?LinkId=330212).

2. Install [Visual Studio](https://www.visualstudio.com/downloads/download-visual-studio-vs.aspx) if you don't have it already. 

3. Install the [Azure SDK for .NET](https://azure.microsoft.com/downloads/) if you have not already done so. We recommend using the most recent version.

4. Clone the sample repository.

		https://github.com/Azure-Samples/storage-dotnet-resource-provider-getting-started.git

5. Create an Azure service principal using 
    [Azure CLI](https://azure.microsoft.com/documentation/articles/resource-group-authenticate-service-principal-cli/),
    [PowerShell](https://azure.microsoft.com/documentation/articles/resource-group-authenticate-service-principal/)
    or [Azure Portal](https://azure.microsoft.com/documentation/articles/resource-group-create-service-principal-portal/).

6. Open the sample solution in Visual Studio, and restore any packages if prompted.
7. In the sample source code, locate the constants for your subscription ID and resource group name, and specify values for them. 
	
		const string subscriptionId = "<subscriptionid>";         
	
	    //Specify a resource group name of your choice. Specifying a new value will create a new resource group.
	    const string rgName = "TestResourceGroup";        

8. Set the following environment variables using the information from the service principle that you created above.
    
	    AZURE_TENANT_ID={your tenant ID as a guid OR the domain name of your org <contosocorp.com>}	
	    CLIENT_ID={your client ID}
	    APPLICATION_SECRET={your client secret}
	    AZURE_SUBSCRIPION_ID={your subscription ID}

## More information

- [Azure SDK for .NET](https://github.com/tamram/azure-sdk-for-net/)
- [Azure Load Balancer overview](https://azure.microsoft.com/documentation/articles/load-balancer-overview/)

