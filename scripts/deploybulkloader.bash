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
#  Script Variables 
#########################################

declare defsubscriptionId=""
declare subscriptionId=""
declare resourceGroupName=""
declare resourceGroupLocation=""
declare serviceplanSuffix="asp"
declare faname="sfload"$RANDOM
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
declare createkv=""


#########################################
#  Script Functions 
#########################################


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

function function_create_keyvault () {
	echo "Creating Keyvault "$kvname"..."

}

function intro {
	# Display the intro - give the user a chance to cancel 
	#
	echo " "
	echo "FHIR-Loader Application installation script... "
	echo " - Prerequisite:  Azure API for FHIR of FHIR Server must be installed"
	echo " - Prerequisite:  Client Application connection information for FHIR Service"
	echo " - Prerequisite:  A Keyvault service"
	echo " "
	echo "Note: You must have rights to able to provision resources within the Subscription scope"
	echo " "
	read -p 'Press Enter to continue, or Ctrl+C to exit'
}


usage() { echo "Usage: $0 -i <subscriptionId> -g <resourceGroupName> -l <resourceGroupLocation> -p <prefix> -k <keyvault> -y (use FHIR Proxy)" 1>&2; exit 1; }



#########################################
#  Script Main Body (start here) 
#########################################
#
# Initialize parameters specified from command line
#
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

# Call the intro function - give the user a chance to exit 
#
intro

echo " "
echo "Begin collecting Script Parameters not supplied"
echo " "


#Prompt for parameters is some required parameters are missing
#
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
	echo "Enter a resource group name"
	read resourceGroupName
	[[ "${resourceGroupName:?}" ]]
fi

if [[ -z "$resourceGroupLocation" ]]; then
	echo "If creating a *new* resource group, you need to set a location "
	echo "You can lookup locations with the CLI using: az account list-locations "
	
	echo "Enter resource group location:"
	read resourceGroupLocation
fi

# Prompt for acript parameters if some required parameters are missing
#

