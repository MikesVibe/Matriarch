# Matriarch

A .NET Blazor web application for viewing Azure role assignments and security relationships in real-time.

## Projects

### Matriarch.Web
A Blazor Server web application that provides an interactive UI for viewing role assignments, API permissions, and security group memberships for Azure identities. The application queries Azure Entra ID and Azure Resource Manager in real-time to display current information.

### Matriarch.Tests
Unit and integration tests for the Matriarch solution.

## Features

- **Real-time Azure Identity Lookup:**
  - Search by email, Object ID, Application ID, or display name
  - Auto-detection of identity type (User, Group, Service Principal, Managed Identity)
  - Support for both System-Assigned and User-Assigned Managed Identities

- **Comprehensive Identity Information:**
  - Direct RBAC role assignments from Azure Resource Graph
  - Security group memberships (both direct and indirect)
  - Role assignments inherited through group memberships
  - API permissions for Service Principals and Managed Identities
  - Managed Identity resource details (Subscription, Resource Group)

- **Interactive Web Interface:**
  - Clean, modern UI built with Blazor Server
  - Real-time data queries from Azure
  - Support for multiple identity search results
  - Detailed role assignment scope information
  - Optional parallel processing for faster group hierarchy traversal

## Prerequisites

- .NET 10 SDK
- Azure Service Principal or App Registration with the following permissions:
  - **Microsoft Graph API:**
    - `Application.Read.All` - Read all applications and service principals
    - `Directory.Read.All` - Read directory data
    - `GroupMember.Read.All` - Read group memberships
  - **Azure RBAC:**
    - Reader role at the subscription or management group level to query role assignments

## Configuration

### Authentication Setup

The application now requires Azure AD authentication for user access. You need to set up two types of Azure App Registrations:

1. **User Authentication App Registration** - For SSO login
2. **Service Principal(s)** - For querying Azure resources (one per tenant)

#### 1. User Authentication App Registration

Create an App Registration in your primary Azure AD tenant (e.g., ComReg):

1. Go to Azure Portal → Azure Active Directory → App registrations → New registration
2. Name: `Matriarch-Web-Auth` (or your preferred name)
3. Supported account types: Choose based on your needs (single tenant or multi-tenant)
4. Redirect URI: 
   - Type: Web
   - URI: `https://localhost:5001/signin-oidc` (add production URL when deployed)
5. After creation, note the:
   - Application (client) ID
   - Directory (tenant) ID
6. Under "Certificates & secrets", create a client secret (optional, not needed for authentication flow)

#### 2. Service Principal App Registrations

For each Azure tenant you want to query (ComReg, ComProd, etc.), create separate App Registrations with the required API permissions:

- **Microsoft Graph API:**
  - `Application.Read.All` - Read all applications and service principals
  - `Directory.Read.All` - Read directory data
  - `GroupMember.Read.All` - Read group memberships
- **Azure RBAC:**
  - Reader role at the subscription or management group level

### Configuration File

