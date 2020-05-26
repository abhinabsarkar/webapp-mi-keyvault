# Azure Key Vault, Web App & Managed Identity
A common challenge when building cloud applications is to manage the credentials in the application code for authenticating to cloud services. Keeping the credentials secure is an important task and ideally, the credentials should never appear on developer workstations and neither they should be checked into source control. Azure Key Vault provides a way to securely store credentials, secrets, and other keys, but the code has to authenticate to Key Vault to retrieve them. This brings us back to square one.

The managed identities for Azure resources feature in Azure Active Directory (Azure AD) solves this problem. The feature provides Azure services with an automatically managed identity in Azure AD. One can use the identity to authenticate to any service that supports Azure AD authentication, including Key Vault, without any credentials in the application code.

Lets see this in action.

## Login to Azure & create a resource group
```bash
# Create resource group
rgName="rg-aks-private"
az group create -g $rgName -l eastus2
```
## Set up key vault
```bash
# create key vault
kvName="kv-abs"
az keyvault create --name $kvName -g $rgName --verbose
# Note down the key vault uri --> "vaultUri": "https://kv-abs.vault.azure.net/"

# Place a secret in the key vault
az keyvault secret set --vault-name $kvName --name "db-credentials" --value "abs-secret"
```

If the key vault url is different, update the same in the dotnet core application.

## Create a dotnet core app & deploy on a Web App
The sample dotnet core application is placed [here](/src/akvaspnetapp).  
The application tries to access key vault created above & fetch the secret stored using the system assigned managed identity of Azure WebApp. 
```bash
# Download & build dotnet app locally. This sample is using dotnet core version 3.1.300
cd akvwebapp
dotnet build
dotnet run

# Add the web app code to local git repository
git init
git add .
git commit -m "first commit"

# create a web app plan
aspName=absAppServicePlan
az appservice plan create --name $aspName --resource-group $rgName --sku FREE

# Create web app
waName=wa-abs-kv
az webapp create --resource-group $rgName --plan $aspName --name $waName --deployment-local-git
# Store local git url in azure & web app name in variables 
localGitUrlAzure="https://wa-abs-kv.scm.azurewebsites.net/wa-abs-kv.git"
webAppHostName="wa-abs-kv.azurewebsites.net"

# Deploy the local app
# add an Azure remote to your local Git repository
git remote add localGitUrlAzure $localGitUrlAzure
# Get bearer token of Service Principal/Logged in User
az account get-access-token --resource https://management.core.windows.net/
# Store the access token in a variable
accessToken="ey..."
# Push to the local Git repository in Azure Web App Deployment Center
git -c http.extraheader="Authorization: Bearer $accessToken"  push $localGitUrlAzure master
```

Browse the application by typing the url - http://wa-abs-kv.azurewebsites.net/keyvault. It should show the below page.

![Alt text](/images/mi-no-access-keyvault.jpg)

## Create and assign a managed identity
The web app will access the Azure Key Vault using system assigned managed identity which is created for the WebApp. Once the managed identity is given access to perform Get & List operations on the secrets stored in Key Vault, the Web App will be able to fetch the secrets.
```bash
# Create a system-assigned managed identity for webapp
az webapp identity assign --name $waName --resource-group $rgName
# Store the principalId of the identity
principalId="xxxx"
# On the key vault, give the webapp's identity access to do get & list operations
az keyvault set-policy --name $kvName --object-id $principalId --secret-permissions get list
```

Since the logic to access the key vault is in the StartUp.cs file, the Azure Web App has to be restarted
```bash
az webapp restart -g $rgName -n $waName
```
Browse the application by typing the url - http://wa-abs-kv.azurewebsites.net/keyvault. It should show the below page.

![Alt text](/images/mi-access-keyvault.jpg)

## Delete resources
```bash
# Delete the web app & the plan
az webapp delete --resource-group $rgName --name wa-abs-kv
az appservice plan delete --name absAppServicePlan --resource-group $rgName --yes
# Delete the key vault
az keyvault delete --name $kvName -g $rgName
```

# References
* [Managed identities for Azure resources](https://docs.microsoft.com/en-us/azure/active-directory/managed-identities-azure-resources/overview)
* [Using managed identity to connect Key Vault to an Azure Web App](https://docs.microsoft.com/en-us/azure/key-vault/general/tutorial-net-create-vault-azure-web-app)
* [Service principal to perform git operation](https://github.com/projectkudu/kudu/wiki/Using-Service-Principal-to-perform-git-operation)