# Deploy Open Source FHIR Bulk Loader using Azure CLI

If you don't have an Azure subscription, create a [free account](https://azure.microsoft.com/free/?WT.mc_id=A261C142F) before you begin.

## Use Azure Cloud Shell

Azure Cloud Shell is an interactive, authenticated, browser-accessible shell for managing Azure resources. It provides the flexibility of choosing the shell experience that best suits the way you work, either Bash or PowerShell.  [Read more](https://docs.microsoft.com/en-us/azure/cloud-shell/overview).  

Note:  As Cloud Shell machines are temporary, your files are persisted in two ways: through a disk image, and through a mounted file share named clouddrive. __On first launch, Cloud Shell prompts to create a resource group, storage account, and Azure Files share on your behalf.__ This is a one-time step and will be automatically attached for all sessions. A single file share can be mapped and will be used by both Bash and PowerShell in Cloud Shell.


You can access the Cloud Shell in two ways:

Direct link: Open a browser to https://shell.azure.com.

Azure portal: Select the Cloud Shell icon on the Azure portal:

![cloud shell](docs/images/portal-launch-icon.png)


To start Azure Cloud Shell:

1. Go to [https://shell.azure.com](https://shell.azure.com), or select the **Launch Cloud Shell** button to open Cloud Shell in your browser. 
1. Select the **Cloud Shell** button on the menu bar at the upper right in the [Azure portal](https://portal.azure.com). Choose to run in Bash mode.

To run the code in this deployment guide in Azure Cloud Shell:

1. Start Cloud Shell.
1. Select the **Copy** button on a code block to copy the code.
1. Paste the code into the Cloud Shell session by selecting **Ctrl**+**Shift**+**V** on Windows and Linux or by selecting **Cmd**+**Shift**+**V** on macOS.
1. Press **Enter** to run the code.


1. [Open Azure Cloud Shell](https://shell.azure.com) you can also access this from [Azure Portal](https://portal.azure.com)
2. Select Bash Shell for the environment 
3. Clone this repo ```git clone https://github.com/microsoft/fhir-loader```
4. Execute ```deploybulkloader.bash``` for direct FHIR Server access or ```deploybulkloader.bash -y``` to use FHIR Proxy access

Detailed instructions can be found [here](docs/deployment.md)


# Quickstart: Deploy Open Source FHIR server using Azure CLI

In this quickstart, you'll learn how to deploy an Open Source FHIR server in Azure using the Azure CLI.

If you don't have an Azure subscription, create a [free account](https://azure.microsoft.com/free/?WT.mc_id=A261C142F) before you begin.

## Use Azure Cloud Shell

Azure hosts Azure Cloud Shell, an interactive shell environment that you can use through your browser. You can use either Bash or PowerShell with Cloud Shell to work with Azure services. You can use the Cloud Shell preinstalled commands to run the code in this article without having to install anything on your local environment.

To start Azure Cloud Shell:
1. Select **Try It** in the upper-right corner of a code block. Selecting **Try It** doesn't automatically copy the code to Cloud Shell. 
1. Go to [https://shell.azure.com](https://shell.azure.com), or select the **Launch Cloud Shell** button to open Cloud Shell in your browser. 
1. Select the **Cloud Shell** button on the menu bar at the upper right in the [Azure portal](https://portal.azure.com). Choose to run in Bash mode.

To run the code in this article in Azure Cloud Shell:

1. Start Cloud Shell.
1. Select the **Copy** button on a code block to copy the code.
1. Paste the code into the Cloud Shell session by selecting **Ctrl**+**Shift**+**V** on Windows and Linux or by selecting **Cmd**+**Shift**+**V** on macOS.
1. Select **Enter** to run the code.

## Create resource group

Pick a name for the resource group that will contain the provisioned resources and create it:

```azurecli-interactive
servicename="myfhirservice"
az group create --name $servicename --location westus2
```

## Deploy template

The Microsoft FHIR Server for Azure [GitHub Repository](https://github.com/Microsoft/fhir-server) contains a template that will deploy all necessary resources.<br />

Deploy using CosmosDB as the data store with the following command:

```azurecli-interactive
az group deployment create -g $servicename --template-uri https://raw.githubusercontent.com/Microsoft/fhir-server/master/samples/templates/default-azuredeploy.json --parameters serviceName=$servicename
```

## Verify FHIR server is running

Obtain a capability statement from the FHIR server with:

```azurecli-interactive
metadataurl="https://${servicename}.azurewebsites.net/metadata"
curl --url $metadataurl
```

It will take a minute or so for the server to respond the first time.

## Clean up resources

If you're not going to continue to use this application, delete the resource group with the following steps:

```azurecli-interactive
az group delete --name $servicename
```

## Next steps

In this tutorial, you've deployed the Microsoft Open Source FHIR Server for Azure into your subscription. To learn how to access the FHIR API using Postman, you can take a look at the [Postman tutorial](https://docs.microsoft.com/en-us/azure/healthcare-apis/access-fhir-postman-tutorial) on the Azure Docs site.