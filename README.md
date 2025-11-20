# Matriarch

A .NET 8 application that fetches data from Azure (Entra ID and Azure Resource Manager) and stores it in a Neo4j graph database.

## Features

- **Fetches Azure Data:**
  - Role assignments from Azure Resource Graph for the entire directory
  - Enterprise applications (Service Principals) from Microsoft Graph
  - App registrations with their federated credentials from Microsoft Graph
  - Entra security groups from Microsoft Graph

- **Creates Graph Relationships:**
  - App Registration → Enterprise Application (via Service Principal)
  - Enterprise Application → Role Assignments
  - Enterprise Application → Security Group memberships
  - Security Group → Role Assignments
  - App Registration → Federated Credentials

## Prerequisites

- .NET 8 SDK
- Neo4j database (local or cloud instance)
- Azure AD Service Principal with the following permissions:
  - **Microsoft Graph API:**
    - `Application.Read.All`
    - `Directory.Read.All`
    - `GroupMember.Read.All`
  - **Azure Resource Graph:**
    - Access to query role assignments across the directory (subscription-level Reader role recommended)

## Configuration

Update `appsettings.json` with your credentials:

```json
{
  "Azure": {
    "TenantId": "your-tenant-id",
    "SubscriptionId": "your-subscription-id",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret"
  },
  "Neo4j": {
    "Uri": "bolt://localhost:7687",
    "Username": "neo4j",
    "Password": "your-neo4j-password"
  }
}
```

Alternatively, you can use environment variables:
- `Azure__TenantId`
- `Azure__SubscriptionId`
- `Azure__ClientId`
- `Azure__ClientSecret`
- `Neo4j__Uri`
- `Neo4j__Username`
- `Neo4j__Password`

## Building

```bash
cd Matriarch
dotnet build
```

## Running

### Using .NET CLI

```bash
cd Matriarch
dotnet run
```

### Using Docker

Build the Docker image:
```bash
cd Matriarch
docker build -t matriarch:latest .
```

Run the container:
```bash
docker run --rm \
  -e Azure__TenantId="your-tenant-id" \
  -e Azure__SubscriptionId="your-subscription-id" \
  -e Azure__ClientId="your-client-id" \
  -e Azure__ClientSecret="your-client-secret" \
  -e Neo4j__Uri="bolt://host.docker.internal:7687" \
  -e Neo4j__Username="neo4j" \
  -e Neo4j__Password="your-password" \
  matriarch:latest
```

### Using Docker Compose

Create a `.env` file in the root directory with your Azure credentials:
```env
AZURE_TENANT_ID=your-tenant-id
AZURE_SUBSCRIPTION_ID=your-subscription-id
AZURE_CLIENT_ID=your-client-id
AZURE_CLIENT_SECRET=your-client-secret
```

Start both Neo4j and Matriarch:
```bash
docker-compose up
```

Access Neo4j Browser at `http://localhost:7474` (username: `neo4j`, password: `password`)

## Graph Schema

The application creates the following nodes and relationships in Neo4j:

### Nodes
- **AppRegistration**: Azure AD App Registrations
  - Properties: `id`, `appId`, `displayName`
- **EnterpriseApp**: Service Principals
  - Properties: `id`, `appId`, `displayName`
- **SecurityGroup**: Entra Security Groups
  - Properties: `id`, `displayName`, `description`
- **RoleAssignment**: Azure RBAC Role Assignments
  - Properties: `id`, `principalId`, `principalType`, `roleDefinitionId`, `roleName`, `scope`
- **FederatedCredential**: Federated Identity Credentials
  - Properties: `id`, `name`, `issuer`, `subject`, `audiences`

### Relationships
- `(AppRegistration)-[:HAS_SERVICE_PRINCIPAL]->(EnterpriseApp)`
- `(AppRegistration)-[:HAS_FEDERATED_CREDENTIAL]->(FederatedCredential)`
- `(EnterpriseApp)-[:HAS_ROLE_ASSIGNMENT]->(RoleAssignment)`
- `(EnterpriseApp)-[:MEMBER_OF]->(SecurityGroup)`
- `(SecurityGroup)-[:HAS_ROLE_ASSIGNMENT]->(RoleAssignment)`

## Example Cypher Queries

### Find all role assignments for an enterprise application
```cypher
MATCH (e:EnterpriseApp {displayName: 'MyApp'})-[:HAS_ROLE_ASSIGNMENT]->(r:RoleAssignment)
RETURN e.displayName, r.roleName, r.scope
```

### Find all groups an enterprise application is a member of
```cypher
MATCH (e:EnterpriseApp)-[:MEMBER_OF]->(g:SecurityGroup)
RETURN e.displayName, g.displayName
```

### Find app registrations with federated credentials
```cypher
MATCH (a:AppRegistration)-[:HAS_FEDERATED_CREDENTIAL]->(f:FederatedCredential)
RETURN a.displayName, f.name, f.issuer, f.subject
```

### Find the complete path from app registration to role assignments
```cypher
MATCH path = (a:AppRegistration)-[:HAS_SERVICE_PRINCIPAL]->(e:EnterpriseApp)-[:HAS_ROLE_ASSIGNMENT]->(r:RoleAssignment)
RETURN path
```

## License

MIT