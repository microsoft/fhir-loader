#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# -e: immediately exit if any command has a non-zero exit status
# -o: prevents errors in a pipeline from being masked
# IFS new value is less likely to cause confusing bugs when looping arrays or arguments (e.g. $@)
#
#FHIR Loader Setup --- Author Steve Ordahl Principal Architect Health Data Platform
#

usage() { echo "Usage: $0 -i <subscriptionId> -g <resourceGroupName> -l <resourceGroupLocation> -p <prefix> -k <keyvault> -y (use FHIR Proxy)" 1>&2; exit 1; }

function fail {
  echo $1 >&2
  exit 1
}
function kvuri {
	echo "@Microsoft.KeyVault(SecretUri=https://"$kvname".vault.azure.net/secrets/"$@"/)"
}
function retry {
  local n=1
  local max=5
  local delay=15
  while true; do
    "$@" && break || {
      if [[ $n -lt $max ]]; then
        ((n++))
        echo "Command failed. Retry Attempt $n/$max in $delay seconds:" >&2
        sleep $delay;
      else
        fail "The command has failed after $n attempts."
      fi
    }
  done
}
declare defsubscriptionId=""
declare subscriptionId=""
declare resourceGroupName=""
declare resourceGroupLocation=""
declare serviceplanSuffix="asp"
declare faname="fload"$RANDOM
declare deployprefix=""
declare defdeployprefix=""
declare storageAccountNameSuffix="store"$RANDOM
declare storageConnectionString=""
declare storesourceid=""
declare faresourceid=""
declare stepresult=""
declare kvname=""
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
declare useproxy=""
declare egndjsonresource=""
declare egbundleresource=""
#Initialize parameters specified from command line
while getopts ":i:g:n:l:p:y" arg; do
	case "${arg}" in
		p)
			deployprefix=${OPTARG:0:14}
			deployprefix=${deployprefix,,}
			deployprefix=${deployprefix//[^[:alnum:]]/}
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
			kvname=${OPTARG}
			;;
		y)
			useproxy="yes"
			;;
		esac
done
shift $((OPTIND-1))
echo "Deploying FHIR Bulk Loader..."
echo "Checking Azure Authentication..."
#login to azure using your credentials
az account show 1> /dev/null

if [ $? != 0 ];
then
	az login
fi

defsubscriptionId=$(az account show --query "id" --out json | sed 's/"//g') 

#Prompt for parameters is some required parameters are missing
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
	echo "Enter a resource group name"
	read resourceGroupName
	[[ "${resourceGroupName:?}" ]]
fi

