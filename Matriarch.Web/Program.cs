using Matriarch.Web.Components;
using Matriarch.Web.Configuration;
using Matriarch.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Bind and register AppSettings for Azure configuration
var appSettings = new AppSettings();
builder.Configuration.Bind(appSettings);
builder.Services.AddSingleton(appSettings);

// Register HttpClient for services that need it
builder.Services.AddHttpClient<IResourceGraphService, AzureResourceGraphService>();

// Register custom services for role assignments
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<IGroupManagementService, GroupManagementService>();
builder.Services.AddScoped<IApiPermissionsService, AzureApiPermissionsService>();
builder.Services.AddScoped<IResourceGraphService, AzureResourceGraphService>();
builder.Services.AddScoped<IRoleAssignmentService, AzureRoleAssignmentService>();

// Register Scrum Board services
builder.Services.AddSingleton<IScrumBoardService, ScrumBoardService>();
builder.Services.AddSignalR();

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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<ScrumBoardHub>("/scrumboardhub");

app.Run();
