using ClosedXML.Excel;
using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

public class ExcelExportService : IExcelExportService
{
    public byte[] ExportToExcel(IdentityRoleAssignmentResult result)
    {
        using var workbook = new XLWorkbook();

        // Add a single comprehensive worksheet for the identity
        AddComprehensiveIdentityWorksheet(workbook, result, SanitizeSheetName(result.Identity.Name));

        // Save to memory stream
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private void AddIdentitySummaryWorksheet(XLWorkbook workbook, IdentityRoleAssignmentResult result)
    {
        var worksheet = workbook.Worksheets.Add("Identity Summary");
        var identity = result.Identity;

        worksheet.Cell(1, 1).Value = "Property";
        worksheet.Cell(1, 2).Value = "Value";

        int row = 2;
        worksheet.Cell(row++, 1).Value = "Identity Type";
        worksheet.Cell(row - 1, 2).Value = GetIdentityTypeDisplay(identity.Type);

        worksheet.Cell(row++, 1).Value = "Name";
        worksheet.Cell(row - 1, 2).Value = identity.Name;

        if (!string.IsNullOrEmpty(identity.Email))
        {
            worksheet.Cell(row++, 1).Value = "Email";
            worksheet.Cell(row - 1, 2).Value = identity.Email;
        }

        worksheet.Cell(row++, 1).Value = "Object ID";
        worksheet.Cell(row - 1, 2).Value = identity.ObjectId;

        if (!string.IsNullOrEmpty(identity.ApplicationId))
        {
            worksheet.Cell(row++, 1).Value = "Application ID";
            worksheet.Cell(row - 1, 2).Value = identity.ApplicationId;
        }

        if (!string.IsNullOrEmpty(identity.AppRegistrationId))
        {
            worksheet.Cell(row++, 1).Value = "App Registration Object ID";
            worksheet.Cell(row - 1, 2).Value = identity.AppRegistrationId;
        }

        if (!string.IsNullOrEmpty(identity.SubscriptionId))
        {
            worksheet.Cell(row++, 1).Value = "Subscription ID";
            worksheet.Cell(row - 1, 2).Value = identity.SubscriptionId;
        }

        if (!string.IsNullOrEmpty(identity.ResourceGroup))
        {
            worksheet.Cell(row++, 1).Value = "Resource Group";
            worksheet.Cell(row - 1, 2).Value = identity.ResourceGroup;
        }

        // Format header
        var headerRange = worksheet.Range(1, 1, 1, 2);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;

        worksheet.Columns().AdjustToContents();
    }

    /// <summary>
    /// Adds a comprehensive worksheet containing all identity information.
    /// </summary>
    /// <param name="workbook">The Excel workbook to add the worksheet to.</param>
    /// <param name="result">The identity role assignment result containing all data.</param>
    /// <param name="sheetName">The sanitized sheet name (must be Excel-compatible).</param>
    private void AddComprehensiveIdentityWorksheet(XLWorkbook workbook, IdentityRoleAssignmentResult result, string sheetName)
    {
        var worksheet = workbook.Worksheets.Add(sheetName);
        var identity = result.Identity;
        int currentRow = 1;

        // Section 1: Identity Summary
        worksheet.Cell(currentRow, 1).Value = "IDENTITY SUMMARY";
        worksheet.Range(currentRow, 1, currentRow, 5).Merge();
        worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
        worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
        worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
        currentRow++;

        worksheet.Cell(currentRow, 1).Value = "Property";
        worksheet.Cell(currentRow, 2).Value = "Value";
        var summaryHeaderRange = worksheet.Range(currentRow, 1, currentRow, 2);
        summaryHeaderRange.Style.Font.Bold = true;
        summaryHeaderRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        currentRow++;

        worksheet.Cell(currentRow, 1).Value = "Identity Type";
        worksheet.Cell(currentRow, 2).Value = GetIdentityTypeDisplay(identity.Type);
        currentRow++;

        worksheet.Cell(currentRow, 1).Value = "Name";
        worksheet.Cell(currentRow, 2).Value = identity.Name;
        currentRow++;

        if (!string.IsNullOrEmpty(identity.Email))
        {
            worksheet.Cell(currentRow, 1).Value = "Email";
            worksheet.Cell(currentRow, 2).Value = identity.Email;
            currentRow++;
        }

        worksheet.Cell(currentRow, 1).Value = "Object ID";
        worksheet.Cell(currentRow, 2).Value = identity.ObjectId;
        currentRow++;

        if (!string.IsNullOrEmpty(identity.ApplicationId))
        {
            worksheet.Cell(currentRow, 1).Value = "Application ID";
            worksheet.Cell(currentRow, 2).Value = identity.ApplicationId;
            currentRow++;
        }

        if (!string.IsNullOrEmpty(identity.AppRegistrationId))
        {
            worksheet.Cell(currentRow, 1).Value = "App Registration Object ID";
            worksheet.Cell(currentRow, 2).Value = identity.AppRegistrationId;
            currentRow++;
        }

        if (!string.IsNullOrEmpty(identity.SubscriptionId))
        {
            worksheet.Cell(currentRow, 1).Value = "Subscription ID";
            worksheet.Cell(currentRow, 2).Value = identity.SubscriptionId;
            currentRow++;
        }

        if (!string.IsNullOrEmpty(identity.ResourceGroup))
        {
            worksheet.Cell(currentRow, 1).Value = "Resource Group";
            worksheet.Cell(currentRow, 2).Value = identity.ResourceGroup;
            currentRow++;
        }

        currentRow += 2; // Add spacing

        // Section 2: Direct Role Assignments
        worksheet.Cell(currentRow, 1).Value = "DIRECT ROLE ASSIGNMENTS";
        worksheet.Range(currentRow, 1, currentRow, 5).Merge();
        worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
        worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
        worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightGreen;
        currentRow++;

        if (result.DirectRoleAssignments.Any())
        {
            worksheet.Cell(currentRow, 1).Value = "Role Name";
            worksheet.Cell(currentRow, 2).Value = "Scope";
            worksheet.Cell(currentRow, 3).Value = "Assigned To";
            var roleHeaderRange = worksheet.Range(currentRow, 1, currentRow, 3);
            roleHeaderRange.Style.Font.Bold = true;
            roleHeaderRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            currentRow++;

            foreach (var ra in result.DirectRoleAssignments)
            {
                worksheet.Cell(currentRow, 1).Value = ra.RoleName;
                worksheet.Cell(currentRow, 2).Value = ra.Scope;
                worksheet.Cell(currentRow, 3).Value = ra.AssignedTo;
                currentRow++;
            }
        }
        else
        {
            worksheet.Cell(currentRow, 1).Value = "No direct role assignments found.";
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
            currentRow++;
        }

        currentRow += 2; // Add spacing

        // Section 3: Direct Group Memberships
        worksheet.Cell(currentRow, 1).Value = "DIRECT GROUP MEMBERSHIPS";
        worksheet.Range(currentRow, 1, currentRow, 5).Merge();
        worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
        worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
        worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightSkyBlue;
        currentRow++;

        if (result.SecurityDirectGroups.Any())
        {
            worksheet.Cell(currentRow, 1).Value = "Group Name";
            worksheet.Cell(currentRow, 2).Value = "Description";
            var groupHeaderRange = worksheet.Range(currentRow, 1, currentRow, 2);
            groupHeaderRange.Style.Font.Bold = true;
            groupHeaderRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            currentRow++;

            foreach (var group in result.SecurityDirectGroups)
            {
                worksheet.Cell(currentRow, 1).Value = group.DisplayName;
                worksheet.Cell(currentRow, 2).Value = group.Description ?? "-";
                currentRow++;
            }
        }
        else
        {
            worksheet.Cell(currentRow, 1).Value = "Not a direct member of any security groups.";
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
            currentRow++;
        }

        currentRow += 2; // Add spacing

        // Section 4: Indirect Group Memberships
        worksheet.Cell(currentRow, 1).Value = "INDIRECT GROUP MEMBERSHIPS (VIA PARENT GROUPS)";
        worksheet.Range(currentRow, 1, currentRow, 5).Merge();
        worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
        worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
        worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightGray;
        currentRow++;

        if (result.SecurityIndirectGroups.Any())
        {
            worksheet.Cell(currentRow, 1).Value = "Group Name";
            worksheet.Cell(currentRow, 2).Value = "Description";
            worksheet.Cell(currentRow, 3).Value = "Child Group";
            var indirectGroupHeaderRange = worksheet.Range(currentRow, 1, currentRow, 3);
            indirectGroupHeaderRange.Style.Font.Bold = true;
            indirectGroupHeaderRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            currentRow++;

            foreach (var group in result.SecurityIndirectGroups)
            {
                worksheet.Cell(currentRow, 1).Value = group.DisplayName;
                worksheet.Cell(currentRow, 2).Value = group.Description ?? "-";
                worksheet.Cell(currentRow, 3).Value = group.ChildGroup?.DisplayName ?? "-";
                currentRow++;
            }
        }
        else
        {
            worksheet.Cell(currentRow, 1).Value = "No indirect group memberships.";
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
            currentRow++;
        }

        currentRow += 2; // Add spacing

        // Section 5: All Role Assignments from Groups
        worksheet.Cell(currentRow, 1).Value = "ALL ROLE ASSIGNMENTS (FROM ALL GROUPS)";
        worksheet.Range(currentRow, 1, currentRow, 5).Merge();
        worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
        worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
        worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightYellow;
        currentRow++;

        var allRoleAssignments = GetAllRoleAssignments(result);
        if (allRoleAssignments.Any())
        {
            worksheet.Cell(currentRow, 1).Value = "Role Name";
            worksheet.Cell(currentRow, 2).Value = "Scope";
            worksheet.Cell(currentRow, 3).Value = "Source";
            var allRoleHeaderRange = worksheet.Range(currentRow, 1, currentRow, 3);
            allRoleHeaderRange.Style.Font.Bold = true;
            allRoleHeaderRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            currentRow++;

            foreach (var ra in allRoleAssignments)
            {
                worksheet.Cell(currentRow, 1).Value = ra.RoleName;
                worksheet.Cell(currentRow, 2).Value = ra.Scope;
                worksheet.Cell(currentRow, 3).Value = ra.AssignedTo;
                currentRow++;
            }
        }
        else
        {
            worksheet.Cell(currentRow, 1).Value = "No role assignments from group memberships.";
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
            currentRow++;
        }

        currentRow += 2; // Add spacing

        // Section 6: API Permissions
        worksheet.Cell(currentRow, 1).Value = "API PERMISSIONS";
        worksheet.Range(currentRow, 1, currentRow, 5).Merge();
        worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
        worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
        worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.DarkGray;
        worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.White;
        currentRow++;

        if (result.ApiPermissions.Any())
        {
            worksheet.Cell(currentRow, 1).Value = "Resource";
            worksheet.Cell(currentRow, 2).Value = "Permission";
            worksheet.Cell(currentRow, 3).Value = "Type";
            var apiHeaderRange = worksheet.Range(currentRow, 1, currentRow, 3);
            apiHeaderRange.Style.Font.Bold = true;
            apiHeaderRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            currentRow++;

            foreach (var permission in result.ApiPermissions)
            {
                worksheet.Cell(currentRow, 1).Value = permission.ResourceDisplayName;
                worksheet.Cell(currentRow, 2).Value = permission.PermissionValue;
                worksheet.Cell(currentRow, 3).Value = permission.PermissionType;
                currentRow++;
            }
        }
        else
        {
            worksheet.Cell(currentRow, 1).Value = "No API permissions found.";
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
            currentRow++;
        }

        currentRow += 2; // Add spacing

        // Section 7: Key Vault Access Policies
        worksheet.Cell(currentRow, 1).Value = "KEY VAULT ACCESS POLICIES";
        worksheet.Range(currentRow, 1, currentRow, 5).Merge();
        worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
        worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
        worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightCyan;
        currentRow++;

        if (result.KeyVaultAccessPolicies.Any())
        {
            worksheet.Cell(currentRow, 1).Value = "Key Vault";
            worksheet.Cell(currentRow, 2).Value = "Assigned To";
            worksheet.Cell(currentRow, 3).Value = "Key Permissions";
            worksheet.Cell(currentRow, 4).Value = "Secret Permissions";
            worksheet.Cell(currentRow, 5).Value = "Certificate Permissions";
            var kvHeaderRange = worksheet.Range(currentRow, 1, currentRow, 5);
            kvHeaderRange.Style.Font.Bold = true;
            kvHeaderRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            currentRow++;

            foreach (var policy in result.KeyVaultAccessPolicies)
            {
                worksheet.Cell(currentRow, 1).Value = policy.KeyVaultName;
                worksheet.Cell(currentRow, 2).Value = policy.AssignedTo;
                worksheet.Cell(currentRow, 3).Value = FormatPermissions(policy.KeyPermissions);
                worksheet.Cell(currentRow, 4).Value = FormatPermissions(policy.SecretPermissions);
                worksheet.Cell(currentRow, 5).Value = FormatPermissions(policy.CertificatePermissions);
                currentRow++;
            }
        }
        else
        {
            worksheet.Cell(currentRow, 1).Value = "No Key Vault access policies found.";
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
            currentRow++;
        }

        // Adjust column widths
        worksheet.Columns().AdjustToContents();
    }

    private string SanitizeSheetName(string name)
    {
        // Excel sheet names cannot exceed 31 characters and cannot contain: \ / ? * [ ]
        var invalidChars = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var sanitized = name;
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }
        
        // Truncate if too long
        if (sanitized.Length > 31)
        {
            sanitized = sanitized.Substring(0, 31);
        }
        
        return sanitized;
    }

