# FHIR Loader Tool

Simple, dotnet command line based FHIR loader that can be easily used to load resources to a FHIR server, while providing simple performance monitoring.

## Installation

  1. Ensure you have dotnet 6+ installed. Run `dotnet --version` from the command line. If you don't have 6+, install [from here](https://docs.microsoft.com/dotnet/core/install/).
  2. Clone this repo and open a terminal/command prompt to `src/FhirLoader.Tool`.
  3. Run `dotnet pack`
  4. Install the tool from disk: `dotnet tool install --global --add-source ./nupkg  FhirLoader.Tool`

## Usage

Send 100 Synthea patients (one patient at a time):
```sh
microsoft-fhir-loader --blob "https://ahdssampledata.blob.core.windows.net/fhir/synthea-bundles-100/" --fhir "fhir-server-url"
```

Send 1000 Synthea patients (one resource type at a time):
```sh
microsoft-fhir-loader --blob "https://ahdssampledata.blob.core.windows.net/fhir/synthea-ndjson-100/" --fhir "fhir-server-url"
```

Send your own local bundle/bulk files:
```sh
microsoft-fhir-loader --file "$HOME/source/synthea/output/fhir" --fhir "fhir-server-url"
```

Send your own package files:
```sh
microsoft-fhir-loader --package "~/Downloads/my-package" --fhir "fhir-server-url"
```
Here, my-package means the folder in which you store the files from the npm command.

Use the below command to install the files using npm.

```sh
npm --registry "your npm package path"
```

This tool will read all the files from the above given location. If any error occurs while reading the file to the FHIR server, it will skip that file, print the warning, and process the next file.

Please [see](/sample.md) this to learn more about how to set up and send profiles to fhir server.

### Authentication

This tool will attempt to pull your authorization context from the Azure CLI. If it cannot be found, it will open a web browser to attempt to authenticate. The tool uses [DefaultAzureCredential](https://docs.microsoft.com/dotnet/api/azure.identity.defaultazurecredential?view=azure-dotnet).

This tool also supports credentials via Environment Variables, Managed Identity, Visual Studio, Visual Studio Code, and Azure Powershell.

### Advanced Usage

Control the size of bundles sent:

```sh
  microsoft-fhir-loader --batch 100 --fhir https://workspace-fhirservice.fhir.azurehealthcareapis.com/ --path ~/synthea/fhir
```

Parameters:

```sh
  --folder     Folder path to FHIR data to load.
  --blob       Url to public blob storage container with FHIR data to load.
  --package    Package path to FHIR data to load.
  --fhir       Required. Base URL of your FHIR server.
  --batch      Size of bundles to split large files into when sending resources.
  --tenant-id  Tenant ID (other than your default)
  --help       Display help screen.
  --version    Display version information.
```

Get help about the command:

```sh
microsoft-fhir-loader --help
```

## Uninstall

Run:

```sh
dotnet tool uninstall FhirLoader.CLI --global
```