Create an `appsettings.json` file in the `Matriarch.Web` directory:

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "your-comreg-tenant-id-here",
    "ClientId": "your-auth-app-client-id-here",
    "CallbackPath": "/signin-oidc"
  },
  "Azure": {
    "ComReg": {
      "TenantId": "your-comreg-tenant-id",
      "SubscriptionId": "your-comreg-subscription-id",
      "ClientId": "your-comreg-service-principal-client-id",
      "ClientSecret": "your-comreg-service-principal-secret"
    },
    "ComProd": {
      "TenantId": "your-comprod-tenant-id",
      "SubscriptionId": "your-comprod-subscription-id",
      "ClientId": "your-comprod-service-principal-client-id",
      "ClientSecret": "your-comprod-service-principal-secret"
    }
  },
  "Parallelization": {
    "MaxDegreeOfParallelism": 4,
    "MaxRetryAttempts": 3,
    "RetryDelayMilliseconds": 1000
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### Tenant Access Control

The application automatically filters available tenants based on user access:
- When a user signs in, the application checks which configured tenants contain their user account
- Users only see and can select tenants where their account exists
- This ensures users can only query resources in tenants they have access to

### Parallelization Settings

The `Parallelization` section controls how the application processes group hierarchies:

- **EnableParallelProcessing** (default: `false`): When enabled in configuration, allows users to toggle parallel processing in the UI. Parallel processing can significantly improve performance when dealing with large group hierarchies.
- **MaxDegreeOfParallelism** (default: `4`): The maximum number of concurrent threads to use when parallel processing is enabled. Adjust based on your system resources and Azure API throttling limits.
- **MaxRetryAttempts** (default: `3`): The number of retry attempts when Azure API throttling (429) or service unavailable (503) errors occur.
- **RetryDelayMilliseconds** (default: `1000`): The initial delay in milliseconds before retrying. Uses exponential backoff for subsequent retries.

**Note:** Even when `EnableParallelProcessing` is set to `false` in configuration, users can still enable parallel processing via the UI toggle. The configuration setting only affects the default behavior.

Alternatively, you can use environment variables:
- Authentication:
  - `AzureAd__TenantId`
  - `AzureAd__ClientId`
- Service Principals:
  - `Azure__ComReg__TenantId`
  - `Azure__ComReg__SubscriptionId`
  - `Azure__ComReg__ClientId`
  - `Azure__ComReg__ClientSecret`
  - (similar pattern for other tenants)

## Building

Build the solution:
```bash
dotnet build Matriarch.sln
```

Or build the web application directly:
```bash
cd Matriarch.Web
dotnet build
```

## Running

### Matriarch.Web (Blazor Application)

Using .NET CLI:
```bash
cd Matriarch.Web
dotnet run
```

Then open your browser and navigate to the URL shown in the console (typically `http://localhost:5000` or `https://localhost:5001`).

**Using the Application:**
1. **Sign In**: Click "Sign in with Microsoft" on the login page
   - You will be redirected to Azure AD login
   - Sign in with your Azure account that has access to the configured tenants
2. **Select Tenant**: Choose the Azure tenant you want to query from the dropdown (only tenants where your account exists will be shown)
3. Enter an identity search term in the input field:
   - Email address (e.g., `user@example.com`)
   - Object ID (GUID format)
   - Application ID / Client ID (GUID format)
   - Display name (e.g., `John Doe` or `MyApp`)
4. (Optional) Enable "Parallel Processing" checkbox for faster processing of large group hierarchies
5. Click "Load Role Assignments"
6. If multiple identities match your search, select the correct one from the table
7. View the comprehensive results:
   - Identity type and details (including Managed Identity resource information)
   - Direct role assignments
   - Group memberships (direct and indirect)
   - Role assignments inherited through groups
   - API permissions (for Service Principals and Managed Identities)
   - Processing time metrics (showing performance difference between parallel and sequential processing)

## Supported Identity Types

The application supports querying and displaying information for:

- **Users**: Azure AD/Entra ID users
- **Security Groups**: Azure AD/Entra ID security-enabled groups
- **Service Principals**: Azure AD App Registrations with Enterprise Applications
- **Managed Identities**:
  - **System-Assigned Managed Identities**: Automatically created and tied to an Azure resource
  - **User-Assigned Managed Identities**: Standalone Azure resources that can be assigned to multiple Azure resources

For Managed Identities, the application displays:
- Subscription ID
- Resource Group
- The distinction between System-Assigned and User-Assigned types

## Architecture

The application uses:
- **Blazor Server** for the interactive web UI
- **Microsoft Graph SDK** for querying Entra ID (users, groups, service principals, applications)
- **Azure Resource Graph SDK** for querying RBAC role assignments across Azure subscriptions
- **Azure.Identity** for authentication with Azure services

All data is queried in real-time from Azure - there is no local database or caching.

## License

MIT