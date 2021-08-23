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
 - 

add FBI-POOLEDCON-MAXCONNECTIONS = 20
WEBSITE_RUN_FROM_PACKAGE=1

 

Creating Service Principal for AAD Auth (FP-RBAC settings )