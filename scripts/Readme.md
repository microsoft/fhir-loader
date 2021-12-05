# FHIR-Loader Getting startd scripts Readme
Script purpose, order of execution and other steps necessary to get up and running with FHIR-Loader

## Errata 
There are no open issues at this time. 

## Prerequisites 

These scripts will gather (and export) information necessary to the proper deployment and configuration of Azure Healthcare API for FHIR, an Application Service Client, Key Vault and Resource Groups secure information will be stored in the Keyvault.  
 - Prerequisites:  User must have rights to deploy resources at the Subscription scope 

__Note__
A Keyvault is necessary for securing Service Client Credentials used with the FHIR Service and FHIR-Proxy.  Only 1 Keyvault should be used as this script scans the keyvault for FHIR Service and FHIR-Proxy values. If multiple Keyvaults have been used, please use the [backup and restore](https://docs.microsoft.com/en-us/azure/key-vault/general/backup?tabs=azure-cli) option to copy values to 1 keyvault.

__Note__ 
The FHIR-loader scripts are designed for and tested from the Azure Cloud Shell - Bash Shell environment.


### Naming & Tagging
All Azure resource types have a scope that defines the level that resource names must be unique.  Some resource names, such as PaaS services with public endpoints have global scopes so they must be unique across the entire Azure platform.    Our deployment scripts strive to suggest naming standards that group logial connections while aligning with Azure Best Practices.  Customers are prompted to accept a default or suppoly their own names during installation, examples include:

Prefix      | Workload        |  Number     | Resource Type 
------------|-----------------|-------------|---------------
NA          | fhir            | random      | NA 
User input  | secure function | random      | storage 

Resources are tagged with their deployment script and origin.  Customers are able to add Tags after installation, examples include::

Origin              |  Deployment       
--------------------|-----------------
HealthArchitectures | FHIR-Loader   

---

## Setup 
Please note you should deploy these components into a tenant and subscriotion where you have appropriate permissions to create and manage Application Registrations (ie Application Adminitrator RBAC Role), and can deploy Resources at the Subscription Scope. 

Launch Azure Cloud Shell (Bash Environment)  
  
[![Launch Azure Shell](/docs/images/launchcloudshell.png "Launch Cloud Shell")](https://shell.azure.com/bash?target="_blank")

Clone the repo to your Bash Shell (CLI) environment 
```azurecli-interactive
git clone https://github.com/microsoft/fhir-loader 
```
Change working directory to the repo Scripts directory
```azurecli-interactive
cd ./fhir-loader/scripts
```

Make the Bash Shell Scripts used for Deployment and Setup executable 
```azurecli-interactive
chmod +x *.bash 
```

## Step 1.  deployFhirBulk.bash
This is the main component deployment script for the Azure Components and application code.  Note that retry logic is used to account for provisioning delays, i.e., networking provisioning is taking some extra time.  Default retry logic is 5 retries.   

This is the main component deployment script for the Azure Components.    

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
./deployFhirBulk.bash -i <subscriptionId> -g <resourceGroupName> -l <resourceGroupLocation> -n <deployPprefix> -k <keyVaultName> -o <fhir or proxy>
```


Azure Components installed 
 - Function App with App Insights and Storage 
 - Function App Service plan 
 - EventGrid 
 - Storage Account (with containers)
 - Keyvault (if none exist)

Information needed by this script 
 - Subscription
 - Resource Group Name and Location 
 - Keyvault Name 

This script prompts users for KeyVault Name, searches for FHIR Service Values if Found loads them; otherwise the script prompts users for the FHIR Service 
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
 
 

