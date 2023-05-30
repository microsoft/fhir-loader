# FHIR Loader

<p align="center">
<b>FHIR Loader</b> is a set of tools to help you easily load FHIR data into <a href="https://learn.microsoft.com/azure/healthcare-apis/healthcare-apis-overview">Azure Health Data Services</a> and <a href = "https://learn.microsoft.com/azure/healthcare-apis/azure-api-for-fhir/">Azure API for FHIR</a>. FHIR Loader implements common patterns for loading data so you don't have to write API calls or custom code.
</p>

FHIR Loader contains two tools:

- **FHIR Bulk Loader & Export** is an Azure Function based tool that imports FHIR bundles and NDJSON files in a performant, resilient, and reliable way from an Azure Storage account. It also contains a performant, patient-centric export for Azure API for FHIR.

- **FHIR Loader Command Line Tool** is a .NET, command line application executed on your computer that can automatically load FHIR packages/profiles and small sets of FHIR data.