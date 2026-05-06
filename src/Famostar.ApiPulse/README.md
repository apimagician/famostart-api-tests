# Famostar API Pulse

A .NET 9 ASP.NET Core background service that executes HTTP API requests from `.http` files on a configurable interval (default: 1 minute).

## Overview

**API Pulse** is designed to run in Azure App Service and:
- Parses `.http` files (REST Client format)
- Executes requests sequentially at regular intervals
- Manages state across requests (e.g., authentication tokens)
- Logs all requests and responses
- Provides a health check endpoint

## Features

- **HTTP File Parser**: Understands `.http` file syntax with headers, bodies, and variables
- **Variable Management**: Extracts tokens/data from responses and uses them in subsequent requests
- **Configurable Interval**: Set execution frequency via configuration (default 60 seconds)
- **Azure App Service Ready**: Includes health check endpoint and structured logging
- **Secure**: Credentials loaded from configuration/environment variables, not hardcoded

## Project Structure

```
src/Famostar.ApiPulse/
├── Program.cs                          # Application entry point
├── appsettings.json                    # Production configuration
├── appsettings.Development.json        # Development configuration
├── Models/
│   └── HttpRequestModel.cs             # HTTP request data model
└── Services/
    ├── HttpFileParser.cs               # .http file parser
    └── ApiPulseBackgroundService.cs    # Background execution service
```

## Configuration

### appsettings.json

```json
{
  "ApiPulse": {
    "HttpFilePath": "../api-gateways.http",  // Relative path to .http file
    "IntervalSeconds": 60                     // Execution interval in seconds
  },
  "Environment": {
    "baseUrl": "https://api.example.com",
    "FAMOSTAR_USERNAME": "",                  // Set via environment variables
    "FAMOSTAR_PASSWORD": ""                   // Set via environment variables
  }
}
```

### Environment Variables (for Azure App Service)

Set these in your Azure App Service configuration:

```
ApiPulse__HttpFilePath=../api-gateways.http
ApiPulse__IntervalSeconds=60
Environment__FAMOSTAR_USERNAME=your_username
Environment__FAMOSTAR_PASSWORD=your_password
Environment__baseUrl=https://www.smartscan.nl
```

Note: Use double underscores (`__`) for nested configuration in environment variables.

## .http File Format

The app parses standard REST Client `.http` format:

```http
@baseUrl=https://api.example.com
@auth_token=

###
# @name Get Bearer Token
POST {{baseUrl}}/api/v1/login
Content-Type: application/json

{
    "username": "{{FAMOSTAR_USERNAME}}",
    "password": "{{FAMOSTAR_PASSWORD}}"
}

###
# @name Get Data
GET {{baseUrl}}/api/v1/data
Authorization: Bearer {{auth_token}}
```

**Features:**
- `###` separates requests
- `@name` adds a descriptive name
- `{{variable}}` placeholders are replaced with values from configuration
- JSON response properties like `token` are automatically extracted and stored
- Headers, methods, bodies all supported

## Local Development

### Prerequisites
- .NET 9 SDK
- Visual Studio Code or Visual Studio

### Run Locally

```bash
cd src/Famostar.ApiPulse
dotnet run
```

The app will:
1. Start on `https://localhost:5001` (HTTPS) or `http://localhost:5000` (HTTP)
2. Begin executing requests from `api-gateways.http` every 60 seconds
3. Log all requests and responses to console

### Health Check

Test the health endpoint:

```bash
curl http://localhost:5000/health
```

Response:
```json
{"status":"healthy","timestamp":"2025-05-06T10:30:45.1234567Z"}
```

## Deployment to Azure App Service

### 1. Publish the App

```bash
cd src/Famostar.ApiPulse
dotnet publish -c Release -o ./publish
```

### 2. Using Azure CLI

```bash
# Create App Service Plan (if needed)
az appservice plan create --name MyPlan --resource-group MyResourceGroup --sku B2 --is-linux

# Create Web App
az webapp create --resource-group MyResourceGroup --plan MyPlan --name MyApiPulseApp --runtime "DOTNET|9.0"

# Deploy
az webapp deployment source config-zip --resource-group MyResourceGroup --name MyApiPulseApp --src ./publish.zip

# Configure Environment Variables
az webapp config appsettings set --resource-group MyResourceGroup --name MyApiPulseApp --settings \
  Environment__FAMOSTAR_USERNAME="your_username" \
  Environment__FAMOSTAR_PASSWORD="your_password" \
  ApiPulse__IntervalSeconds="60"
```

### 3. Using Docker (Recommended for App Service)

A Dockerfile is provided for containerized deployment:

```bash
# Build image
docker build -t famostar-api-pulse:latest .

# Run locally
docker run -p 8080:8080 \
  -e Environment__FAMOSTAR_USERNAME=your_username \
  -e Environment__FAMOSTAR_PASSWORD=your_password \
  famostar-api-pulse:latest
```

Then push to Azure Container Registry and configure App Service to use the image.

## Logging

The application logs to console with the following levels:
- **Information**: Request execution, parsed requests, extracted variables
- **Warning**: Missing files, parse errors
- **Error**: HTTP failures, JSON parsing errors

Example log output:
```
info: Famostar.ApiPulse.Services.ApiPulseBackgroundService[0]
      Starting execution of 2 HTTP requests at 2025-05-06T10:30:00.0000000Z
info: Famostar.ApiPulse.Services.ApiPulseBackgroundService[0]
      Executing POST https://www.smartscan.nl/en/api/v1/login_check - Get Bearer Token
info: Famostar.ApiPulse.Services.ApiPulseBackgroundService[0]
      Response: OK for Get Bearer Token
```

## Troubleshooting

### Requests not executing
- Check the configured `HttpFilePath` is correct
- Verify the `.http` file exists and contains `###` separators
- Check logs for parse errors

### 401/403 Errors
- Verify credentials in environment variables
- Confirm the token extraction is working (check logs for "Extracted auth_token")
- Token may have expired; the app will get a new one on the next cycle

### High error rates
- Verify the API endpoint is accessible from App Service
- Check network/firewall rules
- Increase `IntervalSeconds` to reduce load if hitting rate limits

## Security Considerations

- **Never commit credentials** to the repository
- Use Azure Key Vault to store sensitive values in production
- The app can be easily extended to fetch secrets from Key Vault at startup
- Rotate credentials regularly
- Use HTTPS only for API endpoints
- Monitor logs for authentication failures

## Next Steps / Enhancement Ideas

1. **Add Azure Key Vault integration** for secret management
2. **Implement Application Insights** for monitoring and alerting
3. **Add support for request ordering** with dependencies
4. **Implement retry logic** with exponential backoff
5. **Add request validation** before execution
6. **Stream large response bodies** to avoid memory issues
7. **Support for multiple .http files** or glob patterns
8. **Webhook notifications** on failures

## Support

For issues or questions, contact the Famostar team.
