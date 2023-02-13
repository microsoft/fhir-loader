# FHIR-Bulk Loader Getting Started with Deploy Scripts
In this document, we go over the deploy scripts necessary for installing FHIR Bulk Loader. We cover the order of script execution and the steps to complete the install.

## Errata 
There are no open issues at this time. 

## Prerequisites 

These scripts will gather (and export) information necessary for the proper deployment and configuration of FHIR Bulk Loader. Credentials and other secure information will be stored in the existing Key Vault attached to your FHIR Service/FHIR Proxy deployment.

 - User must have FHIR Server (OSS)/Azure API for FHIR/Azure Healthcare APIs FHIR Service already deployed and set up with FHIR-Proxy.
 - User must have rights to deploy resources at the Azure Subscription scope (i.e., Contributor role or above).

__Note:__
FHIR Service and FHIR-Proxy use a Key Vault for securing Service Client credentials. Because the ```deployFhirBulk.bash``` script scans the Key Vault for FHIR Service and FHIR-Proxy values, only one Key Vault should be used in your Resource Group. If multiple Key Vaults have been deployed in your Resource Group, please use the [backup and restore](https://docs.microsoft.com/en-us/azure/key-vault/general/backup?tabs=azure-cli) option to copy values to one Key Vault.

__Note:__ 
The FHIR-Bulk Loader & Export scripts are designed and tested for the [Azure Cloud Shell - Bash Shell](https://docs.microsoft.com/en-us/azure/cloud-shell/features#:~:text=Azure%20Cloud%20Shell%20is%20a,and%20maintaining%20a%20machine%20yourself.) environment.


### Naming & Tagging
All Azure resource types have a scope in which resource names must be unique. Some resource names, such as PaaS services with public endpoints, have global scopes and therefore must be unique across the entire Azure platform. Our deployment scripts suggest naming standards that group logical connections while aligning with Azure best practices. Users are prompted to accept a default name or supply their own names during installation. See below for the FHIR Bulk Loader resource naming convention.

Resource Type    | Deploy App Name   | Number      | Resource Name Example (automatically generated)
------------|-----------------|-------------|------------------------------------------------
sfb-        | bulk            | random      | sfb-bulk123456

Azure Resources deployed with this script are automatically tagged with their origin and deployment name (see example below). Users are able to add/change [Tags](https://docs.microsoft.com/en-us/azure/azure-resource-manager/management/tag-resources?tabs=json) after installation.

Origin              |  Deployment       
--------------------|-----------------
HealthArchitectures | FHIR-Bulk   

---

## Getting Started
Please note you should deploy these components into a tenant and subscription where you have appropriate permissions to create and manage Application Registrations (ie Application Adminitrator RBAC Role or Global Administrator in AAD), and can deploy Resources at the Subscription Scope. 

Launch Azure Cloud Shell (Bash Environment)  

**CTRL+click** (Windows or Linux) or **CMD+click** (Mac) to open in a new window or tab   

[![Launch Azure Shell](/docs/images/launchcloudshell.png "Launch Cloud Shell")](https://shell.azure.com/bash?target="_blank")

Clone the repo to your Bash Shell (CLI) environment 
```azurecli-interactive
git clone https://github.com/microsoft/fhir-loader 
```
Change working directory to the repo Scripts directory
```azurecli-interactive
cd $HOME/fhir-loader/scripts
```

Make the Bash Shell Scripts used for Deployment and Setup executable 
```azurecli-interactive
chmod +x *.bash 
```

## Step 1.  deployFhirBulk.bash
This is the main component deployment script for the FHIR Bulk Loader Azure components and application code.  Note that retry logic is used to account for provisioning delays (e.g., networking provisioning is taking some extra time).  Default retry logic is 5 retries.    

Ensure you are in the proper directory 
```azurecli-interactive
cd $HOME/fhir-loader/scripts
``` 

Launch the deployFhirBulk.bash shell script 
```azurecli-interactive
./deployFhirBulk.bash 
``` 

Optionally the deployment script can be used with command line options 
```azurecli
./deployFhirBulk.bash -i <subscriptionId> -g <resourceGroupName> -l <resourceGroupLocation> -n <deployPrefix> -k <keyVaultName> -o <fhir or proxy>
```


Azure Components installed 
 - Function App with App Insights and Storage 
 - Function App Service plan 
 - EventGrid 
 - Storage Account (with containers/queues)
 - Keyvault (if none exist)

Information needed by this script 
 - Subscription
 - Resource Group Name and Location 
 - Keyvault Name 

This script prompts users for the existing Key Vault name, searches for FHIR Service values in the Key Vault, and if found, loads them. Otherwise the script prompts users for the FHIR Service 
 - Client ID
 - Resource 
 - Tenant Name
 - URL 

The deployment script connects the Event Grid System Topics with the respective function app

FHIR-Loader Connections  

Event Grid System Topic            | Connects to Function App   | Located               
-----------------------------------|----------------------------|--------------------
ndjsoncreated                      | ImportNDJSON               | EventGrid  
bundlecreated                      | ImportBundleEventGrid      | EventGrid  


FHIR-Loader Application Configuration values loaded by this script 

Name                               | Value                      | Located              
-----------------------------------|----------------------------|--------------------
APPINSIGHTS_INSTRUMENTATIONKEY     | GUID                       | App Service Config  
AzureWebJobsStorage                | Endpoint                   | App Service Config 
FUNCTIONS_EXTENSION_VERSION        | Function Version           | App Service Config 
FUNCTIONS_WORKER_RUNTIME           | Function runtime           | App Service Config 
FBI-TRANSFORMBUNDLES               | True (transaction->batch)  | App Service Config
FS-URL                             | FHIR Service URL           | App Service Config  
SA-FHIR-USEMSI                     | MSI Identity value         | App Service Config   
FBI-STORAGEACCT                    | Storage Connection         | Keyvault reference 
FS-CLIENT-ID                       | FHIR Service Client ID     | Keyvault reference 
FS-SECRET                          | FHIR Service Client Secret | Keyvault reference 
FS-TENANT-NAME                     | FHIR Service TENANT ID     | Keyvault reference 
FS-RESOURCE                        | FHIR Service Resource ID   | Keyvault reference 

FHIR-Loader - Application Configuration values - unique values 

Name                                              | Value  | Used For              
--------------------------------------------------|--------|-----------------------------------
AzureWebJobs.ImportBundleBlobTrigger.Disabled     | 1      | Prevents Conflicts wit Event Grid 
FBI-POOLEDCON-MAXCONNECTIONS                      | 20     | Limits service timeouts
WEBSITE_RUN_FROM_PACKAGE                          | 1      | Optional - sets app to read only
 
## Upgrade from previous fhir-loader
If you are upgrading from a previous version of fhir-loader you will need to redeploy using the instructions in step 1. of this document. New infrastructure components were added to the fhir-loader

 

