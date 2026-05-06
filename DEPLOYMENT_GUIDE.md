# Deploying Famostar API Pulse to Azure App Service

This guide walks through deploying the API Pulse background service to Azure App Service.

## Prerequisites

- Azure subscription with an existing App Service (dotnetcore 9.0)
- Azure CLI installed (`az` command)
- Docker installed (for container-based deployment - recommended)
- .NET 9 SDK (for local publishing)

## Option 1: Direct Deployment (Using ZIP)

### Step 1: Publish the Application

```bash
cd src/Famostar.ApiPulse
dotnet publish -c Release -o ./publish
cd ./publish
Compress-Archive -Path * -DestinationPath ../api-pulse.zip -Force
cd ..
```

### Step 2: Deploy to App Service

```bash
# Variables
$resourceGroup = "your-resource-group"
$appServiceName = "your-app-service-name"
$zipPath = "api-pulse.zip"

# Deploy via ZIP
az webapp deployment source config-zip `
  --resource-group $resourceGroup `
  --name $appServiceName `
  --src $zipPath
```

### Step 3: Configure Environment Variables

```bash
# Set secrets in App Service
az webapp config appsettings set `
  --resource-group $resourceGroup `
  --name $appServiceName `
  --settings `
    Environment__FAMOSTAR_USERNAME="your_username" `
    Environment__FAMOSTAR_PASSWORD="your_password" `
    Environment__baseUrl="https://www.smartscan.nl" `
    ApiPulse__IntervalSeconds="60" `
    ApiPulse__HttpFilePath="api-gateways.http"
```

**Note:** The `api-gateways.http` file should be in the root of your App Service (or adjust the path as needed).

## Option 2: Docker Container Deployment (Recommended)

### Step 1: Build Docker Image

```bash
# From project root
docker build -f src/Famostar.ApiPulse/Dockerfile -t famostar-api-pulse:latest .
```

### Step 2: Tag and Push to Azure Container Registry

```bash
# Variables
$acrName = "your-acr-name"
$acrLoginServer = "$acrName.azurecr.io"
$imageName = "famostar-api-pulse"
$version = "latest"

# Login to ACR
az acr login --name $acrName

# Tag image
docker tag famostar-api-pulse:latest "$acrLoginServer/$imageName`:$version"

# Push to ACR
docker push "$acrLoginServer/$imageName`:$version"
```

### Step 3: Update App Service to Use Container

```bash
# Variables
$resourceGroup = "your-resource-group"
$appServiceName = "your-app-service-name"
$acrName = "your-acr-name"
$imageName = "famostar-api-pulse"
$version = "latest"

# Configure App Service to use the container image
az webapp config container set `
  --name $appServiceName `
  --resource-group $resourceGroup `
  --docker-custom-image-name "$acrName.azurecr.io/$imageName`:$version" `
  --docker-registry-server-url "https://$acrName.azurecr.io" `
  --docker-registry-server-user (az acr credential show -n $acrName --query username -o tsv) `
  --docker-registry-server-password (az acr credential show -n $acrName --query "passwords[0].value" -o tsv)
```

### Step 4: Configure Environment Variables

```bash
az webapp config appsettings set `
  --resource-group $resourceGroup `
  --name $appServiceName `
  --settings `
    WEBSITES_PORT="8080" `
    Environment__FAMOSTAR_USERNAME="your_username" `
    Environment__FAMOSTAR_PASSWORD="your_password" `
    Environment__baseUrl="https://www.smartscan.nl" `
    ApiPulse__IntervalSeconds="60"
```

## Step 4: Verify Deployment (Both Options)

### Check Health Endpoint

```bash
# Get App Service URL
$appUrl = az webapp show --resource-group $resourceGroup --name $appServiceName --query "defaultHostName" -o tsv

# Test health check
curl "https://$appUrl/health"
```

Expected response:
```json
{"status":"healthy","timestamp":"2025-05-06T10:30:00Z"}
```

### View Application Logs

```bash
# Stream live logs
az webapp log tail --resource-group $resourceGroup --name $appServiceName

# Or view recent logs
az webapp log download --resource-group $resourceGroup --name $appServiceName --log-file logs.zip
```

## Troubleshooting

### Container Won't Start

1. Check container logs:
   ```bash
   az webapp log tail --resource-group $resourceGroup --name $appServiceName
   ```

2. Verify environment variables are set
3. Ensure the listening port matches (8080 for containers)

### API Requests Failing

1. Check that credentials are correct
2. Verify network access from App Service to API endpoint
3. Check logs for specific error messages
4. Increase `IntervalSeconds` to reduce request frequency during testing

### Health Check Fails

1. Verify the app is running: `az webapp log tail ...`
2. Check that port 8080 (container) or app's default port is correctly configured
3. For direct ZIP deployment, ensure appsettings have correct paths

## Using Azure Key Vault for Secrets (Optional but Recommended)

For production deployments, store secrets in Azure Key Vault:

1. Create a Key Vault:
   ```bash
   az keyvault create --name MyKeyVault --resource-group $resourceGroup
   ```

2. Add secrets:
   ```bash
   az keyvault secret set --vault-name MyKeyVault --name FamostarUsername --value "your_username"
   az keyvault secret set --vault-name MyKeyVault --name FamostarPassword --value "your_password"
   ```

3. Grant App Service access (Managed Identity):
   ```bash
   # Enable Managed Identity
   az webapp identity assign --resource-group $resourceGroup --name $appServiceName
   
   # Get the principal ID
   $principalId = az webapp identity show --resource-group $resourceGroup --name $appServiceName --query principalId -o tsv
   
   # Grant Key Vault access
   az keyvault set-policy --name MyKeyVault --object-id $principalId --secret-permissions get list
   ```

4. Modify the app to fetch secrets from Key Vault (requires code update)

## Monitoring and Alerting

### Enable Application Insights

```bash
# Create Application Insights resource
az monitor app-insights component create `
  --app MyApiPulseInsights `
  --resource-group $resourceGroup `
  --application-type web

# Get instrumentation key
$instrumentation_key = az monitor app-insights component show `
  --app MyApiPulseInsights `
  --resource-group $resourceGroup `
  --query instrumentationKey -o tsv

# Add to App Service
az webapp config appsettings set `
  --resource-group $resourceGroup `
  --name $appServiceName `
  --settings APPINSIGHTS_INSTRUMENTATIONKEY=$instrumentation_key
```

### Set Up Alerts

Create alerts for high error rates, health check failures, etc. via Azure Portal.

## Cleanup

To remove resources:

```bash
# Stop the app service
az webapp stop --resource-group $resourceGroup --name $appServiceName

# Delete if needed
az webapp delete --resource-group $resourceGroup --name $appServiceName
```

## Summary

✅ Application deployed and running  
✅ Environment variables configured  
✅ Health endpoint responding  
✅ Background service executing requests every minute  

The API Pulse service is now running on your Azure App Service and will automatically execute your API requests according to the configured schedule.
