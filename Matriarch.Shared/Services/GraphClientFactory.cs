using Azure.Identity;
using Microsoft.Graph.Beta;

namespace Matriarch.Shared.Services;

/// <summary>
/// Enum representing different Azure cloud environments
/// </summary>
public enum CloudEnvironment
{
    /// <summary>
    /// Azure Commercial Cloud (https://graph.microsoft.com)
    /// </summary>
    Public,
    
    /// <summary>
    /// Azure Government Cloud (https://graph.microsoft.us)
    /// </summary>
    Government,
    
    /// <summary>
    /// Azure China Cloud (https://microsoftgraph.chinacloudapi.cn)
    /// </summary>
    China
}

/// <summary>
/// Factory for creating GraphServiceClient instances configured for different Azure cloud environments
/// </summary>
public static class GraphClientFactory
{
    /// <summary>
    /// Creates a GraphServiceClient for the specified cloud environment
    /// </summary>
    /// <param name="tenantId">Azure AD tenant ID</param>
    /// <param name="clientId">Application (client) ID</param>
    /// <param name="clientSecret">Client secret</param>
    /// <param name="cloudEnvironment">Target cloud environment (defaults to Public)</param>
    /// <returns>Configured GraphServiceClient instance</returns>
    public static GraphServiceClient CreateClient(
        string tenantId,
        string clientId,
        string clientSecret,
        CloudEnvironment cloudEnvironment = CloudEnvironment.Public)
    {
        var credential = CreateCredential(tenantId, clientId, clientSecret, cloudEnvironment);
        var graphEndpoint = GetGraphEndpoint(cloudEnvironment);
        
        return new GraphServiceClient(credential, [$"{graphEndpoint}/.default"], $"{graphEndpoint}/v1.0");
    }

    /// <summary>
    /// Creates a ClientSecretCredential for the specified cloud environment
    /// </summary>
    private static ClientSecretCredential CreateCredential(
        string tenantId,
        string clientId,
        string clientSecret,
        CloudEnvironment cloudEnvironment)
    {
        var options = new ClientSecretCredentialOptions
        {
            AuthorityHost = GetAuthorityHost(cloudEnvironment)
        };

        return new ClientSecretCredential(tenantId, clientId, clientSecret, options);
    }

    /// <summary>
    /// Gets the Microsoft Graph API endpoint for the specified cloud environment
    /// </summary>
    private static string GetGraphEndpoint(CloudEnvironment cloudEnvironment)
    {
        return cloudEnvironment switch
        {
            CloudEnvironment.Public => "https://graph.microsoft.com",
            CloudEnvironment.Government => "https://graph.microsoft.us",
            CloudEnvironment.China => "https://microsoftgraph.chinacloudapi.cn",
            _ => throw new ArgumentOutOfRangeException(nameof(cloudEnvironment), cloudEnvironment, "Unsupported cloud environment")
        };
    }

    /// <summary>
    /// Gets the Azure AD authority host URI for the specified cloud environment
    /// </summary>
    public static Uri GetAuthorityHost(CloudEnvironment cloudEnvironment)
    {
        return cloudEnvironment switch
        {
            CloudEnvironment.Public => AzureAuthorityHosts.AzurePublicCloud,
            CloudEnvironment.Government => AzureAuthorityHosts.AzureGovernment,
            CloudEnvironment.China => AzureAuthorityHosts.AzureChina,
            _ => throw new ArgumentOutOfRangeException(nameof(cloudEnvironment), cloudEnvironment, "Unsupported cloud environment")
        };
    }

    /// <summary>
    /// Gets the Azure Resource Manager endpoint for the specified cloud environment
    /// </summary>
    /// <param name="cloudEnvironment">Target cloud environment</param>
    /// <returns>Azure Resource Manager endpoint URL</returns>
    public static string GetResourceManagerEndpoint(CloudEnvironment cloudEnvironment)
    {
        return cloudEnvironment switch
        {
            CloudEnvironment.Public => "https://management.azure.com",
            CloudEnvironment.Government => "https://management.usgovcloudapi.net",
            CloudEnvironment.China => "https://management.chinacloudapi.cn",
            _ => throw new ArgumentOutOfRangeException(nameof(cloudEnvironment), cloudEnvironment, "Unsupported cloud environment")
        };
    }

    /// <summary>
    /// Parses a string value to CloudEnvironment enum
    /// </summary>
    /// <param name="value">String representation of cloud environment (e.g., "Public", "Government", "China"). Case-insensitive.</param>
    /// <returns>CloudEnvironment enum value. Returns CloudEnvironment.Public if value is null or whitespace.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is not a valid cloud environment name.</exception>
    public static CloudEnvironment ParseCloudEnvironment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CloudEnvironment.Public;
        }

        if (Enum.TryParse<CloudEnvironment>(value, ignoreCase: true, out var result))
        {
            return result;
        }

        throw new ArgumentException($"Invalid cloud environment value: {value}. Valid values are: {string.Join(", ", Enum.GetNames(typeof(CloudEnvironment)))}", nameof(value));
    }
}
