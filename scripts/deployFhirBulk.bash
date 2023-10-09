#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# -e: immediately exit if any command has a non-zero exit status
# -o: prevents errors in a pipeline from being masked
# IFS new value is less likely to cause confusing bugs when looping arrays or arguments (e.g. $@)
#
#FHIR Loader Setup --- Author Steve Ordahl Principal Architect Health Data Platform
#

# Resources Required by this script 
# Need to add a test to see if these resource providers are enabled
# Service Bus 
# Function App 
# App Insights 


#########################################
# HealthArchitecture Deployment Settings 
#########################################
declare TAG="HealthArchitectures = FHIRBulk"
declare functionSKU="B1"
declare functionWorkers="2"
declare storageSKU="Standard_LRS"


#########################################
# FHIR Bulk Loader & Export Default App Settings 
#########################################
declare suffix=$RANDOM
declare defresourceGroupLocation="westus2"
declare defresourceGroupName="bulk-fhir-"$suffix
declare defdeployPrefix="bulk"$suffix
declare defAppName="sfb-"$defdeployPrefix
declare defkeyVaultName="kv-"$defdeployPrefix
declare genPostmanEnv="yes"

#########################################
#  Function Variables 
#########################################
# the import variables and Subscription variables should come from the source code.  The eg endpoints variables are placeholders  
declare importNdjsonvar="ndjsonqueue"
declare importBundle="bundlequeue"
declare eventGridEndpointNDJSON=""
declare eventGridEndpointBundle=""
declare egNdjsonSubscription="ndjsoncreated"
declare egBundleSubscription="bundlecreated"

#########################################
#  Common Variables 
#########################################
declare script_dir="$( cd -P -- "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd -P )"
declare defSubscriptionId=""
declare subscriptionId=""
declare resourceGroupName=""
declare resourceGroupExists=""
declare useExistingResourceGroup=""
declare createNewResourceGroup=""
declare resourceGroupLocation=""
declare storageAccountNameSuffix="store"
declare storageConnectionString=""
declare serviceplanSuffix="asp"
declare redisAccountNameSuffix="cache"
declare redisConnectionString=""
declare redisKey=""
declare stepresult=""
declare distribution="distribution/publish.zip"
declare postmanTemplate="postmantemplate.json"

# FHIR
declare defAuthType="MSI"
declare authType=""
declare fhirServiceWorkspace=""
declare fhirServiceUrl=""
declare fhirServiceClientId=""
declare fhirServiceClientSecret=""
declare fhirServiceTenantId=""
declare fhirServiceAudience=""
declare fhirResourceId=""
declare fhirServiceName=""
declare fhirServiceExists=""
declare fhirServiceProperties=""
declare fhirServiceClientAppName=""
declare fhirServiceClientObjectId=""
declare fhirServiceClientRoleAssignment=""

# KeyVault 
declare keyVaultName=""
declare keyVaultExists=""
declare useExistingKeyVault=""
declare createNewKeyVault=""
declare storeFHIRServiceConfig=""


# Postman 
declare fhirServiceUrl=""
declare fhirServiceClientId=""
declare fhirServiceClientSecret=""
declare fhirServiceTenant=""
declare fhirServiceAudience=""



declare deployPrefix=""
declare stepresult=""
declare bulkAppName=""
declare defsubscriptionId=""
declare subscriptionId=""
declare resourceGroupName=""
declare resourceGroupLocation=""
declare bulkAppName=""
declare deployPrefix=""
declare storageConnectionString=""
declare storesourceid=""
declare stepresult=""
declare keyVaultName=""
declare kvexists=""
declare msi=""
declare fahost=""
declare fsclientid=""
declare fstenantid=""
declare fssecret=""
declare fsresource=""
declare fsurl=""
declare fphost=""
declare fpclientid=""
declare egndjsonresource=""
declare egbundleresource=""
declare createkv=""
declare msifhirserverdefault=""
declare msifhirservername=""
declare msifhirserverrg=""
declare msifhirserverrid=""
declare msirolename="FHIR Data Contributor"



#########################################
#  Script Functions 
#########################################