    private void AddDirectRoleAssignmentsWorksheet(XLWorkbook workbook, IdentityRoleAssignmentResult result)
    {
        var worksheet = workbook.Worksheets.Add("Direct Role Assignments");

        worksheet.Cell(1, 1).Value = "Role Name";
        worksheet.Cell(1, 2).Value = "Scope";
        worksheet.Cell(1, 3).Value = "Assigned To";

        int row = 2;
        foreach (var ra in result.DirectRoleAssignments)
        {
            worksheet.Cell(row, 1).Value = ra.RoleName;
            worksheet.Cell(row, 2).Value = ra.Scope;
            worksheet.Cell(row, 3).Value = ra.AssignedTo;
            row++;
        }

        // Format header
        var headerRange = worksheet.Range(1, 1, 1, 3);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGreen;

        worksheet.Columns().AdjustToContents();
    }

    private void AddDirectGroupsWorksheet(XLWorkbook workbook, IdentityRoleAssignmentResult result)
    {
        var worksheet = workbook.Worksheets.Add("Direct Groups");

        worksheet.Cell(1, 1).Value = "Group Name";
        worksheet.Cell(1, 2).Value = "Description";

        int row = 2;
        foreach (var group in result.SecurityDirectGroups)
        {
            worksheet.Cell(row, 1).Value = group.DisplayName;
            worksheet.Cell(row, 2).Value = group.Description ?? "-";
            row++;
        }

        // Format header
        var headerRange = worksheet.Range(1, 1, 1, 2);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightSkyBlue;

        worksheet.Columns().AdjustToContents();
    }

