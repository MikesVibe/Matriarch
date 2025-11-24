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

Create an `appsettings.json` file in the `Matriarch.Web` directory with your Azure credentials:

```json
{
  "Azure": {
    "TenantId": "your-tenant-id",
    "SubscriptionId": "your-subscription-id",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

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
2. Click "Load Role Assignments"
3. If multiple identities match your search, select the correct one from the table
4. View the comprehensive results:
   - Identity type and details (including Managed Identity resource information)
   - Direct role assignments
   - Group memberships (direct and indirect)
   - Role assignments inherited through groups
   - API permissions (for Service Principals and Managed Identities)

## Supported Identity Types

The application supports querying and displaying information for:

- **Users**: Azure AD/Entra ID users
- **Security Groups**: Azure AD/Entra ID security-enabled groups
- **Service Principals**: Azure AD App Registrations with Enterprise Applications
- **Managed Identities**:
  - **System-Assigned Managed Identities**: Automatically created and tied to an Azure resource
  - **User-Assigned Managed Identities**: Standalone Azure resources that can be assigned to multiple Azure resources

For Managed Identities, the application displays:
- Subscription ID and name
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