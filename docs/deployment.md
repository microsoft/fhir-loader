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

## Deployment Considerations 
The FHIR Bulk Loader works with the HealthArchitecture [FHIR Proxy](https://github.com/microsoft/fhir-proxy), the [Azure API for FHIR](https://docs.microsoft.com/en-us/azure/healthcare-apis/fhir/fhir-paas-portal-quickstart) and the [Microsoft FHIR OSS Server](https://github.com/microsoft/fhir-server/).

When using the Bulk Loader with FHIR Proxy the bulk loader will use the Proxy Key Vault rather than deploying another key vault.   

## Deploy FHIR Bulk Loader Script (bash)

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
chmod +x ./deployFhirBulkLoader.bash
```
Alternatively you can run the script with command line attributes   
```azurecli-interactive
./deployFhirBulkLoader.bash --i <subscriptionId> -g <resourceGroupName> -l <resourceGroupLocation> -p <install prefix> -k <keyvault> -y <use FHIR Proxy=yes> 
```
4. Run the deploybulkloader.bash script 
```azurecli-interactive
./deploybulkloader.bash
```

Upon successful completion you will see the following information 
```azurecli
    **************************************************************************************
	"FHIR Loader has successfully been deployed to group "resourceGroupName" on "$(date)
	"Please note the following reference information for future use:"
	"Your FHIRLoader URL is: " host
	"Your FHIRLoader Storage Account name is: "storageAccountName
	 ***************************************************************************************

```
Should instllation fail at anytime the follow error will be shown 

```azurecli
FHIR-Loader deployment had errors. Consider deleting the resources and trying again...

```



