# Developer Docs

## Installing a new version

1. Update the version number in `FhirLoader.Tool.csproj`.
2. Build and package the project with 
    `dotnet pack`.
3. Uninstall existing tool: 
    `dotnet tool uninstall --global FhirLoader.Tool`
4. Install local tool:
    `dotnet tool install --global --add-source ./FhirLoader.Tool/nupkg  FhirLoader.Tool`
