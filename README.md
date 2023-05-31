# FHIR Loader

<p>
<b>FHIR Loader</b> is a set of tools to help you easily load FHIR data into <a href="https://learn.microsoft.com/azure/healthcare-apis/healthcare-apis-overview">Azure Health Data Services</a> and <a href = "https://learn.microsoft.com/azure/healthcare-apis/azure-api-for-fhir/">Azure API for FHIR</a>. FHIR Loader implements common patterns for loading data so you don't have to write API calls or custom code.
</p>

This repository contains two FHIR Loader tools:

- **[FHIR Bulk Loader & Export](/src/FhirLoader.BulkImport/README.md)** is an Azure Function based tool that imports FHIR bundles and NDJSON files in a performant, resilient, and reliable way from an Azure Storage account. It also contains a performant, patient-centric export for Azure API for FHIR.

- **[FHIR Loader Command Line Tool](/src/FhirLoader.CommandLineTool/README.md)** is a .NET, command line application executed on your computer that can automatically load FHIR packages/profiles and small sets of FHIR data.

## Getting started

### FHIR Bulk Loader & Export

To quickly deploy the FHIR Bulk Loader, you can use the Azure deployment below. This deployment method provides simple configuration.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fmicrosoft%2Ffhir-loader%2Ffhir-loader-cli%2Fscripts%2FfhirBulkImport.json)

For a more configurable deployment, you can use the script deployment. Check out the [deployment guide here](/docs/BulkImport/deployment.md). 

For more information, check out the [README document here](/src/FhirLoader.BulkImport/README.md).

### FHIR Loader Command Line Tool

Check out the [README document here](/src/FhirLoader.CommandLineTool/README.md).

## Features

### FHIR Bulk Loader & Export

- Automatically sends FHIR bundles and NDJSON files (compressed or uncompressed) uploaded to an Azure Storage account to the target FHIR server.
    - Compatible with Azure Health Data Services, Azure API for FHIR, or a open-source Microsoft FHIR Server.
- Best for continual
- Optimized high-speed, parallel, and performant logic built on Azure Event Grid from an Azure Storage account or custom Event Grid source.
- Full auditing, error logging, and retry logic to respond to throttled requests.
- High-speed parallel orchestrated patient-centric export for Azure API for FHIR
  - Azure Health Data Services users should use [$export](https://learn.microsoft.com/azure/healthcare-apis/fhir/export-data).

### FHIR Loader Command Line Tool

- Local .NET tool to send FHIR bundles and NDJSON files to the target FHIR server.
- Splits large FHIR files into smaller bundles compatible with Azure Health Data Services and Azure API for FHIR.
- Best for loading small FHIR datasets that need to be sent one time.
- Easily load [FHIR Packages](https://registry.fhir.org/learn) (also known as FHIR profiles).

## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

FHIR is the registered trademark of HL7 and is used with the permission of HL7.
