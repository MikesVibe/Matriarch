namespace Matriarch.Web.Configuration;

public class AppSettings
{
    public AzureSettings Azure { get; set; } = new();
    public Neo4jSettings Neo4j { get; set; } = new();
    public CacheSettings Cache { get; set; } = new();
    public SqliteSettings Sqlite { get; set; } = new();
}

public class AzureSettings
{
    public string TenantId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty; // Optional - not used for Resource Graph queries
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public class Neo4jSettings
{
    public string Uri { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class CacheSettings
{
    public bool UseCache { get; set; } = true;
    public string CacheDirectory { get; set; } = "cache";
}

public class SqliteSettings
{
    public string DatabasePath { get; set; } = "matriarch.db";
}
