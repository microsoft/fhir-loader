# Deployment
1. [Open Azure Cloud Shell](https://shell.azure.com) you can also access this from [Azure Portal](https://portal.azure.com)
2. Select Bash Shell for the environment 
3. Clone this repo ```git clone https://github.com/microsoft/fhir-loader```
4. Execute ```deploybulkloader.bash``` for direct FHIR Server access or ```deploybulkloader.bash -y``` to use FHIR Proxy access

Detailed instructions can be found [here](docs/deployment.md)