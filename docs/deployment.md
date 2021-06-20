# Deploy Open Source FHIR Bulk Loader using Azure CLI

If you don't have an Azure subscription, create a [free account](https://azure.microsoft.com/free/?WT.mc_id=A261C142F) before you begin.

## Use Azure Cloud Shell

Azure Cloud Shell is an interactive, authenticated, browser-accessible shell for managing Azure resources. It provides the flexibility of choosing the shell experience that best suits the way you work, either Bash or PowerShell.  [Read more](https://docs.microsoft.com/en-us/azure/cloud-shell/overview).  

Note:  As Cloud Shell machines are temporary, your files are persisted in two ways: through a disk image, and through a mounted file share named clouddrive. __On first launch, Cloud Shell prompts to create a resource group, storage account, and Azure Files share on your behalf.__ This is a one-time step and will be automatically attached for all sessions. A single file share can be mapped and will be used by both Bash and PowerShell in Cloud Shell.


You can access the Cloud Shell in two ways:

Direct link: Open a browser to https://shell.azure.com.

Azure portal: Select the Cloud Shell icon on the Azure portal:

![cloud shell](images/portal-launch-icon.png)


To run the code in this deployment guide do the following:

1. Start Cloud Shell.
1. Select __Bash__ envionment 
1. Select the **Copy** button on a code block to copy the code.
1. Paste the code into the Cloud Shell session by selecting **Ctrl**+**Shift**+**V** on Windows and Linux or by selecting **Cmd**+**Shift**+**V** on macOS.
1. Press **Enter** to run the code.


## Deploy Bulk Loader Script (bash)

1. Clone this repo to your Cloudshell environment 

```azurecli-interactive
git clone https://github.com/microsoft/fhir-loader
```

2. Change working directory to fhir-loader/scripts 
```azurecli-interactive
cd ./fhir-loader/scripts 
```
3. Enable Execute permissions on the deploybulkloader.bash script 
```azurecli-interactive
chmod +x ./deploybulkloader.bash
```
4. Run the deploybulkloader.bash script (__without HealthArchitectures fhir-proxy__) 
```azurecli-interactive
./deploybulkloader.bash 
```
4. Run the deploybulkloader.bash script (__with HealthArchitectures fhir-proxy__) (_recommended production approach_)
```azurecli-interactive
./deploybulkloader.bash -y
```

## Information Input / Needs 



## Environment 

ResourceGroup,
KeyVault,
Storage Account,
App Service Plan,
Function App,
EventGrid

### Resource Group 

### Key Vault 

### Storage Containers 

### App Service Plan 
he free app service plan and Cosmos Db account have restrictions that can be seen on their respective doc pages: App Service plan overview, Cosmos DB free tier

https://docs.microsoft.com/en-us/azure/app-service/overview-hosting-plans 

### Function App

### EventGrid 


### Application Insights 


## Verify Bulk Loader is functioning 

Obtain a capability statement from the FHIR server with:

```azurecli-interactive
metadataurl="https://${servicename}.azurewebsites.net/metadata"
curl --url $metadataurl
```

It will take a minute or so for the server to respond the first time.

