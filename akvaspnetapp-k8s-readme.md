# Azure Key Vault, Pod Identity & AKS

## Create Azure resources
### Create AKS cluster
Create an AKS cluster. In this case, the cluster is created in a Resource Group named *rg-aksAkv-demo*. The steps not shown here is create a Service Principal and assign it *Contributor* role scoped to the Resource Group created above.
```bash
spAppId=""
spObjectId=""
spSecret=""
subscriptionId=""
rgName="rg-aksAkv-demo"
# Create an aks cluster
az aks create --name aks-abs-demo \
    --resource-group $rgName --node-count 1 \
    --service-principal $spAppId --client-secret $spSecret --subscription $subscriptionId \
    --generate-ssh-keys --verbose
# Get the AKS credentials
az aks get-credentials --resource-group $rgName --name aks-abs-demo --verbose
```

### Create a User Assigned Managed Identity on Azure & assign it the Roles
Create the user assigned managed identity
```bash
identityName="mi-akvaspnetapp"
az identity create -g $rgName -n $identityName --subscription $subscriptionId --verbose
# Store the identity client Id and resource Id
identityClientId="$(az identity show -g $rgName -n $identityName --subscription $subscriptionId --query clientId -o tsv)"
identityResourceId="$(az identity show -g $rgName -n $identityName --subscription $subscriptionId --query id -o tsv)"
principalId="$(az identity show -g $rgName -n $identityName --subscription $subscriptionId --query principalId -o tsv)"
```
Role assignments
* Assign *Reader* role to the *Managed Identity* scoped over *Resource Group*
* Assign *Managed Identity Operator* role to the *Service Principal* scoped over *Managed Identity*
```bash
# Assign reader role to the identity on the appropriate Resource Group, in this case on the node RG i.e. MC*
# Store the identity assignment id
nodeRgName="MC_rg-aksAkv-demo_aks-abs-demo_eastus"
nodeRgId="$(az group show -n $nodeRgName --query id -o tsv)"
identityAssignmentId="$(az role assignment create --role Reader --assignee $identityClientId --scope $nodeRgId --query id -o tsv)"

# Get the service principal id
spAppId=$(az aks show -g $rgName -n aks-abs-demo --query servicePrincipalProfile.clientId -o tsv)
# Assign Managed Identity Operator role to the service principal scoped over the Managed Identity
az role assignment create --role "Managed Identity Operator" --assignee $spAppId --scope $identityResourceId
```

## Create Azure Key Vault & provide Managed Identity access on it
Create Azure Key Vault
```bash
# create key vault
kvName="kv-abs"
az keyvault create --name $kvName -g $rgName --verbose
# Note down the key vault uri --> "vaultUri": "https://kv-abs.vault.azure.net/". This will be updated in the configmap when creating the kubernetes object in "akvaspnetapp.yaml"

# Place a secret in the key vault
az keyvault secret set --vault-name $kvName --name "db-credentials" --value "abs-secret"
```
Provide Managed Identity access for appropriate operations on Azure Key Vault
```bash
# On the key vault, give the managed identity access to do get & list operations
az keyvault set-policy --name $kvName --object-id $principalId --secret-permissions get list
```

## Configure Pod Identity & sample application
### 1. Deploy aad-pod-identity
```bash
# Deploy aad-pod-identity components to an RBAC-enabled cluster
# AAD Pod identity version used 1.6.1. The Pod Identity is scoped to "default" namespace
kubectl apply -f https://raw.githubusercontent.com/Azure/aad-pod-identity/master/deploy/infra/deployment-rbac.yaml
```

### 2. Deploy Azure Identity (Kubernetes Object)
Azure Identity is a kubernetes object which references the Managed Identity. A sample yaml file is shown below.
> type: 0 for user-assigned Managed Identity or type: 1 for Service Principal.
```bash
cat <<EOF | kubectl apply -f -
apiVersion: "aadpodidentity.k8s.io/v1"
kind: AzureIdentity
metadata:
  name: $identityName
spec:
  type: 0
  resourceID: $identityResourceId
  clientID: $identityClientId
EOF
```
It will create kubernetes object azureidentity.aadpodidentity.k8s.io/mi-akvaspnetapp

> **Pods can also be matched with a namespace**

### 3. Deploy AzureIdentityBinding
Create an AzureIdentityBinding kubernetes object that references the AzureIdentity created above. Sample yaml file is shown below
```bash
cat <<EOF | kubectl apply -f -
apiVersion: "aadpodidentity.k8s.io/v1"
kind: AzureIdentityBinding
metadata:
  name: $identityName-binding
spec:
  azureIdentity: $identityName
  selector: $identityName
EOF
```
This will create kubernetes object azureidentitybinding.aadpodidentity.k8s.io/mi-akvaspnetapp-binding

### 4. Deploy an App pod with AzureIdentityBinding
For an application pod to match an identity binding, it needs a label with the key *aadpodidbinding* whose value is that of the *selector:* field in the *AzureIdentityBinding*.
The complete yaml file can be found [here](/src/akvaspnetapp.yaml)
```yaml
name: akvaspnetapp
labels:
  app: akvaspnetapp
  aadpodidbinding: mi-akvaspnetapp
```
Before deploying the application pod, update the configmap to point to the Azure Key Vault Uri in [akvaspnetapp.yaml](/src/akvaspnetapp.yaml). The values to be updated is "VaultUri".
Deploy the application pod
```bash
# Deploy the application pod
kubectl apply -f akvaspnetapp.yaml
# Get the resources 
kubectl get all
```
The sample application deployed is accessing an Azure Key Vault under the context of Managed Identity. If the Managed Identity is configured, it returns the secret stored in the Azure Key Vault else it returns a message stating "Cannot access key vault". The dockerfile & application source code can be found [here.](https://github.com/abhinabsarkar/webapp-mi-keyvault/tree/master/src)

Test the application by browsing the External-IP of the service akvaspnetapp. If everything is configured correctly, then browsing the web page https://<External-IP>/keyvault should list the value stored in the Azure Key Vault.

![Alt text](/images/aks-mi-access-keyvault.jpg)

Change the configmap,  restart the pod
```bash
kubectl scale deployment akvaspnetapp --replicas=0
```
## Clean up the resources
```bash
# Delete the AKS cluster
az aks delete -g $rgName -n aks-abs-demo --yes
```

## References
* [AAD Pod Identity](https://github.com/Azure/aad-pod-identity)