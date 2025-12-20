using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

public interface IExcelExportService
{
    byte[] ExportToExcel(IdentityRoleAssignmentResult result);
}
