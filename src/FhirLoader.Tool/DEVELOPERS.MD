# Publishing a new version

1. Update the version number in `Applied.FhirLoader.CLI.csproj`.
2. Build and package the project with 
    `dotnet pack`.
3. Uninstall existing tool: 
    `dotnet tool uninstall --global Applied.FhirLoader.CLI`
4. Install local tool:
    `dotnet tool install --global --add-source ./Applied.FhirLoader.CLI/nupkg  Applied.FhirLoader.CLI`
5. Test a simple load.
6. Uninstall again: 
    `dotnet tool uninstall --global Applied.FhirLoader.CLI`
7. Push the new package with:

    ```sh
    dotnet nuget push --source "applied" --api-key az --interactive Applied.FhirLoader.CLI/nupkg/Applied.FhirLoader.CLI.*.nupkg
    ```

8. Install or update the tool.

## To install

```sh
dotnet tool install Applied.FhirLoader.CLI --global --add-source https://pkgs.dev.azure.com/microsofthealth/Health/_packaging/applied/nuget/v3/index.json --interactive
``` 

## To update

```sh
dotnet tool update Applied.FhirLoader.CLI --global --add-source https://pkgs.dev.azure.com/microsofthealth/Health/_packaging/applied/nuget/v3/index.json --interactive
```
