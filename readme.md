# FHIR Bulk Loader

FHIR Bulk Loader is an Azure Function App solution that provides the following services for ingesting and exporting FHIR Resources:
 + Imports FHIR Bundles (compressed and non-compressed) and NDJSON files into FHIR Server 
 + High Speed Parallel Event Grid triggers from storage accounts or other event grid resources.
 + Complete Auditing, Error logging and Retry for throttled transactions
 + High Speed Parallel Orchestrated Patient Centric Export Capability 

# Architecture Overview
![Bulk Loader](docs/images/architecture/bulkloadarch.png)

# Prerequsites
1. The FHIR Loader requires the following compoentns 
   + an API for FHIR Service or OSS FHIR Server
   + the Microsoft FHIR Proxy (with Keyvault)

1. The following resources providers must be registered in your subscription and you must have the ability to create/update them:
   + ResourceGroup
   + Storage Account 
   + App Service Plan 
   + Function App 
   + EventGrid

2. You must have the policy assigned to read/write KeyVault Secrets in the specified Key Vault.

# Deployment
1. [Open Azure Cloud Shell](https://shell.azure.com) you can also access this from [Azure Portal](https://portal.azure.com)
2. Select Bash Shell for the environment 
3. Clone this repo
```azurecli
git clone https://github.com/microsoft/fhir-loader
```
4. Execute ```deployFhirBulkLoader.bash``` for direct FHIR Server access or ```deployFhirBulkLoader.bash -y``` to use FHIR Proxy access

Detailed instructions can be found [here](docs/deployment.md)

# Importing FHIR Data
The containers for importing data are created during deployment.  Containers are created by input file type
- for FHIR Bundles (transactional or batch), use the "bundles" container
- for NDJSON formated FHIR Bunldles use the "ndjson" container
- for Compressed (zip) formatted FHIR Bundles, use the "zip" container

Detailed configurations can be found [here](scripts/gettingStartedFhirLoader.md) 
# Exporting Bulk Patient Centric FHIR Data
The FHIR Loader also provides
# Performance 
The FHIR Loader deploys with a Standard App Service plan that can support tens of thousands file imports per hour.  During testing we have been able to scale the FHIR Loader performance to hundreds of thousands of files per hour.  

Note:  Scaling to hundreds of thousands of files per hour requires additional scaling on the FHIR API to handle the incoming messages.  High rates of 429's at the API or Cosmos data plane indicate that additional scaling is necessary. 

Detailed performance guidelines can be found [here](docs/performance.md) 

# Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

FHIR� is the registered trademark of HL7 and is used with the permission of HL7.
