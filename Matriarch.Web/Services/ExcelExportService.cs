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
            worksheet.Cell(row, 3).Value = policy.KeyPermissions.Any() ? string.Join(", ", policy.KeyPermissions) : "None";
            worksheet.Cell(row, 4).Value = policy.SecretPermissions.Any() ? string.Join(", ", policy.SecretPermissions) : "None";
            worksheet.Cell(row, 5).Value = policy.CertificatePermissions.Any() ? string.Join(", ", policy.CertificatePermissions) : "None";
            row++;
        }

        // Format header
        var headerRange = worksheet.Range(1, 1, 1, 5);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightCyan;

        worksheet.Columns().AdjustToContents();
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

        void CollectRoleAssignments(SecurityGroup group)
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
}
