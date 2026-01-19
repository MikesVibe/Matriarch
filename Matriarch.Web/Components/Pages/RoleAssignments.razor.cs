using Matriarch.Web.Models;
using Microsoft.JSInterop;

namespace Matriarch.Web.Components.Pages
{
    public partial class RoleAssignments
    {
        private string identityInput = "";
        private bool isLoading = false;
        private bool useParallelProcessing = true;
        private TimeSpan? processingTime = null;
        private IdentityRoleAssignmentResult? result;
        private string? errorMessage;
        private IdentitySearchResult? searchResult;
        private Identity? selectedIdentity;

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
    }
}