function intro {
	# Display the intro - give the user a chance to cancel 
	#
	echo " "
	echo "FHIR-Loader Bulk Import and Export Application installation script... "
	echo " - Prerequisite:  Azure API for FHIR or FHIR Server must be installed"
	echo " - Prerequisite:  Client Application connection information for FHIR Service"
	echo " - Prerequisite:  A Keyvault service"
	echo " "
	echo "Note: You must have rights to able to provision resources within the Subscription scope"
	echo " "
	read -p 'Press Enter to continue, or Ctrl+C to exit'
}


function fail () {
  echo $1 >&2
  exit 1
}


function retry () {
  local n=1
  local max=5
  local delay=30
  while true; do
    "$@" && break || {
      if [[ $n -lt $max ]]; then
        ((n++))
        echo "Command failed. Retry Attempt $n/$max in $delay seconds:" >&2
        sleep $delay ;
      else
        fail "The command has failed after $n attempts."
      fi
    }
  done
}

function kvuri {
	echo "@Microsoft.KeyVault(SecretUri=https://"$keyVaultName".vault.azure.net/secrets/"$@"/)"
}


usage() { echo "Usage: $0 -i <subscriptionId> -g <resourceGroupName> -l <resourceGroupLocation> -n <deployPprefix> -k <keyVaultName> -o <option>" 1>&2; exit 1; }


#########################################
#  Script Main Body (start here) 
#########################################
#
# Initialize parameters specified from command line
#
while getopts ":i:g:l:n:k:o:" arg; do
	case "${arg}" in
		n)
			deployPrefix=${OPTARG:0:14}
			deployPrefix=${deployPrefix,,}
			deployPrefix=${deployPrefix//[^[:alnum:]]/}
			;;
		i)
			subscriptionId=${OPTARG}
			;;
		g)
			resourceGroupName=${OPTARG}
			;;
		l)
			resourceGroupLocation=${OPTARG}
			;;
		k)
			keyVaultName=${OPTARG}
			;;
		o)
			option=${OPTARG}
			;;
		esac
done
shift $((OPTIND-1))
echo "Executing "$0"..."
echo "Checking Azure Authentication..."

#login to azure using your credentials
#
az account show 1> /dev/null

if [ $? != 0 ];
then
	az login
fi

# set default subscription information
#
defsubscriptionId=$(az account show --query "id" --out json | sed 's/"//g') 

# Test for correct directory path / destination 
#
if [ -f "${script_dir}/$0" ] && [ -f "${script_dir}/deployFhirBulk.bash" ] ; then
	echo "Checking Script execution directory..."
else
	echo "Please ensure you launch this script from within the ./scripts directory"
	usage ;
fi


# Call the intro function - give the user a chance to exit 
#
intro


# ---------------------------------------------------------------------
# Prompt for common parameters if some required parameters are missing
# 
echo " "
echo "Collecting Azure Parameters (unless supplied on the command line) "

if [[ -z "$subscriptionId" ]]; then
	echo "Enter your subscription ID ["$defsubscriptionId"]:"
	read subscriptionId
	if [ -z "$subscriptionId" ] ; then
		subscriptionId=$defsubscriptionId
	fi
	[[ "${subscriptionId:?}" ]]
fi

if [[ -z "$resourceGroupName" ]]; then
	echo "This script will look for an existing resource group, otherwise a new one will be created "
	echo "You can create new resource groups with the CLI using: az group create "
	echo "Enter a resource group name <press Enter to accept default> ["$defresourceGroupName"]: "
	read resourceGroupName
	if [ -z "$resourceGroupName" ] ; then
		resourceGroupName=$defresourceGroupName
	fi
	[[ "${resourceGroupName:?}" ]]
fi


if [[ -z "$resourceGroupLocation" ]]; then
	echo "If creating a *new* resource group, you need to set a location "
	echo "You can lookup locations with the CLI using: az account list-locations "
	echo "Enter resource group location <press Enter to accept default> ["$defresourceGroupLocation"]: "
	read resourceGroupLocation
	if [ -z "$resourceGroupLocation" ] ; then
		resourceGroupLocation=$defresourceGroupLocation
	fi
	[[ "${resourceGroupLocation:?}" ]]
fi


