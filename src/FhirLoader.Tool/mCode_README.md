#  mCode Implementation Guide.

  The below steps will help you to understand how you can set up and send the mCode profiles to fhir server.


## Implementation Steps

 Let's say you want to upload the mCode profile to the Fhir server. so, as a first step, you will download the mCode package to your local folder.

 * Create an empty directroy in your drive.
 * Open that directory in Command Prompt.
 * Run this below command in cmd.

 ```sh
 npm --registry https://packages.simplifier.net install hl7.fhir.us.mcode@2.0.0
 ```

 * It will download all the mCode Files.
 
 Now execute the following command to send the mCode files to the Fhir server. 

 ```sh
 microsoft-fhir-loader --package "Your directory path" --fhir "your fhir server url"
 ```
  
 Once this process is started, it will first check if the package.json and  .index.json files exist or not. If it's not in the folder, it shows the below error.

 ```sh
Provided package path does not have .index.json and/or package.json file. Skipping the loading process.
 ```

 As a next step, it will check the package type in the package.json. If package types are conformance, fhir.ig, or fhir.core, then it will be processed to the next step, otherwise it will show the below error.

```sh
Package type is not valid. Skipping the loading process.
```
    
To learn more about .index.json and package.json, please refer to this [link](https://confluence.hl7.org/pages/viewpage.action?pageId=35718629#NPMPackageSpecification-Packagemanifest)   
  
 If all validations are successful, then it reads all the profile file names from the.index.json file. 
 
 While reading the files,
   * If it's  SearchParameter file, then the process will check if the file is already posted on the server or not. All posted files will be skipped and the rest of the files will be posted to the server.
   * Other files will be directly posted to the file server.

Once the process is finished, if any searchparamater files were posted to fhir server, you may need to run a reindex job so that the new search parameters can be useed. Learn more [here](https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/how-to-run-a-reindex) about reindex.

A question will prompt to console that says ,
   
```sh
Do you want loader to submit reindex ?
```

If you click yes, it will reindex and show a url with reindex id. You can then navigate to this URL to check on the status of the reindex job.
If you click no, it will print the messsages as shown in last two bullets.


If any error occurs during the process, it will be printed on the console window.
At the end of the process, it will print the message with how many resources are posted to the server.

