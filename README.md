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

> [!NOTE]
> Currently this application supports only connection using client credentials (client ID and client secret).

Create an `appsettings.json` file in the `Matriarch.Web` directory with your Azure credentials:

```json
{
  "Azure": {
    "Tenant1": {
      "TenantId": "your-tenant-id-here",
      "SubscriptionId": "your-subscription-id-here",
      "ClientId": "your-client-id-here",
      "ClientSecret": "your-client-secret-here"
    },
    "Tenant2": {
      "TenantId": "your-tenant-id-here",
      "SubscriptionId": "your-subscription-id-here",
      "ClientId": "your-client-id-here",
      "ClientSecret": "your-client-secret-here"
    }
  },
  "Parallelization": {
    "EnableParallelProcessing": false,
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

### Parallelization Settings

The `Parallelization` section controls how the application processes group hierarchies:

- **EnableParallelProcessing** (default: `false`): When enabled in configuration, allows users to toggle parallel processing in the UI. Parallel processing can significantly improve performance when dealing with large group hierarchies.
- **MaxDegreeOfParallelism** (default: `4`): The maximum number of concurrent threads to use when parallel processing is enabled. Adjust based on your system resources and Azure API throttling limits.
- **MaxRetryAttempts** (default: `3`): The number of retry attempts when Azure API throttling (429) or service unavailable (503) errors occur.
- **RetryDelayMilliseconds** (default: `1000`): The initial delay in milliseconds before retrying. Uses exponential backoff for subsequent retries.

**Note:** Even when `EnableParallelProcessing` is set to `false` in configuration, users can still enable parallel processing via the UI toggle. The configuration setting only affects the default behavior.

Alternatively, you can use environment variables:
- `Azure__TenantId`
- `Azure__SubscriptionId`
- `Azure__ClientId`
- `Azure__ClientSecret`

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
1. Enter an identity search term in the input field:
   - Email address (e.g., `user@example.com`)
   - Object ID (GUID format)
   - Application ID / Client ID (GUID format)
   - Display name (e.g., `John Doe` or `MyApp`)
2. (Optional) Enable "Parallel Processing" checkbox for faster processing of large group hierarchies
3. Click "Load Role Assignments"
4. If multiple identities match your search, select the correct one from the table
5. View the comprehensive results:
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