using Matriarch.Web.Components;
using Matriarch.Web.Configuration;
using Matriarch.Web.Services;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

// Add authentication services
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add controllers for Microsoft Identity Web UI
builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();

// Bind and register AppSettings for Azure configuration
var appSettings = new AppSettings();
builder.Configuration.Bind(appSettings);
builder.Services.AddSingleton(appSettings);

// Register TenantContext as scoped service (per user session)
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ITenantAccessService, TenantAccessService>();

// Register HttpClient for services that need it
builder.Services.AddHttpClient<IResourceGraphService, AzureResourceGraphService>();

// Register custom services for role assignments
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<IGroupManagementService, GroupManagementService>();
builder.Services.AddScoped<IApiPermissionsService, AzureApiPermissionsService>();
builder.Services.AddScoped<IResourceGraphService, AzureResourceGraphService>();
builder.Services.AddScoped<IRoleAssignmentService, AzureRoleAssignmentService>();
builder.Services.AddScoped<IExcelExportService, ExcelExportService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map controllers for authentication
app.MapControllers();

app.Run();
