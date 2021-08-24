# FHIR-Loader Getting startd scripts Readme
Script purpose, order of execution and other steps necessary to get up and running with FHIR-Loader


## Prerequisites 

These scripts will gather (and export) information necessary to the proper operation of the FHIR-Loader and will store that information into a Keyvault (either FHIR-Proxy Keyvault or a Customer Provided one). 

 - Prerequisites:  Azure API for FHIR
 - Prerequisites:  Ability to Provision resources within the Subscription scope


## Step 1.  deployFhirProxy.bash
This is the main component deployment script for the Azure Components and application code.  Note that retry logic is used to account for provisioning delays, i.e., networking provisioning is taking some extra time.  Default retry logic is 5 retries.   

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
FBI-POOLEDCON-MAXCONNECTIONS                      | 20     | Limitis service timeouts
WEBSITE_RUN_FROM_PACKAGE                          | 1      | Optional - sets app to read only
 
 

