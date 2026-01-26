using Matriarch.Web.Models;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Matriarch.Web.Services;
using Matriarch.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Matriarch.Web.Components.Pages
{
    public partial class RoleAssignments
    {
        [Inject] private ITenantContext TenantContext { get; set; } = default!;
        [Inject] private ISubscriptionService SubscriptionService { get; set; } = default!;
        [Inject] private ILogger<RoleAssignments> Logger { get; set; } = default!;

        private string identityInput = "";
        private bool isLoading = false;
        private bool useParallelProcessing = true;
        private TimeSpan? processingTime = null;
        private IdentityRoleAssignmentResult? result;
        private string? errorMessage;
        private IdentitySearchResult? searchResult;
        private Identity? selectedIdentity;

        // Cache for subscription and management group lookups
        private Dictionary<string, SubscriptionDto> subscriptionCache = new();
        private Dictionary<string, ManagementGroupDto> managementGroupCache = new();
        private bool subscriptionCacheLoaded = false;

        // Loading states for progressive loading
        private bool isLoadingDirectRoleAssignments = false;
        private bool isLoadingDirectGroups = false;
        private bool isLoadingIndirectGroups = false;
        private bool isLoadingGroupRoleAssignments = false;
        private bool isLoadingApiPermissions = false;
        private bool isLoadingKeyVaultPolicies = false;
        private bool isAnyDataLoaded = false;

        private async Task LoadRoleAssignments()
        {
            if (string.IsNullOrWhiteSpace(identityInput))
            {
                return;
            }

            // Trim whitespace from identity input
            identityInput = identityInput.Trim();

            isLoading = true;
            errorMessage = null;
            result = null;
            processingTime = null;
            searchResult = null;
            selectedIdentity = null;

            try
            {
                // First, search for identities
                searchResult = await _roleAssignmentService.SearchIdentitiesAsync(identityInput);

                if (searchResult.Identities.Count == 0)
                {
                    errorMessage = $"Identity '{identityInput}' could not be found in the tenant. Please verify the input and try again.";
                }
                else if (searchResult.Identities.Count == 1)
                {
                    // Only one result, load role assignments directly
                    selectedIdentity = searchResult.Identities[0];
                    await LoadRoleAssignmentsForSelectedIdentity();
                }
                // else: multiple results, show selection table
            }
            catch (Exception ex)
            {
                errorMessage = $"An error occurred while searching for identities: {ex.Message}";
            }
            finally
            {
                isLoading = false;
            }
        }

        private async Task LoadRoleAssignmentsForSelectedIdentity()
        {
            if (selectedIdentity == null)
            {
                return;
            }

            isLoading = true;
            errorMessage = null;
            result = null;
            processingTime = null;

            // Set all loading states to true - show spinners on all sections
            isLoadingDirectRoleAssignments = true;
            isLoadingDirectGroups = true;
            isLoadingIndirectGroups = true;
            isLoadingGroupRoleAssignments = true;
            isLoadingApiPermissions = true;
            isLoadingKeyVaultPolicies = true;

            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Preload subscription cache if not already loaded
                await EnsureSubscriptionCacheLoadedAsync();

                // Initialize result with identity
                result = new IdentityRoleAssignmentResult
                {
                    Identity = selectedIdentity,
                    DirectRoleAssignments = new List<RoleAssignment>(),
                    SecurityDirectGroups = new List<SecurityGroup>(),
                    SecurityIndirectGroups = new List<SecurityGroup>(),
                    ApiPermissions = new List<ApiPermission>(),
                    KeyVaultAccessPolicies = new List<KeyVaultAccessPolicy>()
                };
                StateHasChanged(); // Update UI to show identity info and all loading spinners

                // Start API permissions loading immediately in parallel (doesn't depend on groups)
                var apiPermissionsTask = LoadApiPermissionsAsync();

                // Step 1: Load direct role assignments
                result.DirectRoleAssignments = await _roleAssignmentService.GetDirectRoleAssignmentsAsync(selectedIdentity);
                isLoadingDirectRoleAssignments = false;
                StateHasChanged(); // Update UI to show direct role assignments

                // Step 2: Load direct groups
                result.SecurityDirectGroups = await _roleAssignmentService.GetDirectGroupsAsync(selectedIdentity);
                isLoadingDirectGroups = false;
                StateHasChanged(); // Update UI to show direct groups

                // Step 3: Load indirect groups (transitive) - use UI checkbox value
                result.SecurityIndirectGroups = await _roleAssignmentService.GetIndirectGroupsAsync(result.SecurityDirectGroups, useParallelProcessing);
                isLoadingIndirectGroups = false;
                StateHasChanged(); // Update UI to show indirect groups

                // Step 4: Populate group role assignments
                await _roleAssignmentService.PopulateGroupRoleAssignmentsAsync(result.SecurityDirectGroups, result.SecurityIndirectGroups);
                isLoadingGroupRoleAssignments = false;
                StateHasChanged(); // Update UI to show group role assignments

                // Step 5: Load Key Vault access policies (can be done in parallel with API permissions)
                var keyVaultPoliciesTask = LoadKeyVaultAccessPoliciesAsync();

                // Step 6: Wait for API permissions and Key Vault policies to complete
                await apiPermissionsTask;
                await keyVaultPoliciesTask;

                totalStopwatch.Stop();
                processingTime = totalStopwatch.Elapsed;
                isAnyDataLoaded = true;
            }
            catch (Exception ex)
            {
                errorMessage = $"An error occurred while loading role assignments: {ex.Message}";
            }
            finally
            {
                isLoading = false;
            }
        }

        private void SelectIdentity(Identity identity)
        {
            selectedIdentity = identity;
        }

        private async Task LoadApiPermissionsAsync()
        {
            if (selectedIdentity == null || result == null)
            {
                return;
            }

            try
            {
                result.ApiPermissions = await _roleAssignmentService.GetApiPermissionsAsync(selectedIdentity);
                isLoadingApiPermissions = false;
                StateHasChanged();
            }
            catch (Exception ex)
            {
                // Log error but don't fail the entire operation
                errorMessage = $"Error loading API permissions: {ex.Message}";
                isLoadingApiPermissions = false;
                StateHasChanged();
            }
        }

        private async Task LoadKeyVaultAccessPoliciesAsync()
        {
            if (selectedIdentity == null || result == null)
            {
                return;
            }

            try
            {
                result.KeyVaultAccessPolicies = await _roleAssignmentService.GetKeyVaultAccessPoliciesAsync(
                    selectedIdentity,
                    result.SecurityDirectGroups,
                    result.SecurityIndirectGroups);
                isLoadingKeyVaultPolicies = false;
                StateHasChanged();
            }
            catch (Exception ex)
            {
                // Log error but don't fail the entire operation
                errorMessage = $"Error loading Key Vault access policies: {ex.Message}";
                isLoadingKeyVaultPolicies = false;
                StateHasChanged();
            }
        }

        private string GetIdentityTypeDisplay(IdentityType identityType)
        {
            return identityType switch
            {
                IdentityType.User => "User",
                IdentityType.Group => "Group",
                IdentityType.ServicePrincipal => "Service Principal",
                IdentityType.UserAssignedManagedIdentity => "User-Assigned Managed Identity",
                IdentityType.SystemAssignedManagedIdentity => "System-Assigned Managed Identity",
                _ => "Unknown"
            };
        }

        private List<RoleAssignment> GetAllRoleAssignments()
        {
            if (result == null)
            {
                return new List<RoleAssignment>();
            }

            var allRoleAssignments = new List<RoleAssignment>();
            var processedIds = new HashSet<string>();

            // Collect role assignments from all groups
            void CollectRoleAssignments(SecurityGroup group)
            {
                foreach (var ra in group.RoleAssignments)
                {
                    // Use a combination of role name and scope as unique identifier
                    var key = $"{ra.RoleName}|{ra.Scope}";
                    if (!processedIds.Contains(key))
                    {
                        processedIds.Add(key);
                        allRoleAssignments.Add(ra);
                    }
                }
            }

            foreach (var group in result.SecurityDirectGroups)
            {
                CollectRoleAssignments(group);
            }
            foreach (var group in result.SecurityIndirectGroups)
            {
                CollectRoleAssignments(group);
            }

            return allRoleAssignments;
        }

        private bool IsDataFullyLoaded()
        {
            // Check if all loading states are false, meaning all data has been fetched
            return isAnyDataLoaded
                && !isLoadingDirectRoleAssignments
                && !isLoadingDirectGroups
                && !isLoadingIndirectGroups
                && !isLoadingGroupRoleAssignments
                && !isLoadingApiPermissions
                && !isLoadingKeyVaultPolicies;
        }

        private async Task DownloadExcel()
        {
            if (result == null)
            {
                return;
            }

            try
            {
                // Generate Excel file
                var excelBytes = _excelExportService.ExportToExcel(result);

                // Create filename with identity name and timestamp
                var fileName = $"RoleAssignments_{SanitizeFileName(result.Identity.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                // Download file using JavaScript interop
                await JSRuntime.InvokeVoidAsync("downloadFile", fileName, Convert.ToBase64String(excelBytes));
            }
            catch (Exception ex)
            {
                errorMessage = $"An error occurred while generating the Excel file: {ex.Message}";
            }
        }

        private string SanitizeFileName(string fileName)
        {
            // Remove invalid characters from filename
            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }

        private string? GetIdentityPortalUrl(Identity identity)
        {
            var tenantSettings = TenantContext.GetCurrentTenantSettings();
            var cloudEnvironment = GraphClientFactory.ParseCloudEnvironment(tenantSettings.CloudEnvironment);
            var tenantId = tenantSettings.TenantId;

            return identity.Type switch
            {
                IdentityType.User => AzurePortalUrlHelper.GetUserUrl(cloudEnvironment, identity.ObjectId),
                IdentityType.Group => AzurePortalUrlHelper.GetGroupUrl(cloudEnvironment, identity.ObjectId),
                IdentityType.ServicePrincipal => AzurePortalUrlHelper.GetServicePrincipalUrl(cloudEnvironment, identity.ObjectId, identity.ApplicationId),
                IdentityType.UserAssignedManagedIdentity => AzurePortalUrlHelper.GetManagedIdentityUrl(
                    cloudEnvironment,
                    tenantId,
                    identity.SubscriptionId, 
                    identity.ResourceGroup, 
                    identity.Name, 
                    true),
                IdentityType.SystemAssignedManagedIdentity => null, // System-assigned MIs don't have their own portal page
                _ => null
            };
        }

        private string GetSubscriptionPortalUrl(string subscriptionId)
        {
            var tenantSettings = TenantContext.GetCurrentTenantSettings();
            var cloudEnvironment = GraphClientFactory.ParseCloudEnvironment(tenantSettings.CloudEnvironment);
            var tenantId = tenantSettings.TenantId;
            return AzurePortalUrlHelper.GetSubscriptionUrl(cloudEnvironment, tenantId, subscriptionId);
        }

        private RenderFragment RenderScopeWithTooltip(string scope)
        {
            return builder =>
            {
                var subscriptionId = ExtractSubscriptionIdFromScope(scope);
                var managementGroupId = ExtractManagementGroupIdFromScope(scope);
                
                if (!string.IsNullOrEmpty(subscriptionId))
                {
                    var subscription = GetSubscriptionInfoCached(subscriptionId);
                    
                    if (subscription != null)
                    {
                        var tooltipText = $"{subscription.Name}";
                        if (subscription.ManagementGroupHierarchy != null && subscription.ManagementGroupHierarchy.Any())
                        {
                            tooltipText += $"\n{string.Join(" > ", subscription.ManagementGroupHierarchy)}";
                        }

                        builder.OpenElement(0, "code");
                        builder.AddAttribute(1, "class", "text-muted");
                        builder.AddAttribute(2, "title", tooltipText);
                        builder.AddAttribute(3, "style", "cursor: help;");
                        builder.AddContent(4, scope);
                        builder.CloseElement();
                    }
                    else
                    {
                        // No subscription info available
                        builder.OpenElement(0, "code");
                        builder.AddAttribute(1, "class", "text-muted");
                        builder.AddContent(2, scope);
                        builder.CloseElement();
                    }
                }
                else if (!string.IsNullOrEmpty(managementGroupId))
                {
                    // Management Group scope - show tooltip with MG display name
                    var mg = GetManagementGroupInfoCached(managementGroupId);
                    string tooltipText;
                    
                    if (mg != null && !string.IsNullOrEmpty(mg.DisplayName))
                    {
                        tooltipText = $"Management Group: {mg.DisplayName}";
                    }
                    else
                    {
                        tooltipText = $"Management Group: {managementGroupId}";
                    }

                    builder.OpenElement(0, "code");
                    builder.AddAttribute(1, "class", "text-muted");
                    builder.AddAttribute(2, "title", tooltipText);
                    builder.AddAttribute(3, "style", "cursor: help;");
                    builder.AddContent(4, scope);
                    builder.CloseElement();
                }
                else
                {
                    // Not a subscription or management group scope
                    builder.OpenElement(0, "code");
                    builder.AddAttribute(1, "class", "text-muted");
                    builder.AddContent(2, scope);
                    builder.CloseElement();
                }
            };
        }

        private string? ExtractSubscriptionIdFromScope(string scope)
        {
            // Scope format: /subscriptions/{subscriptionId}/...
            if (string.IsNullOrEmpty(scope))
            {
                return null;
            }

            var parts = scope.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("subscriptions", StringComparison.OrdinalIgnoreCase))
                {
                    return parts[i + 1];
                }
            }

            return null;
        }

        private string? ExtractManagementGroupIdFromScope(string scope)
        {
            // Scope format: /providers/Microsoft.Management/managementGroups/{managementGroupId}
            if (string.IsNullOrEmpty(scope))
            {
                return null;
            }

            var parts = scope.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("managementGroups", StringComparison.OrdinalIgnoreCase))
                {
                    return parts[i + 1];
                }
            }

            return null;
        }

        private SubscriptionDto? GetSubscriptionInfoCached(string subscriptionId)
        {
            // Returns subscription from the preloaded cache
            if (subscriptionCache.TryGetValue(subscriptionId, out var subscription))
            {
                return subscription;
            }
            return null;
        }

        private ManagementGroupDto? GetManagementGroupInfoCached(string managementGroupName)
        {
            // Returns management group from the preloaded cache
            if (managementGroupCache.TryGetValue(managementGroupName, out var mg))
            {
                return mg;
            }
            return null;
        }

        private async Task EnsureSubscriptionCacheLoadedAsync()
        {
            if (subscriptionCacheLoaded)
            {
                return;
            }

            try
            {
                // Trigger subscription service cache refresh which loads both subscriptions and MGs
                await SubscriptionService.RefreshSubscriptionCacheAsync();
                
                // Get subscriptions from cache
                var subscriptions = await SubscriptionService.GetAllSubscriptionsAsync();
                subscriptionCache = subscriptions.ToDictionary(s => s.SubscriptionId, StringComparer.OrdinalIgnoreCase);
                
                // Populate management group cache by requesting all unique MG names from subscription hierarchies
                var uniqueMgNames = subscriptions
                    .SelectMany(s => s.ManagementGroupHierarchy)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                
                foreach (var mgName in uniqueMgNames)
                {
                    var mg = await SubscriptionService.GetManagementGroupAsync(mgName);
                    if (mg != null)
                    {
                        managementGroupCache[mgName] = mg;
                    }
                }
                
                subscriptionCacheLoaded = true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error loading subscription and management group cache");
            }
        }
    }
}
