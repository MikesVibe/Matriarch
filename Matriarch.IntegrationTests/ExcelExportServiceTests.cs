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

        // Should have one worksheet named after the identity
        Assert.Equal(1, workbook.Worksheets.Count);
        Assert.Contains(workbook.Worksheets, ws => ws.Name == "Test User");
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
        var worksheet = workbook.Worksheet("Test User");

        // Check identity summary section header
        Assert.Equal("IDENTITY SUMMARY", worksheet.Cell(1, 1).Value.ToString());

        // Check identity name and email are present
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
        var worksheet = workbook.Worksheet("Test User");

        // Check role assignment data is present in the comprehensive sheet
        Assert.Contains(worksheet.CellsUsed(), cell => cell.Value.ToString() == "DIRECT ROLE ASSIGNMENTS");
        Assert.Contains(worksheet.CellsUsed(), cell => cell.Value.ToString() == "Owner");
        Assert.Contains(worksheet.CellsUsed(), cell => cell.Value.ToString() == "/subscriptions/test-sub");
        Assert.Contains(worksheet.CellsUsed(), cell => cell.Value.ToString() == "Direct Assignment");
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
        Assert.Equal(1, workbook.Worksheets.Count);
    }

    [Fact]
    public void ExportMultipleIdentitiesToExcel_WithMultipleResults_CreatesOneSheetPerIdentity()
    {
        // Arrange
        var results = new List<IdentityRoleAssignmentResult>
        {
            new IdentityRoleAssignmentResult
            {
                Identity = new Identity
                {
                    ObjectId = "test-id-1",
                    Name = "Test User 1",
                    Email = "user1@example.com",
                    Type = IdentityType.User
                },
                DirectRoleAssignments = new List<RoleAssignment>
                {
                    new RoleAssignment
                    {
                        Id = "ra-1",
                        RoleName = "Reader",
                        Scope = "/subscriptions/sub1",
                        AssignedTo = "Direct Assignment"
                    }
                },
                SecurityDirectGroups = new List<SecurityGroup>(),
                SecurityIndirectGroups = new List<SecurityGroup>(),
                ApiPermissions = new List<ApiPermission>(),
                KeyVaultAccessPolicies = new List<KeyVaultAccessPolicy>()
            },
            new IdentityRoleAssignmentResult
            {
                Identity = new Identity
                {
                    ObjectId = "test-id-2",
                    Name = "Test User 2",
                    Email = "user2@example.com",
                    Type = IdentityType.User
                },
                DirectRoleAssignments = new List<RoleAssignment>
                {
                    new RoleAssignment
                    {
                        Id = "ra-2",
                        RoleName = "Contributor",
                        Scope = "/subscriptions/sub2",
                        AssignedTo = "Direct Assignment"
                    }
                },
                SecurityDirectGroups = new List<SecurityGroup>(),
                SecurityIndirectGroups = new List<SecurityGroup>(),
                ApiPermissions = new List<ApiPermission>(),
                KeyVaultAccessPolicies = new List<KeyVaultAccessPolicy>()
            }
        };

        // Act
        var excelBytes = _service.ExportMultipleIdentitiesToExcel(results);

        // Assert
        Assert.NotNull(excelBytes);
        Assert.True(excelBytes.Length > 0);

        using var stream = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(stream);
        
        // Should have one worksheet per identity
        Assert.Equal(2, workbook.Worksheets.Count);
        Assert.Contains(workbook.Worksheets, ws => ws.Name == "Test User 1");
        Assert.Contains(workbook.Worksheets, ws => ws.Name == "Test User 2");

        // Verify first identity's data
        var worksheet1 = workbook.Worksheet("Test User 1");
        Assert.Contains(worksheet1.CellsUsed(), cell => cell.Value.ToString() == "user1@example.com");
        Assert.Contains(worksheet1.CellsUsed(), cell => cell.Value.ToString() == "Reader");

        // Verify second identity's data
        var worksheet2 = workbook.Worksheet("Test User 2");
        Assert.Contains(worksheet2.CellsUsed(), cell => cell.Value.ToString() == "user2@example.com");
        Assert.Contains(worksheet2.CellsUsed(), cell => cell.Value.ToString() == "Contributor");
    }
}