    private void AddIndirectGroupsWorksheet(XLWorkbook workbook, IdentityRoleAssignmentResult result)
    {
        var worksheet = workbook.Worksheets.Add("Indirect Groups");

        worksheet.Cell(1, 1).Value = "Group Name";
        worksheet.Cell(1, 2).Value = "Description";
        worksheet.Cell(1, 3).Value = "Child Group";

        int row = 2;
        foreach (var group in result.SecurityIndirectGroups)
        {
            worksheet.Cell(row, 1).Value = group.DisplayName;
            worksheet.Cell(row, 2).Value = group.Description ?? "-";
            worksheet.Cell(row, 3).Value = group.ChildGroup?.DisplayName ?? "-";
            row++;
        }

        // Format header
        var headerRange = worksheet.Range(1, 1, 1, 3);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

        worksheet.Columns().AdjustToContents();
    }

    private void AddAllRoleAssignmentsWorksheet(XLWorkbook workbook, IdentityRoleAssignmentResult result)
    {
        var worksheet = workbook.Worksheets.Add("All Role Assignments");

        worksheet.Cell(1, 1).Value = "Role Name";
        worksheet.Cell(1, 2).Value = "Scope";
        worksheet.Cell(1, 3).Value = "Source";

        int row = 2;
        var allRoleAssignments = GetAllRoleAssignments(result);
        foreach (var ra in allRoleAssignments)
        {
            worksheet.Cell(row, 1).Value = ra.RoleName;
            worksheet.Cell(row, 2).Value = ra.Scope;
            worksheet.Cell(row, 3).Value = ra.AssignedTo;
            row++;
        }

        // Format header
        var headerRange = worksheet.Range(1, 1, 1, 3);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightYellow;

        worksheet.Columns().AdjustToContents();
    }