# Ensure there are subscriptionId and resourcegroup names 
#
if [ -z "$subscriptionId" ] || [ -z "$resourceGroupName" ]; then
	echo "Either one of subscriptionId, resourceGroupName is empty, exiting..."
	exit 1
fi

# set the default subscription id
#
echo " "
echo "Setting default subscription id"
az account set --subscription $subscriptionId


# Check if the resource group exists
#
echo " "
echo "Checking for existing Resource Group named ["$resourceGroupName"]"
resourceGroupExists=$(az group exists --name $resourceGroupName)
if [[ "$resourceGroupExists" == "true" ]]; then
    echo "  Resource Group ["$resourceGroupName"] found"
    useExistingResourceGroup="yes" 
    createNewResourceGroup="no" ;
else
    echo "  Resource Group ["$resourceGroupName"] not found a new Resource group will be created"
    useExistingResourceGroup="no" 
    createNewResourceGroup="yes"
fi

# ---------------------------------------------------------------------
# Prompt for script parameters if some required parameters are missing
#
echo " "
echo "Collecting Script Parameters (unless supplied on the command line).."

# Set Default Deployment Prefix
#
defdeployPrefix=${defdeployPrefix:0:14}
defdeployPrefix=${defdeployPrefix//[^[:alnum:]]/}
defdeployPrefix=${defdeployPrefix,,}

if [[ -z "$deployPrefix" ]]; then
	echo "Enter your deploy prefix - bulk components begin with this prefix ["$defdeployPrefix"]:"
	read deployPrefix
	if [ -z "$deployPrefix" ] ; then
		deployPrefix=$defdeployPrefix
	fi
	deployPrefix=${deployPrefix:0:14}
	deployPrefix=${deployPrefix//[^[:alnum:]]/}
    deployPrefix=${deployPrefix,,}
	[[ "${deployPrefix:?}" ]]
else 
	bulkAppName="sfb-"${deployPrefix}
fi

# Set a Default Function App Name
# 
if [[ -z "$bulkAppName" ]]; then
	echo "Enter the bulk loader & export function app name - this is the name of the function app ["$defAppName"]:"
	read bulkAppName
	if [ -z "$bulkAppName" ] ; then
		bulkAppName=$defAppName
	fi
fi
[[ "${bulkAppName:?}" ]]

# Obtain Keyvault Name 
#
if [[ -z "$keyVaultName" ]]; then
	echo "Enter a Key Vault name <press Enter to accept default> ["$defkeyVaultName"]:"
	read keyVaultName
	if [ -z "$keyVaultName" ] ; then
		keyVaultName=$defkeyVaultName
	fi
	[[ "${keyVaultName:?}" ]]
fi

# Check KV exists
#
echo "Checking for keyvault "$keyVaultName"..."
keyVaultExists=$(az keyvault list --query "[?name == '$keyVaultName'].name" --out tsv)
if [[ -n "$keyVaultExists" ]]; then
	set +e 
	echo "  "$keyVaultName" found"
	echo " "
	echo "Checking for FHIR Service configuration..."
	fhirServiceUrl=$(az keyvault secret show --vault-name $keyVaultName --name FS-URL --query "value" --out tsv 2>/dev/null)
	if [ -n "$fhirServiceUrl" ]; then
		echo "  FHIR Service URL: "$fhirServiceUrl

        fhirResourceId=$(az keyvault secret show --vault-name $keyVaultName --name FS-URL --query "value" --out tsv | awk -F. '{print $1}' | sed -e 's/https\:\/\///g' 2>/dev/null) 
		echo "  FHIR Service Resource ID: "$fhirResourceId 

		fhirServiceTenant=$(az keyvault secret show --vault-name $keyVaultName --name FS-TENANT-NAME --query "value" --out tsv 2>/dev/null)
		echo "  FHIR Service Tenant ID: "$fhirServiceTenant 
		
		fhirServiceClientId=$(az keyvault secret show --vault-name $keyVaultName --name FS-CLIENT-ID --query "value" --out tsv 2>/dev/null)
		echo "  FHIR Service Client ID: "$fhirServiceClientId
		
		fhirServiceClientSecret=$(az keyvault secret show --vault-name $keyVaultName --name FS-SECRET --query "value" --out tsv 2>/dev/null)
		echo "  FHIR Service Client Secret: *****"
		
		fhirServiceAudience=$(az keyvault secret show --vault-name $keyVaultName --name FS-RESOURCE --query "value" --out tsv 2>/dev/null) 
		echo "  FHIR Service Audience: "$fhirServiceAudience 
		useExistingKeyVault="yes"
		createNewKeyVault="no"
		storeFHIRServiceConfig="no"	;
	else	
		echo "  unable to read FHIR Service URL from ["$keyVaultName"]" 
        echo "  setting script to create new FHIR Service Entry in existing Key Vault ["$keyVaultName"]"
        useExistingKeyVault="yes"
		storeFHIRServiceConfig="yes"
        createNewKeyVault="no" ;
	fi 
else
	echo "  Script will deploy new Key Vault ["$keyVaultName"]" 
    useExistingKeyVault="no"
    createNewKeyVault="yes"
fi
# Prompt for FHIR Server Parameters if not found in KeyVault
#
if [ -z "$fhirServiceUrl" ]; then
	echo "Enter the destination FHIR Server URL (aka Endpoint):"
	read fhirServiceUrl
	if [ -z "$fhirServiceUrl" ] ; then
		echo "You must provide a destination FHIR Server URL"
		exit 1 ;
	fi
fi

# Setup Auth type based on input 
# 
until [[ "$authType" == "MSI" ]] || [[ "$authType" == "SP" ]]; do
	echo "Which authentication method should be used internally to connect from the fhir-loader to the FHIR Service MSI or SP? ["$defAuthType"]:"
	read authType
	if [ -z "$authType" ] ; then
		authType=$defAuthType
	fi
	authType=${authType^^}
done
# Setup Auth type based on input 
# 
if [[ "$authType" == "SP" ]] ; then 
	echo "Auth Type is set to Service Principal (SP)"

	if [ -z "$fhirServiceTenant" ] ; then
		echo "  Enter the FHIR Service - Tenant ID (GUID)"
		read fhirServiceTenant
		if [ -z "$fhirServiceTenant" ] ; then
			echo "You must provide a FHIR Service - Tenant ID (GUID)"
			exit 1;
		fi
	fi 
	[[ "${fhirServiceTenant:?}" ]]

	if [ -z "$fhirServiceClientId" ] ; then 
		echo "  Enter the FHIR Service - SP Client ID (GUID)"
		read fhirServiceClientId
		if [ -z "$fhirServiceClientId" ] ; then
			echo "You must provide a FHIR Service - SP Client ID (GUID)"
			exit 1;
		fi
	fi 
	[[ "${fhirServiceClientId:?}" ]]

	if [ -z "$fhirServiceClientSecret" ] ; then 
		echo "  Enter the FHIR Service - SP Client Secret"
		read fhirServiceClientSecret
		if [ -z "$fhirServiceClientSecret" ] ; then
			echo "You must provide a FHIR Service - SP Client Secret"
			exit 1;
		fi
	fi 
	[[ "${fhirServiceClientSecret:?}" ]]

	if [ -z "$fhirServiceAudience" ] ; then 
		echo "  Enter the FHIR Service - SP Audience (URL)"
		read fhirServiceAudience
		if [ -z "$fhirServiceAudience" ] ; then
			echo "You must provide a FHIR Service - SP Audience (URL)"
			exit 1;
		fi
	fi 
	[[ "${fhirServiceAudience:?}" ]]
else
		echo "Auth Type is set to Managed Service Identity (MSI)"		
		echo "Note: API for FHIR or AHDS FHIR Server must be in same tenant as fhir-loader to use MSI..."
		msifhirserverdefault=${fhirServiceUrl#https://}
		msifhirserverdefault=${msifhirserverdefault%%.*}
		if [[ "$fhirServiceUrl" == *".fhir.azurehealthcareapis.com"* ]]; then
			IFS='-' read -ra Arr <<< "$msifhirserverdefault"
			fhirServiceWorkspace=${Arr[0]}
			msifhirservername=""
			for (( i=1; i<${#Arr[@]}; i++ ));
			do
				msifhirservername=$msifhirservername${Arr[$i]}"-"
			done
			msifhirservername=${msifhirservername::-1}
			IFS=$'\n\t'
			msifhirserverrg=$(az resource list --name $fhirServiceWorkspace/$msifhirservername --resource-type 'Microsoft.HealthcareApis/workspaces/fhirservices' --query "[0].resourceGroup" --output tsv)
		else 
			msifhirservername=$msifhirserverdefault
			msifhirserverrg=$(az resource list --name $msifhirservername --resource-type 'Microsoft.HealthcareApis/services' --query "[0].resourceGroup" --output tsv)
		fi
		fhirServiceAudience=${fhirServiceUrl}
fi
sptenant=$(az account show --name $subscriptionId --query "tenantId" --out tsv)

# Prompt for final confirmation
#
echo "--- "
echo "Ready to start deployment of FHIR-Bulk Loader Application: ["$bulkAppName"] with the following values:"
echo "FHIR Service URL:...................... "$fhirServiceUrl
echo "FHIR Service Auth Type:................ "$authType
if [[ "$authType" == "MSI" ]] ; then
	echo "  FHIR Server Workspace................ "$fhirServiceWorkspace
	echo "  FHIR Server Name..................... "$msifhirservername
	echo "  FHIR Server Resource Group........... "$msifhirserverrg
fi
echo "Subscription ID:....................... "$subscriptionId
echo "Subscription Tenant ID:................ "$sptenant
echo "Resource Group Name:................... "$resourceGroupName
echo "  Use Existing Resource Group:......... "$useExistingResourceGroup
echo "  Create New Resource Group:........... "$createNewResourceGroup
echo "Resource Group Location:............... "$resourceGroupLocation 
echo "KeyVault Name:......................... "$keyVaultName
echo "  Use Existing Key Vault:.............. "$useExistingKeyVault
echo "  Create New Key Vault:................ "$createNewKeyVault
echo " "
echo "Please validate the settings above before continuing"
read -p 'Press Enter to continue, or Ctrl+C to exit...'

#############################################################
#  Start Setup & Deployment 
#############################################################
#

# Set up variables
if [[ -z "$fhirServiceWorkspace" ]]; then
	msifhirserverrid="/subscriptions/"$subscriptionId"/resourceGroups/"$msifhirserverrg"/providers/Microsoft.HealthcareApis/services/"$msifhirservername
else
	msifhirserverrid="/subscriptions/"$subscriptionId"/resourceGroups/"$msifhirserverrg"/providers/Microsoft.HealthcareApis/workspaces/"$fhirServiceWorkspace"/fhirservices/"$msifhirservername
fi

echo "Starting Deployments... "
(
    if [[ "$useExistingResourceGroup" == "no" ]]; then
        echo " "
        echo "Creating Resource Group ["$resourceGroupName"] in location ["$resourceGroupLocation"]"
        set -x
        az group create --name $resourceGroupName --location $resourceGroupLocation --output none --tags $TAG ;
    else
        echo "Using Existing Resource Group ["$resourceGroupName"]"
    fi

    if [[ "$useExistingKeyVault" == "no" ]]; then
        echo " "
        echo "Creating Key Vault ["$keyVaultName"] in location ["$resourceGroupName"]"
        set -x
        stepresult=$(az keyvault create --name $keyVaultName --resource-group $resourceGroupName --location  $resourceGroupLocation --tags $TAG --output none)
		
		sleep 3 ;
    else
        echo "Using Existing Key Vault ["$keyVaultName"]"
    fi
)
echo "Storing FHIR Service information in KeyVault ["$keyVaultName"]"
(
	echo "Storing FHIR Server Information in KeyVault..."
	stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-URL" --value $fhirServiceUrl)
	stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-RESOURCE" --value $fhirServiceAudience)
    if [[ "$authType" == "SP" ]] ; then 
		stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-TENANT-NAME" --value $fhirServiceTenant)
		stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-CLIENT-ID" --value $fhirServiceClientId)
		stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-SECRET" --value $fhirServiceClientSecret)
		stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-CLIENT-SECRET" --value $fhirServiceClientSecret)
		stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-ISMSI" --value "false")
	else
		stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-ISMSI" --value "true")
	fi
	
)
echo "Creating FHIR Bulk Loader & Export Function Application"
(

	# Create Storage Account
	#
	echo "Creating Storage Account ["$deployPrefix$storageAccountNameSuffix"]..."
	stepresult=$(az storage account create --name $deployPrefix$storageAccountNameSuffix --resource-group $resourceGroupName --location  $resourceGroupLocation --sku $storageSKU --encryption-services blob --tags $TAG)

	echo "Retrieving Storage Account Connection String..."
	storageConnectionString=$(az storage account show-connection-string -g $resourceGroupName -n $deployPrefix$storageAccountNameSuffix --query "connectionString" --out tsv)
	
	echo "Storing Storage Account Connection String in Key Vault..."
	stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FBI-STORAGEACCT" --value $storageConnectionString)
	
	echo "Creating containers..."
	echo "  Import bundles"
	stepresult=$(az storage container create -n bundles --connection-string $storageConnectionString)
	echo "  Import ndjson"
	stepresult=$(az storage container create -n ndjson --connection-string $storageConnectionString)
	echo "  Import zip"
	stepresult=$(az storage container create -n zip --connection-string $storageConnectionString)
	echo "  Export"
	stepresult=$(az storage container create -n export --connection-string $storageConnectionString)
	echo "  Export trigger"
	stepresult=$(az storage container create -n export-trigger --connection-string $storageConnectionString)
	echo "Creating storage queues..."
	echo "  Ndjson queue"
	stepresult=$(az storage queue create -n ndjsonqueue --connection-string $storageConnectionString)
	echo "  Bundles queue"
	stepresult=$(az storage queue create -n bundlequeue --connection-string $storageConnectionString)
	
	# Create Service Plan
	#
	echo "Creating FHIR Loader Function App Serviceplan ["$deployPrefix$serviceplanSuffix"]..."
	stepresult=$(az appservice plan create -g  $resourceGroupName -n $deployPrefix$serviceplanSuffix --number-of-workers $functionWorkers --sku $functionSKU --tags $TAG)
	
	# Create the function app
	echo "Creating FHIR Bulk Loader & Export Function App ["$bulkAppName"]..."
	fahost=$(az functionapp create --name $bulkAppName --storage-account $deployPrefix$storageAccountNameSuffix  --plan $deployPrefix$serviceplanSuffix  --resource-group $resourceGroupName --runtime dotnet --os-type Windows --functions-version 4 --query "defaultHostName" --output tsv --only-show-errors)
	stepresult=$(az functionapp config set --net-framework-version v6.0 --name $bulkAppName --resource-group $resourceGroupName)
	stepresult=$(az functionapp update --name $bulkAppName --resource-group $resourceGroupName --set httpsOnly=true)
	
	# Result Echo
	echo "FHIR Bulk Loader & Export Function hostname is: "$fahost
	
	# Setup Auth 
	echo "Creating MSI for FHIR Bulk Loader & Export Function App..."
	msi=$(az functionapp identity assign -g $resourceGroupName -n $bulkAppName --query "principalId" --out tsv)
	
	# Setup Keyvault Access 
	echo "Setting KeyVault Policy to allow secret access for FHIR Bulk Loader & Export App..."
	stepresult=$(az keyvault set-policy -n $keyVaultName --secret-permissions list get set --object-id $msi)

	#If using MSI set fhir-proxy function app role assignment on FHIR Server
	if [[ "$authType" == "MSI" ]] ; then 
		echo "Setting "$fahost" app role assignment on FHIR Server..."
		stepresult=$(retry az role assignment create --assignee "${msi}" --role "${msirolename}" --scope "${msifhirserverrid}" --only-show-errors)
	fi
	# Apply App Auth and Connection settings 
	echo "Applying FHIR Bulk Loader & Export App settings ["$bulkAppName"]..."
	echo " Fhir Service URL will be referenced directly in App Settings for readability"
	stepresult=$(az functionapp config appsettings set --name $bulkAppName --resource-group $resourceGroupName --settings FBI-STORAGEACCT=$(kvuri FBI-STORAGEACCT) FS-URL=$fhirServiceUrl FS-TENANT-NAME=$(kvuri FS-TENANT-NAME) FS-CLIENT-ID=$(kvuri FS-CLIENT-ID) FS-SECRET=$(kvuri FS-SECRET) FS-RESOURCE=$(kvuri FS-RESOURCE) FS-ISMSI=$(kvuri FS-ISMSI))
	
	# Apply App Setting (static)
	# Note:  We need to by default disable the ImportBlobTrigger as that will conflict with the EventGridTrigger
	#
	echo "Applying Static App settings for FHIR Bulk Loader & Export App ["$bulkAppName"]..."
	stepresult=$(az functionapp config appsettings set --name $bulkAppName --resource-group $resourceGroupName --settings FBI-DISABLE-BLOBTRIGGER=1 FBI-DISABLE-HTTPEP=1 FBI-TRANSFORMBUNDLES=true FBI-POOLEDCON-MAXCONNECTIONS=20 AzureFunctionsJobHost__functionTimeout=23:00:00 FBI-POISONQUEUE-TIMER-CRON="0 */2 * * * *")


	# Deploy Function Application code
	echo "Deploying FHIR Bulk Loader & Export App from source repo to ["$bulkAppName"]...  note - this can take a while"
	stepresult=$(retry az functionapp deployment source config --branch personal/snarang/ndjsonfix --manual-integration --name $bulkAppName --repo-url https://github.com/microsoft/fhir-loader --resource-group $resourceGroupName)

	sleep 30	
	#---
)

echo "Creating Event Grid Subscription...  this may take a while"
(
	# Creating Event Grid Subscription 

	# assigning source input / id 
	storesourceid="/subscriptions/"$subscriptionId"/resourceGroups/"$resourceGroupName"/providers/Microsoft.Storage/storageAccounts/"$deployPrefix$storageAccountNameSuffix
	
	#EventGrid Storage Queue Endpoints
	eventGridEndpointNDJSON="/subscriptions/"$subscriptionId"/resourceGroups/"$resourceGroupName"/providers/Microsoft.Storage/storageAccounts/"$deployPrefix$storageAccountNameSuffix"/queueservices/default/queues/"$importNdjsonvar
	eventGridEndpointBundle="/subscriptions/"$subscriptionId"/resourceGroups/"$resourceGroupName"/providers/Microsoft.Storage/storageAccounts/"$deployPrefix$storageAccountNameSuffix"/queueservices/default/queues/"$importBundle
	echo " "
	echo "Creating NDJSON Subscription "
	echo "Source input: $storesourceid"
	echo "Topic name: $egndjsonresource"
	echo "Storage Queue name: $importNdjsonvar"
	echo "Endpoint: $eventGridEndpointNDJSON"

	stepresult=$(az eventgrid event-subscription create --name $egNdjsonSubscription \
     --source-resource-id $storesourceid \
     --endpoint $eventGridEndpointNDJSON  \
     --endpoint-type storagequeue --subject-begins-with /blobServices/default/containers/ndjson --subject-ends-with .ndjson --advanced-filter data.api stringin CopyBlob PutBlob PutBlockList FlushWithClose)

	
	echo " "
	echo "Creating BUNDLE Subscription "
	
	echo "Source input: $storesourceid"
	echo "Topic name: $egbundleresource"
	echo "Storage Queue name: $importBundle"
	echo "Endpoint: $eventGridEndpointBundle"

	sleep 30

	stepresult=$(az eventgrid event-subscription create --name $egBundleSubscription \
     --source-resource-id $storesourceid \
     --endpoint $eventGridEndpointBundle  \
     --endpoint-type storagequeue --subject-begins-with /blobServices/default/containers/bundles --subject-ends-with .json --advanced-filter data.api stringin CopyBlob PutBlob PutBlockList FlushWithClose)


	#---

	echo " "
	echo "**************************************************************************************"
	echo "FHIR Loader has successfully been deployed to group "$resourceGroupName" on "$(date)
	echo "Please note the following reference information for future use:"
	echo "Your Loader Destination FHIR Service URL is: "$fhirServiceUrl
	echo "Your FHIRLoader Storage Account name is: "$deployPrefix$storageAccountNameSuffix
	echo "***************************************************************************************"
	echo " "

)

if [ $? != 0 ] ; then
	echo "FHIR-Loader deployment had errors. Consider deleting the resources and trying again..."
fi