defdeployprefix=${resourceGroupName:0:14}
defdeployprefix=${defdeployprefix//[^[:alnum:]]/}
defdeployprefix=${defdeployprefix,,}

if [[ -z "$resourceGroupLocation" ]]; then
	echo "If creating a *new* resource group, you need to set a location "
	echo "You can lookup locations with the CLI using: az account list-locations "
	
	echo "Enter resource group location:"
	read resourceGroupLocation
fi
#Prompt for parameters is some required parameters are missing
if [[ -z "$deployprefix" ]]; then
	echo "Enter your deployment prefix ["$defdeployprefix"]:"
	read deployprefix
	if [ -z "$deployprefix" ] ; then
		deployprefix=$defdeployprefix
	fi
	deployprefix=${deployprefix:0:14}
	deployprefix=${deployprefix//[^[:alnum:]]/}
    deployprefix=${deployprefix,,}
	[[ "${deployprefix:?}" ]]
fi
if [[ -z "$kvname" ]]; then
	echo "Enter keyvault that contains FHIR Server or Proxy configuration (e.g. FS- or FP-SC- settings): "
	read kvname
fi
if [ -z "$subscriptionId" ] || [ -z "$resourceGroupName" ] || [ -z "$kvname" ]; then
	echo "Either one of subscriptionId, resourceGroupName or keyvault is empty"
	usage
fi
echo "Setting subscription id..."
#set the default subscription id
az account set --subscription $subscriptionId
#Check KV exists
echo "Checking for keyvault "$kvname"..."
kvexists=$(az keyvault list --query "[?name == '$kvname'].name" --out tsv)
if [[ -z "$kvexists" ]]; then
	echo "Cannot Locate Key Vault "$kvname" this deployment requires access to a keyvault with FHIR Server configuration settings...Consider installing the FHIR Proxy or API4FHIRStarter Project"
	exit 1
fi

echo "Checking resource groups..."

set +e

#Check for existing RG
if [ $(az group exists --name $resourceGroupName) = false ]; then
	echo "Resource group with name" $resourceGroupName "could not be found. Creating new resource group.."
	set -e
	(
		set -x
		az group create --name $resourceGroupName --location $resourceGroupLocation 1> /dev/null
	)
else
	echo "Using existing resource group..."
fi
set -e
#Start deployment
echo "Starting FHIR Loader deployment..."
(
		echo "Checking configuration settings in key vault "$kvname"..."
		if [ -n "$useproxy" ]; then
			fphost=$(az keyvault secret show --vault-name $kvname --name FP-HOST --query "value" --out tsv)
			if [ -z "$fphost" ]; then
					echo $kvname" does not appear to contain fhir proxy settings...Is the Proxy Installed?"
					exit 1
			fi
			fsurl="https://"$fphost"/fhir"
		else
			fsurl=$(az keyvault secret show --vault-name $kvname --name FS-URL --query "value" --out tsv)
			if [ -z "$fsurl" ]; then
					echo $kvname" does not appear to contain fhir server settings...FS-URL"
					exit 1
			fi
		fi
		#Create Storage Account
		echo "Creating Storage Account ["$deployprefix$storageAccountNameSuffix"]..."
		stepresult=$(az storage account create --name $deployprefix$storageAccountNameSuffix --resource-group $resourceGroupName --location  $resourceGroupLocation --sku Standard_LRS --encryption-services blob)
		echo "Retrieving Storage Account Connection String..."
		storageConnectionString=$(az storage account show-connection-string -g $resourceGroupName -n $deployprefix$storageAccountNameSuffix --query "connectionString" --out tsv)
		stepresult=$(az keyvault secret set --vault-name $kvname --name "FBI-STORAGEACCT" --value $storageConnectionString)
		echo "Creating import containers..."
		stepresult=$(az storage container create -n bundles --connection-string $storageConnectionString)
		stepresult=$(az storage container create -n ndjson --connection-string $storageConnectionString)
		#Create Service Plan
		echo "Creating FHIR Loader Function App Serviceplan ["$deployprefix$serviceplanSuffix"]..."
		stepresult=$(az appservice plan create -g  $resourceGroupName -n $deployprefix$serviceplanSuffix --number-of-workers 2 --sku B1)
		#Create the function app
		echo "Creating FHIR Loader Function App ["$faname"]..."
		fahost=$(az functionapp create --name $faname --storage-account $deployprefix$storageAccountNameSuffix  --plan $deployprefix$serviceplanSuffix  --resource-group $resourceGroupName --runtime dotnet --os-type Windows --functions-version 3 --query defaultHostName --output tsv)
		echo "Creating MSI for FHIR Loader Function App..."
		msi=$(az functionapp identity assign -g $resourceGroupName -n $faname --query "principalId" --out tsv)
		echo "Setting KeyVault Policy to allow secret access for FHIR Loader App..."
		stepresult=$(az keyvault set-policy -n $kvname --secret-permissions list get set --object-id $msi)
		echo "Retrieving FHIR Loader Function App Host Key..."
		faresourceid="/subscriptions/"$subscriptionId"/resourceGroups/"$resourceGroupName"/providers/Microsoft.Web/sites/"$faname
		fakey=$(retry az rest --method post --uri "https://management.azure.com"$faresourceid"/host/default/listKeys?api-version=2018-02-01" --query "functionKeys.default" --output tsv)
		echo "Configuring FHIR Loader App ["$faname"]..."
		if [ -n "$useproxy" ]; then
			stepresult=$(az functionapp config appsettings set --name $faname --resource-group $resourceGroupName --settings FBI-STORAGEACCT=$(kvuri FBI-STORAGEACCT) FS-URL=$fsurl FS-TENANT-NAME=$(kvuri FP-SC-TENANT-NAME) FS-CLIENT-ID=$(kvuri FP-SC-CLIENT-ID) FS-SECRET=$(kvuri FP-SC-SECRET) FS-RESOURCE=$(kvuri FP-SC-RESOURCE))
		else
			stepresult=$(az functionapp config appsettings set --name $faname --resource-group $resourceGroupName --settings FBI-STORAGEACCT=$(kvuri FBI-STORAGEACCT) FS-URL=$fsurl FS-TENANT-NAME=$(kvuri FS-TENANT-NAME) FS-CLIENT-ID=$(kvuri FS-CLIENT-ID) FS-SECRET=$(kvuri FS-SECRET) FS-RESOURCE=$(kvuri FS-RESOURCE))
		fi
		echo "Deploying FHIR Loader App from source repo to ["$fahost"]..."
		stepresult=$(retry az functionapp deployment source config --branch master --manual-integration --name $faname --repo-url https://github.com/sordahl-ga/FHIRBulkImport --resource-group $resourceGroupName)
		echo "Creating Azure Event GridSubscriptions..."
		storesourceid="/subscriptions/"$subscriptionId"/resourceGroups/"$resourceGroupName"/providers/Microsoft.Storage/storageAccounts/"$deployprefix$storageAccountNameSuffix
		egndjsonresource=$faresourceid"/functions/NDJSONConverter"
		egbundleresource=$faresourceid"/functions/ImportFHIRBundles"
		stepresult=$(az eventgrid event-subscription create --name ndjsoncreated --source-resource-id $storesourceid --endpoint $egndjsonresource --endpoint-type azurefunction  --subject-ends-with .ndjson --advanced-filter data.api stringin CopyBlob PutBlob PutBlockList FlushWithClose) 
		stepresult=$(az eventgrid event-subscription create --name bundlecreated --source-resource-id $storesourceid --endpoint $egbundleresource --endpoint-type azurefunction  --subject-ends-with .json --advanced-filter data.api stringin CopyBlob PutBlob PutBlockList FlushWithClose) 
		echo " "
		echo "************************************************************************************************************"
		echo "FHIR Loader has successfully been deployed to group "$resourceGroupName" on "$(date)
		echo "Please note the following reference information for future use:"
		echo "Your FHIRLoader URL is: "$fahost
		echo "Your FHIRLoader Function App Key is: "$fakey
		echo "Your FHIRLoader Storage Account name is: "$deployprefix$storageAccountNameSuffix
		echo "************************************************************************************************************"
		echo " "
)