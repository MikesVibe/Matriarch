using Matriarch.Web.Models;
using Matriarch.Web.Services;
using ClosedXML.Excel;

namespace Matriarch.IntegrationTests;

/// <summary>
/// Integration tests for ExcelExportService.
/// These tests verify the Excel export functionality for identity role assignment results.
/// </summary>
public class ExcelExportServiceTests
{
    private readonly ExcelExportService _service;

    public ExcelExportServiceTests()
    {
        _service = new ExcelExportService();
    }

    private static IdentityRoleAssignmentResult CreateTestResult()
    {
        return new IdentityRoleAssignmentResult
        {
            Identity = new Identity
            {
                ObjectId = "test-object-id",
                Name = "Test User",
                Email = "test@example.com",
                Type = IdentityType.User
            },
            DirectRoleAssignments = new List<RoleAssignment>
            {
                new RoleAssignment
                {
                    Id = "ra-1",
                    RoleName = "Owner",
                    Scope = "/subscriptions/test-sub",
                    AssignedTo = "Direct Assignment"
                }
            },
            SecurityDirectGroups = new List<SecurityGroup>
            {
                new SecurityGroup
                {
                    Id = "group-1",
                    DisplayName = "Test Group",
                    Description = "Test Description",
                    RoleAssignments = new List<RoleAssignment>()
                }
            },
            SecurityIndirectGroups = new List<SecurityGroup>(),
            ApiPermissions = new List<ApiPermission>
            {
                new ApiPermission
                {
                    Id = "perm-1",
                    ResourceDisplayName = "Microsoft Graph",
                    PermissionType = "Application",
                    PermissionValue = "User.Read.All"
                }
            },
            KeyVaultAccessPolicies = new List<KeyVaultAccessPolicy>
            {
                new KeyVaultAccessPolicy
                {
                    KeyVaultName = "test-vault",
                    KeyVaultId = "/subscriptions/test-sub/resourceGroups/test-rg/providers/Microsoft.KeyVault/vaults/test-vault",
                    AssignedTo = "Test User",
                    KeyPermissions = new List<string> { "Get", "List" },
                    SecretPermissions = new List<string> { "Get" },
                    CertificatePermissions = new List<string>()
                }
            }
        };
    }

    [Fact]
    public void ExportToExcel_WithValidResult_ReturnsNonEmptyByteArray()
    {
        // Arrange
        var result = CreateTestResult();

        // Act
        var excelBytes = _service.ExportToExcel(result);

        // Assert
        Assert.NotNull(excelBytes);
        Assert.True(excelBytes.Length > 0);
    }

    [Fact]
    public void ExportToExcel_WithValidResult_CreatesValidExcelFile()
    {
        // Arrange
        var result = CreateTestResult();

        // Act
        var excelBytes = _service.ExportToExcel(result);

        // Assert - Try to open the generated Excel file
        using var stream = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(stream);

        Assert.NotNull(workbook);
        Assert.True(workbook.Worksheets.Count > 0);
    }

    [Fact]
    public void ExportToExcel_WithValidResult_ContainsExpectedWorksheets()
    {
        // Arrange
        var result = CreateTestResult();

        // Act
        var excelBytes = _service.ExportToExcel(result);

        // Assert
        using var stream = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(stream);

        Assert.Contains(workbook.Worksheets, ws => ws.Name == "Identity Summary");
        Assert.Contains(workbook.Worksheets, ws => ws.Name == "Direct Role Assignments");
        Assert.Contains(workbook.Worksheets, ws => ws.Name == "Direct Groups");
        Assert.Contains(workbook.Worksheets, ws => ws.Name == "Indirect Groups");
        Assert.Contains(workbook.Worksheets, ws => ws.Name == "All Role Assignments");
        Assert.Contains(workbook.Worksheets, ws => ws.Name == "API Permissions");
        Assert.Contains(workbook.Worksheets, ws => ws.Name == "Key Vault Access Policies");
    }

    [Fact]
    public void ExportToExcel_WithValidResult_IdentitySummaryContainsCorrectData()
    {
        // Arrange
        var result = CreateTestResult();

        // Act
        var excelBytes = _service.ExportToExcel(result);

        // Assert
        using var stream = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Identity Summary");

        // Check headers
        Assert.Equal("Property", worksheet.Cell(1, 1).Value.ToString());
        Assert.Equal("Value", worksheet.Cell(1, 2).Value.ToString());

        // Check identity name is present
        Assert.Contains(worksheet.CellsUsed(), cell => cell.Value.ToString() == "Test User");
        Assert.Contains(worksheet.CellsUsed(), cell => cell.Value.ToString() == "test@example.com");
    }

    [Fact]
    public void ExportToExcel_WithValidResult_DirectRoleAssignmentsContainsCorrectData()
    {
        // Arrange
        var result = CreateTestResult();

        // Act
        var excelBytes = _service.ExportToExcel(result);

        // Assert
        using var stream = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Direct Role Assignments");

        // Check headers
        Assert.Equal("Role Name", worksheet.Cell(1, 1).Value.ToString());
        Assert.Equal("Scope", worksheet.Cell(1, 2).Value.ToString());
        Assert.Equal("Assigned To", worksheet.Cell(1, 3).Value.ToString());

        // Check role assignment data
        Assert.Equal("Owner", worksheet.Cell(2, 1).Value.ToString());
        Assert.Equal("/subscriptions/test-sub", worksheet.Cell(2, 2).Value.ToString());
        Assert.Equal("Direct Assignment", worksheet.Cell(2, 3).Value.ToString());
    }

    [Fact]
    public void ExportToExcel_WithEmptyCollections_CreatesValidExcelFile()
    {
        // Arrange
        var result = new IdentityRoleAssignmentResult
        {
            Identity = new Identity
            {
                ObjectId = "test-id",
                Name = "Empty Test",
                Type = IdentityType.User
            },
            DirectRoleAssignments = new List<RoleAssignment>(),
            SecurityDirectGroups = new List<SecurityGroup>(),
            SecurityIndirectGroups = new List<SecurityGroup>(),
            ApiPermissions = new List<ApiPermission>(),
            KeyVaultAccessPolicies = new List<KeyVaultAccessPolicy>()
        };

        // Act
        var excelBytes = _service.ExportToExcel(result);

        // Assert
        Assert.NotNull(excelBytes);
        Assert.True(excelBytes.Length > 0);

        using var stream = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(7, workbook.Worksheets.Count);
    }
}
