using Matriarch.Shared.Services;

namespace Matriarch.Web.Services;

/// <summary>
/// Utility class for constructing Azure Portal URLs based on cloud environment.
/// </summary>
public static class AzurePortalUrlHelper
{
    /// <summary>
    /// Gets the Azure Portal base URL for the specified cloud environment.
    /// </summary>
    public static string GetPortalUrl(CloudEnvironment cloudEnvironment)
    {
        return cloudEnvironment switch
        {
            CloudEnvironment.Government => "https://portal.azure.us",
            CloudEnvironment.China => "https://portal.azure.cn",
            _ => "https://portal.azure.com"
        };
    }

    /// <summary>
    /// Constructs a URL to a managed identity resource in the Azure Portal.
    /// </summary>
    public static string? GetManagedIdentityUrl(CloudEnvironment cloudEnvironment, string? subscriptionId, string? resourceGroup, string identityName, bool isUserAssigned)
    {
        if (string.IsNullOrEmpty(subscriptionId) || string.IsNullOrEmpty(resourceGroup))
        {
            return null;
        }

        var portalUrl = GetPortalUrl(cloudEnvironment);
        
        if (isUserAssigned)
        {
            // User-assigned managed identity resource URL
            return $"{portalUrl}/#view/Microsoft_Azure_ManagedServiceIdentity/Identity.ReactView/resourceId/%2Fsubscriptions%2F{subscriptionId}%2FresourceGroups%2F{resourceGroup}%2Fproviders%2FMicrosoft.ManagedIdentity%2FuserAssignedIdentities%2F{identityName}";
        }
        else
        {
            // For system-assigned, we can't link directly to the identity itself, so return null
            // The identity is part of another resource
            return null;
        }
    }

    /// <summary>
    /// Constructs a URL to a subscription in the Azure Portal.
    /// </summary>
    public static string GetSubscriptionUrl(CloudEnvironment cloudEnvironment, string subscriptionId)
    {
        var portalUrl = GetPortalUrl(cloudEnvironment);
        return $"{portalUrl}/#view/Microsoft_Azure_Billing/SubscriptionsBlade/subscriptionId/{subscriptionId}";
    }

    /// <summary>
    /// Constructs a URL to a service principal in the Azure Portal (Enterprise Application).
    /// </summary>
    public static string GetServicePrincipalUrl(CloudEnvironment cloudEnvironment, string objectId)
    {
        var portalUrl = GetPortalUrl(cloudEnvironment);
        return $"{portalUrl}/#view/Microsoft_AAD_IAM/ManagedAppMenuBlade/~/Overview/objectId/{objectId}";
    }

    /// <summary>
    /// Constructs a URL to a user in the Azure Portal.
    /// </summary>
    public static string GetUserUrl(CloudEnvironment cloudEnvironment, string objectId)
    {
        var portalUrl = GetPortalUrl(cloudEnvironment);
        return $"{portalUrl}/#view/Microsoft_AAD_UsersAndTenants/UserProfileMenuBlade/~/overview/userId/{objectId}";
    }

    /// <summary>
    /// Constructs a URL to a group in the Azure Portal.
    /// </summary>
    public static string GetGroupUrl(CloudEnvironment cloudEnvironment, string objectId)
    {
        var portalUrl = GetPortalUrl(cloudEnvironment);
        return $"{portalUrl}/#view/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Overview/groupId/{objectId}";
    }
}
