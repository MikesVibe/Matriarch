namespace Matriarch.Services;

public interface ICachingService
{
    Task<T?> GetCachedDataAsync<T>(string cacheKey) where T : class;
    Task SetCachedDataAsync<T>(string cacheKey, T data) where T : class;
    Task ClearCacheAsync(string cacheKey);
    Task ClearAllCacheAsync();
}