# Set Default Deployment Prefix
#
defdeployprefix=${resourceGroupName:0:14}
defdeployprefix=${defdeployprefix//[^[:alnum:]]/}
defdeployprefix=${defdeployprefix,,}

# Prompt for Deployment Prefix
#
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


# Prompt for Keyvault 
#
if [[ -z "$kvname" ]]; then
	echo "Enter keyvault name to used with FHIR-Loader"
	read kvname
fi


# Ensure there are subscriptionId, resourcegroupnames and keyvault values  
#
if [ -z "$subscriptionId" ] || [ -z "$resourceGroupName" ] || [ -z "$kvname" ]; then
	echo "Either one of subscriptionId, resourceGroupName or keyvault is empty"
	usage
fi

# set the default subscription id
#
echo " "
echo "Setting subscription id..."
az account set --subscription $subscriptionId

# Begin validation checks 
#
echo " "
echo "Checking resource group..."

# Check for existing RG
#
set +e
if [ $(az group exists --name $resourceGroupName) = false ]; then
	echo "Resource group with name" $resourceGroupName "could not be found. Creating new resource group.."
	set -e
	(
		set -x
		az group create --name $resourceGroupName --location $resourceGroupLocation 1> /dev/null
	) ;
else
	echo "...Found, using existing resource group "$resourceGroupName
fi

# Check for Keyvault
#
echo " "
echo "Checking for keyvault "$kvname"..."
kvexists=$(az keyvault list --query "[?name == '$kvname'].name" --out tsv)
if [[ -n "$kvexists" ]]; then
	echo "...Found, using existing keyvault "$kvname
	echo " "
	echo "Checking "$kvname" for FHIR Service and/or FHIR-Proxy settings"
	fphost=$(az keyvault secret show --vault-name $kvname --name FP-HOST --query "value" --out tsv)
	if [ -n "$fphost" ]; then
		echo "...found FHIR-Proxy host "$fphost 
	fi
	fsurl=$(az keyvault secret show --vault-name $kvname --name FS-URL --query "value" --out tsv)
	if [ -n "$fsurl" ]; then
		echo "...found FHIR-Service URL "$fsurl
	fi
fi

# Keyvault does not exist, create it
#
if [[ -z "$kvexists" ]]; then
	echo "Keyvault "$kvname" does not exist, would you like to create it? [yes/no]"
	read createkv
	if [[ "$createkv" == "yes" ]]; then
		echo "Creating Keyvault "$kvname"..."
		az keyvault create --name $kvname --resource-group $resourceGroupName --location $resourceGroupLocation
		az keyvault set-policy --vault-name $kvname --object-id $kvname --permissions-deployment-admin "get,set,delete,backup,restore,list"
	fi
fi

# Sanity check Keyvault - and store settings if the keyvault is created
#
kvexists=$(az keyvault list --query "[?name == '$kvname'].name" --out tsv)
if [[ -z "$kvexists" ]]; then
	echo "Therer was a problem creating "$kvname" please check permissions and try again"
	exit 1 
fi

#############################################
#  Setup FHIR Service details 
#############################################
#
(
	# if not already set (see fsurl above), prompt for FHIR Service information 
	#
	if [[ -z "$fsurl" ]]; then
		echo "FHIR Service settings were not found in "$kvname" please provide the following information..."
		
		echo "Enter the FHIR Server URL (aka Endpoint):"
		read fsurl
		if [ -z "$fsurl" ] ; then
			echo "You must provide a destination FHIR Server URL"
			exit 1
		fi
		
		echo "Enter the Tenant ID of the FHIR Server Service Client (used to connect to the FHIR Service)."
		read fstenant
		if [ -z "$fstenant" ] ; then
			echo "You must provide a tenant ID"
			exit 1
		fi

		echo "Enter the FHIR Server - Service Client Application ID (used to connecto to the FHIR Service):"
		read fsclientid
		if [ -z "$fsclientid" ] ; then
			echo "You must provide a Client ID"
			exit 1 
		fi

		echo "Enter the FHIR Server - Service Client Secret (used to connect to the FHIR Service)."
		read fssecret ;
		if [ -z "$fssecret" ] ; then
			echo "You must provide a Client Secret"
			exit 1
		fi

		echo "Enter the FHIR Server - Service Client Audience/Resource (FHIR Service URL) ["$fsurl"]:"
		read fsaud
		if [ -z "$fsaud" ] ; then
			fsaud=$fsurl
		fi
		[[ "${fsaud:?}" ]]

		# Storing the FHIR Service information in the Keyvault
		#
		echo "Storing FHIR Service information in Keyvault "$kvname"..."
		echo "...storing FS-URL"
		stepresult=$(az keyvault secret set --vault-name $kvname --name "FS-URL" --value $fsurl)
		
		echo "...storing FS-TENANT-NAME"
		stepresult=$(az keyvault secret set --vault-name $kvname --name "FS-TENANT-NAME" --value $fstenant)
		
		echo "...storing FS-CLIENT-ID"
		stepresult=$(az keyvault secret set --vault-name $kvname --name "FS-CLIENT-ID" --value $fsclientid)
		
		echo "...storing FS-SECRET"
		stepresult=$(az keyvault secret set --vault-name $kvname --name "FS-SECRET" --value $fssecret)
		
		echo "...storing FS-RESURCE"
		stepresult=$(az keyvault secret set --vault-name $kvname --name "FS-RESOURCE" --value $fsaud)
	fi

)


# Check for FHIR-Proxy details (see useproxy above) 
#
echo " "
if [ -n "$fphost" ]; then
	echo "FHIR-Proxy ("$fphost") settings were found in "$kvname
	echo "would you like to use FHIR-Proxy [yes/no]? "
	read useproxy
	if [[ "$useproxy" == "yes" ]]; then
		fsurl="https://"$fphost"/fhir" 
		echo "FHIR Service URL is set to FHIR-Proxy "$fsurl"..." ;
	else 
		useproxy="no"
		echo "FHIR Service URL is set to "$fsurl"..." 
	fi
fi



#
echo " "
echo "Starting deployment of... "$0 
echo "              -i" $subscriptionId 
echo "              -g" $resourceGroupName 
echo "              -l" $resourceGroupLocation 
echo "              -p" $deployprefix  
echo "use FHIR-Proxy = "$useproxy
echo " "
read -p 'Press Enter to continue, or Ctrl+C to exit'

#############################################
#  Start FHIR-Proxy Configuration / Updates 
#############################################
#

set -e
#Start deployment
echo "Starting FHIR Loader deployment..."
(
	# Create Storage Account
	echo "Creating Storage Account ["$deployprefix$storageAccountNameSuffix"]..."
	stepresult=$(az storage account create --name $deployprefix$storageAccountNameSuffix --resource-group $resourceGroupName --location  $resourceGroupLocation --sku Standard_LRS --encryption-services blob)

	echo "Retrieving Storage Account Connection String..."
	storageConnectionString=$(az storage account show-connection-string -g $resourceGroupName -n $deployprefix$storageAccountNameSuffix --query "connectionString" --out tsv)
	
	stepresult=$(az keyvault secret set --vault-name $kvname --name "FBI-STORAGEACCT" --value $storageConnectionString)
	
	echo "Creating import containers..."
	stepresult=$(az storage container create -n bundles --connection-string $storageConnectionString)
	stepresult=$(az storage container create -n ndjson --connection-string $storageConnectionString)
	
	#---

	# Create Service Plan
	echo "Creating FHIR Loader Function App Serviceplan ["$deployprefix$serviceplanSuffix"]..."
	stepresult=$(az appservice plan create -g  $resourceGroupName -n $deployprefix$serviceplanSuffix --number-of-workers 2 --sku B1)
	
	# Create the function app
	echo "Creating FHIR Loader Function App ["$faname"]..."
	fahost=$(az functionapp create --name $faname --storage-account $deployprefix$storageAccountNameSuffix  --plan $deployprefix$serviceplanSuffix  --resource-group $resourceGroupName --runtime dotnet --os-type Windows --functions-version 3 --query defaultHostName --output tsv)
	
	# Setup Auth 
	echo "Creating MSI for FHIR Loader Function App..."
	msi=$(az functionapp identity assign -g $resourceGroupName -n $faname --query "principalId" --out tsv)
	
	# Setup Keyvault Access 
	echo "Setting KeyVault Policy to allow secret access for FHIR Loader App..."
	stepresult=$(az keyvault set-policy -n $kvname --secret-permissions list get set --object-id $msi)
	
	# Obtain Function Application Key 
	echo "Retrieving FHIR Loader Function App Host Key..."
	faresourceid="/subscriptions/"$subscriptionId"/resourceGroups/"$resourceGroupName"/providers/Microsoft.Web/sites/"$faname
	fakey=$(retry az rest --method post --uri "https://management.azure.com"$faresourceid"/host/default/listKeys?api-version=2018-02-01" --query "functionKeys.default" --output tsv)
	
	# Apply App settings 
	echo "Configuring FHIR Loader App ["$faname"]..."
	if [[ "$useproxy" == "yes" ]]; then
		stepresult=$(az functionapp config appsettings set --name $faname --resource-group $resourceGroupName --settings FBI-STORAGEACCT=$(kvuri FBI-STORAGEACCT) FS-URL=$fsurl FS-TENANT-NAME=$(kvuri FP-SC-TENANT-NAME) FS-CLIENT-ID=$(kvuri FP-SC-CLIENT-ID) FS-SECRET=$(kvuri FP-SC-SECRET) FS-RESOURCE=$(kvuri FP-SC-RESOURCE)) ;
	else
		stepresult=$(az functionapp config appsettings set --name $faname --resource-group $resourceGroupName --settings FBI-STORAGEACCT=$(kvuri FBI-STORAGEACCT) FS-URL=$fsurl FS-TENANT-NAME=$(kvuri FS-TENANT-NAME) FS-CLIENT-ID=$(kvuri FS-CLIENT-ID) FS-SECRET=$(kvuri FS-SECRET) FS-RESOURCE=$(kvuri FS-RESOURCE))
	fi
	
	# Deploy Function Application code
	echo "Deploying FHIR Loader App from source repo to ["$fahost"]..."
	stepresult=$(retry az functionapp deployment source config --branch main --manual-integration --name $faname --repo-url https://github.com/microsoft/fhir-loader --resource-group $resourceGroupName)
	
	#---

	# Creating Event Grid Subscription 
	echo "Creating Azure Event GridSubscriptions..."
	storesourceid="/subscriptions/"$subscriptionId"/resourceGroups/"$resourceGroupName"/providers/Microsoft.Storage/storageAccounts/"$deployprefix$storageAccountNameSuffix
	
	egndjsonresource=$faresourceid"/functions/NDJSONConverter"
	
	egbundleresource=$faresourceid"/functions/ImportFHIRBundles"
	
	stepresult=$(az eventgrid event-subscription create --name ndjsoncreated --source-resource-id $storesourceid --endpoint $egndjsonresource --endpoint-type azurefunction  --subject-ends-with .ndjson --advanced-filter data.api stringin CopyBlob PutBlob PutBlockList FlushWithClose) 
	
	stepresult=$(az eventgrid event-subscription create --name bundlecreated --source-resource-id $storesourceid --endpoint $egbundleresource --endpoint-type azurefunction  --subject-ends-with .json --advanced-filter data.api stringin CopyBlob PutBlob PutBlockList FlushWithClose) 

	#---

	echo " "
	echo "**************************************************************************************"
	echo "FHIR Loader has successfully been deployed to group "$resourceGroupName" on "$(date)
	echo "Please note the following reference information for future use:"
	echo "Your FHIRLoader URL is: "$fahost
	echo "Your FHIRLoader Function App Key is: "$fakey
	echo "Your FHIRLoader Storage Account name is: "$deployprefix$storageAccountNameSuffix
	echo "***************************************************************************************"
	echo " "

)

if [ $?  != 0 ] ; then
	echo "FHIR-Loader deployment had errors. Consider deleting the resources and trying again..."
fi