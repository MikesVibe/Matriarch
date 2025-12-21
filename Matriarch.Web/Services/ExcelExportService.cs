using ClosedXML.Excel;
using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

public class ExcelExportService : IExcelExportService
{
    public byte[] ExportToExcel(IdentityRoleAssignmentResult result)
    {
        using var workbook = new XLWorkbook();

        // Add Identity Summary worksheet
        AddIdentitySummaryWorksheet(workbook, result);

        // Add Direct Role Assignments worksheet
        AddDirectRoleAssignmentsWorksheet(workbook, result);

        // Add Direct Groups worksheet
        AddDirectGroupsWorksheet(workbook, result);

        // Add Indirect Groups worksheet
        AddIndirectGroupsWorksheet(workbook, result);

        // Add All Role Assignments from Groups worksheet
        AddAllRoleAssignmentsWorksheet(workbook, result);

        // Add API Permissions worksheet
        AddApiPermissionsWorksheet(workbook, result);

        // Add Key Vault Access Policies worksheet
        AddKeyVaultAccessPoliciesWorksheet(workbook, result);

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

        // Add Identity Summary worksheet with all identities
        AddMultipleIdentitiesSummaryWorksheet(workbook, results);

        // Add Direct Role Assignments worksheet with all identities
        AddMultipleIdentitiesDirectRoleAssignmentsWorksheet(workbook, results);

        // Add Direct Groups worksheet with all identities
        AddMultipleIdentitiesDirectGroupsWorksheet(workbook, results);

        // Add Indirect Groups worksheet with all identities
        AddMultipleIdentitiesIndirectGroupsWorksheet(workbook, results);

        // Add All Role Assignments from Groups worksheet with all identities
        AddMultipleIdentitiesAllRoleAssignmentsWorksheet(workbook, results);

        // Add API Permissions worksheet with all identities
        AddMultipleIdentitiesApiPermissionsWorksheet(workbook, results);

        // Add Key Vault Access Policies worksheet with all identities
        AddMultipleIdentitiesKeyVaultPoliciesWorksheet(workbook, results);

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
