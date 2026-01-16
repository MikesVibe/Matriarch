namespace Matriarch.Web.Configuration;

public class AppSettings
{
    public Dictionary<string, AzureSettings> Azure { get; set; } = new();
    public ParallelizationSettings Parallelization { get; set; } = new();
}

public class AzureSettings
{
    public string TenantId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty; // Optional - not used for Resource Graph queries
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public class ParallelizationSettings
{
    public int MaxDegreeOfParallelism { get; set; } = 4;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 1000;
    public int MaxConcurrentTransitiveGroupRequests { get; set; } = 5;
    public int TransitiveGroupBatchSize { get; set; } = 10;
    public int DelayBetweenBatchesMilliseconds { get; set; } = 100;
}
