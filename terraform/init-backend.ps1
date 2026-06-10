param(
    [string]$SubscriptionId = (az account show --query id -o tsv),
    [string]$Location       = "polandcentral",
    [string]$ResourceGroup  = "rg-misty-tfstate",
    [string]$StorageAccount = "stmistytfstate",
    [string]$Container      = "tfstate"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "Using subscription: $SubscriptionId"
az account set --subscription $SubscriptionId

Write-Host "Creating resource group '$ResourceGroup'..."
az group create --name $ResourceGroup --location $Location --output none

Write-Host "Creating storage account '$StorageAccount'..."
az storage account create `
    --name $StorageAccount `
    --resource-group $ResourceGroup `
    --location $Location `
    --sku Standard_LRS `
    --kind StorageV2 `
    --allow-blob-public-access false `
    --output none

Write-Host "Creating blob container '$Container'..."
az storage container create `
    --name $Container `
    --account-name $StorageAccount `
    --auth-mode login `
    --output none

Write-Host "Done. Run 'terraform init' from the terraform/ directory next."