    private void AddApiPermissionsWorksheet(XLWorkbook workbook, IdentityRoleAssignmentResult result)
    {
        var worksheet = workbook.Worksheets.Add("API Permissions");

        worksheet.Cell(1, 1).Value = "Resource";
        worksheet.Cell(1, 2).Value = "Permission";
        worksheet.Cell(1, 3).Value = "Type";

        int row = 2;
        foreach (var permission in result.ApiPermissions)
        {
            worksheet.Cell(row, 1).Value = permission.ResourceDisplayName;
            worksheet.Cell(row, 2).Value = permission.PermissionValue;
            worksheet.Cell(row, 3).Value = permission.PermissionType;
            row++;
        }

        // Format header
        var headerRange = worksheet.Range(1, 1, 1, 3);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.DarkGray;
        headerRange.Style.Font.FontColor = XLColor.White;

        worksheet.Columns().AdjustToContents();
    }

    private void AddKeyVaultAccessPoliciesWorksheet(XLWorkbook workbook, IdentityRoleAssignmentResult result)
    {
        var worksheet = workbook.Worksheets.Add("Key Vault Access Policies");

        worksheet.Cell(1, 1).Value = "Key Vault";
        worksheet.Cell(1, 2).Value = "Assigned To";
        worksheet.Cell(1, 3).Value = "Key Permissions";
        worksheet.Cell(1, 4).Value = "Secret Permissions";
        worksheet.Cell(1, 5).Value = "Certificate Permissions";

        int row = 2;
        foreach (var policy in result.KeyVaultAccessPolicies)
        {
            worksheet.Cell(row, 1).Value = policy.KeyVaultName;
            worksheet.Cell(row, 2).Value = policy.AssignedTo;
            worksheet.Cell(row, 3).Value = FormatPermissions(policy.KeyPermissions);
            worksheet.Cell(row, 4).Value = FormatPermissions(policy.SecretPermissions);
            worksheet.Cell(row, 5).Value = FormatPermissions(policy.CertificatePermissions);
            row++;
        }

        // Format header
        var headerRange = worksheet.Range(1, 1, 1, 5);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightCyan;

        worksheet.Columns().AdjustToContents();
    }

    private string FormatPermissions(List<string> permissions)
    {
        return permissions.Any() ? string.Join(", ", permissions) : "None";
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

    private List<RoleAssignment> GetAllRoleAssignments(IdentityRoleAssignmentResult result)
    {
        var allRoleAssignments = new List<RoleAssignment>();
        var processedIds = new HashSet<string>();

        foreach (var group in result.SecurityDirectGroups)
        {
            CollectRoleAssignments(group, allRoleAssignments, processedIds);
        }
        foreach (var group in result.SecurityIndirectGroups)
        {
            CollectRoleAssignments(group, allRoleAssignments, processedIds);
        }

        return allRoleAssignments;
    }

    private void CollectRoleAssignments(SecurityGroup group, List<RoleAssignment> allRoleAssignments, HashSet<string> processedIds)
    {
        foreach (var ra in group.RoleAssignments)
        {
            var key = $"{ra.RoleName}|{ra.Scope}";
            if (!processedIds.Contains(key))
            {
                processedIds.Add(key);
                allRoleAssignments.Add(ra);
            }
        }
    }

    public byte[] ExportMultipleIdentitiesToExcel(List<IdentityRoleAssignmentResult> results)
    {
        using var workbook = new XLWorkbook();

        // Add one comprehensive worksheet per identity
        foreach (var result in results)
        {
            AddComprehensiveIdentityWorksheet(workbook, result, SanitizeSheetName(result.Identity.Name));
        }

        // Save to memory stream
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private void AddMultipleIdentitiesSummaryWorksheet(XLWorkbook workbook, List<IdentityRoleAssignmentResult> results)
    {
        var worksheet = workbook.Worksheets.Add("Identity Summary");
        int currentRow = 1;

        foreach (var result in results)
        {
            var identity = result.Identity;

            // Add identity name as header
            worksheet.Cell(currentRow, 1).Value = $"Identity: {identity.Name}";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
            currentRow++;

            // Add property headers
            worksheet.Cell(currentRow, 1).Value = "Property";
            worksheet.Cell(currentRow, 2).Value = "Value";
            var headerRange = worksheet.Range(currentRow, 1, currentRow, 2);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
            currentRow++;

            // Add identity properties
            worksheet.Cell(currentRow++, 1).Value = "Identity Type";
            worksheet.Cell(currentRow - 1, 2).Value = GetIdentityTypeDisplay(identity.Type);

            worksheet.Cell(currentRow++, 1).Value = "Name";
            worksheet.Cell(currentRow - 1, 2).Value = identity.Name;

            if (!string.IsNullOrEmpty(identity.Email))
            {
                worksheet.Cell(currentRow++, 1).Value = "Email";
                worksheet.Cell(currentRow - 1, 2).Value = identity.Email;
            }

            worksheet.Cell(currentRow++, 1).Value = "Object ID";
            worksheet.Cell(currentRow - 1, 2).Value = identity.ObjectId;

            if (!string.IsNullOrEmpty(identity.ApplicationId))
            {
                worksheet.Cell(currentRow++, 1).Value = "Application ID";
                worksheet.Cell(currentRow - 1, 2).Value = identity.ApplicationId;
            }

            if (!string.IsNullOrEmpty(identity.SubscriptionId))
            {
                worksheet.Cell(currentRow++, 1).Value = "Subscription ID";
                worksheet.Cell(currentRow - 1, 2).Value = identity.SubscriptionId;
            }

            if (!string.IsNullOrEmpty(identity.ResourceGroup))
            {
                worksheet.Cell(currentRow++, 1).Value = "Resource Group";
                worksheet.Cell(currentRow - 1, 2).Value = identity.ResourceGroup;
            }

            // Add spacing between identities
            currentRow += 2;
        }

        worksheet.Columns().AdjustToContents();
    }

    private void AddMultipleIdentitiesDirectRoleAssignmentsWorksheet(XLWorkbook workbook, List<IdentityRoleAssignmentResult> results)
    {
        var worksheet = workbook.Worksheets.Add("Direct Role Assignments");
        int currentRow = 1;

        foreach (var result in results)
        {
            // Add identity name as header
            worksheet.Cell(currentRow, 1).Value = $"Identity: {result.Identity.Name}";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
            currentRow++;

            // Add column headers
            worksheet.Cell(currentRow, 1).Value = "Role Name";
            worksheet.Cell(currentRow, 2).Value = "Scope";
            worksheet.Cell(currentRow, 3).Value = "Assigned To";
            var headerRange = worksheet.Range(currentRow, 1, currentRow, 3);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGreen;
            currentRow++;

            // Add role assignments
            foreach (var ra in result.DirectRoleAssignments)
            {
                worksheet.Cell(currentRow, 1).Value = ra.RoleName;
                worksheet.Cell(currentRow, 2).Value = ra.Scope;
                worksheet.Cell(currentRow, 3).Value = ra.AssignedTo;
                currentRow++;
            }

            // Add spacing between identities
            currentRow += 2;
        }

        worksheet.Columns().AdjustToContents();
    }

    private void AddMultipleIdentitiesDirectGroupsWorksheet(XLWorkbook workbook, List<IdentityRoleAssignmentResult> results)
    {
        var worksheet = workbook.Worksheets.Add("Direct Groups");
        int currentRow = 1;

        foreach (var result in results)
        {
            // Add identity name as header
            worksheet.Cell(currentRow, 1).Value = $"Identity: {result.Identity.Name}";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
            currentRow++;

            // Add column headers
            worksheet.Cell(currentRow, 1).Value = "Group Name";
            worksheet.Cell(currentRow, 2).Value = "Description";
            var headerRange = worksheet.Range(currentRow, 1, currentRow, 2);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightSkyBlue;
            currentRow++;

            // Add groups
            foreach (var group in result.SecurityDirectGroups)
            {
                worksheet.Cell(currentRow, 1).Value = group.DisplayName;
                worksheet.Cell(currentRow, 2).Value = group.Description ?? "-";
                currentRow++;
            }

            // Add spacing between identities
            currentRow += 2;
        }

        worksheet.Columns().AdjustToContents();
    }

    private void AddMultipleIdentitiesIndirectGroupsWorksheet(XLWorkbook workbook, List<IdentityRoleAssignmentResult> results)
    {
        var worksheet = workbook.Worksheets.Add("Indirect Groups");
        int currentRow = 1;

        foreach (var result in results)
        {
            // Add identity name as header
            worksheet.Cell(currentRow, 1).Value = $"Identity: {result.Identity.Name}";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
            currentRow++;

            // Add column headers
            worksheet.Cell(currentRow, 1).Value = "Group Name";
            worksheet.Cell(currentRow, 2).Value = "Description";
            worksheet.Cell(currentRow, 3).Value = "Child Group";
            var headerRange = worksheet.Range(currentRow, 1, currentRow, 3);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            currentRow++;

            // Add groups
            foreach (var group in result.SecurityIndirectGroups)
            {
                worksheet.Cell(currentRow, 1).Value = group.DisplayName;
                worksheet.Cell(currentRow, 2).Value = group.Description ?? "-";
                worksheet.Cell(currentRow, 3).Value = group.ChildGroup?.DisplayName ?? "-";
                currentRow++;
            }

            // Add spacing between identities
            currentRow += 2;
        }

        worksheet.Columns().AdjustToContents();
    }

    private void AddMultipleIdentitiesAllRoleAssignmentsWorksheet(XLWorkbook workbook, List<IdentityRoleAssignmentResult> results)
    {
        var worksheet = workbook.Worksheets.Add("All Role Assignments");
        int currentRow = 1;

        foreach (var result in results)
        {
            // Add identity name as header
            worksheet.Cell(currentRow, 1).Value = $"Identity: {result.Identity.Name}";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
            currentRow++;

            // Add column headers
            worksheet.Cell(currentRow, 1).Value = "Role Name";
            worksheet.Cell(currentRow, 2).Value = "Scope";
            worksheet.Cell(currentRow, 3).Value = "Source";
            var headerRange = worksheet.Range(currentRow, 1, currentRow, 3);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightYellow;
            currentRow++;

            // Add all role assignments from groups
            var allRoleAssignments = GetAllRoleAssignments(result);
            foreach (var ra in allRoleAssignments)
            {
                worksheet.Cell(currentRow, 1).Value = ra.RoleName;
                worksheet.Cell(currentRow, 2).Value = ra.Scope;
                worksheet.Cell(currentRow, 3).Value = ra.AssignedTo;
                currentRow++;
            }

            // Add spacing between identities
            currentRow += 2;
        }

        worksheet.Columns().AdjustToContents();
    }

    private void AddMultipleIdentitiesApiPermissionsWorksheet(XLWorkbook workbook, List<IdentityRoleAssignmentResult> results)
    {
        var worksheet = workbook.Worksheets.Add("API Permissions");
        int currentRow = 1;

        foreach (var result in results)
        {
            // Add identity name as header
            worksheet.Cell(currentRow, 1).Value = $"Identity: {result.Identity.Name}";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
            currentRow++;

            // Add column headers
            worksheet.Cell(currentRow, 1).Value = "Resource";
            worksheet.Cell(currentRow, 2).Value = "Permission";
            worksheet.Cell(currentRow, 3).Value = "Type";
            var headerRange = worksheet.Range(currentRow, 1, currentRow, 3);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.DarkGray;
            headerRange.Style.Font.FontColor = XLColor.White;
            currentRow++;

            // Add API permissions
            foreach (var permission in result.ApiPermissions)
            {
                worksheet.Cell(currentRow, 1).Value = permission.ResourceDisplayName;
                worksheet.Cell(currentRow, 2).Value = permission.PermissionValue;
                worksheet.Cell(currentRow, 3).Value = permission.PermissionType;
                currentRow++;
            }

            // Add spacing between identities
            currentRow += 2;
        }

        worksheet.Columns().AdjustToContents();
    }

    private void AddMultipleIdentitiesKeyVaultPoliciesWorksheet(XLWorkbook workbook, List<IdentityRoleAssignmentResult> results)
    {
        var worksheet = workbook.Worksheets.Add("Key Vault Access Policies");
        int currentRow = 1;

        foreach (var result in results)
        {
            // Add identity name as header
            worksheet.Cell(currentRow, 1).Value = $"Identity: {result.Identity.Name}";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
            currentRow++;

            // Add column headers
            worksheet.Cell(currentRow, 1).Value = "Key Vault";
            worksheet.Cell(currentRow, 2).Value = "Assigned To";
            worksheet.Cell(currentRow, 3).Value = "Key Permissions";
            worksheet.Cell(currentRow, 4).Value = "Secret Permissions";
            worksheet.Cell(currentRow, 5).Value = "Certificate Permissions";
            var headerRange = worksheet.Range(currentRow, 1, currentRow, 5);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightCyan;
            currentRow++;

            // Add Key Vault policies
            foreach (var policy in result.KeyVaultAccessPolicies)
            {
                worksheet.Cell(currentRow, 1).Value = policy.KeyVaultName;
                worksheet.Cell(currentRow, 2).Value = policy.AssignedTo;
                worksheet.Cell(currentRow, 3).Value = FormatPermissions(policy.KeyPermissions);
                worksheet.Cell(currentRow, 4).Value = FormatPermissions(policy.SecretPermissions);
                worksheet.Cell(currentRow, 5).Value = FormatPermissions(policy.CertificatePermissions);
                currentRow++;
            }

            // Add spacing between identities
            currentRow += 2;
        }

        worksheet.Columns().AdjustToContents();
    }
}
