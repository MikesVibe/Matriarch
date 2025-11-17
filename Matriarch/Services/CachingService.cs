using Microsoft.Extensions.Logging;
using Matriarch.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Matriarch.Services;

public class CachingService
{
    private readonly ILogger<CachingService> _logger;
    private readonly CacheSettings _cacheSettings;
    private readonly JsonSerializerOptions _jsonOptions;

    public CachingService(AppSettings settings, ILogger<CachingService> logger)
    {
        _logger = logger;
        _cacheSettings = settings.Cache;
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Ensure cache directory exists
        if (_cacheSettings.UseCache)
        {
            Directory.CreateDirectory(_cacheSettings.CacheDirectory);
            _logger.LogInformation($"Cache directory: {Path.GetFullPath(_cacheSettings.CacheDirectory)}");
        }
    }

    /// <summary>
    /// Gets cached data if available and cache is enabled, otherwise returns null
    /// </summary>
    public async Task<T?> GetCachedDataAsync<T>(string cacheKey) where T : class
    {
        if (!_cacheSettings.UseCache)
        {
            _logger.LogDebug($"Cache is disabled. Skipping cache lookup for: {cacheKey}");
            return null;
        }

        var cacheFilePath = GetCacheFilePath(cacheKey);

        if (!File.Exists(cacheFilePath))
        {
            _logger.LogInformation($"Cache miss for: {cacheKey}");
            return null;
        }

        try
        {
            _logger.LogInformation($"Cache hit for: {cacheKey}");
            var json = await File.ReadAllTextAsync(cacheFilePath);
            var data = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            
            if (data != null)
            {
                _logger.LogInformation($"Successfully loaded cached data from: {cacheFilePath}");
            }
            
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Error reading cache file: {cacheFilePath}. Will fetch fresh data.");
            return null;
        }
    }

    /// <summary>
    /// Saves data to cache if caching is enabled
    /// </summary>
    public async Task SetCachedDataAsync<T>(string cacheKey, T data) where T : class
    {
        var cacheFilePath = GetCacheFilePath(cacheKey);

        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            await File.WriteAllTextAsync(cacheFilePath, json);
            _logger.LogInformation($"Cached data saved to: {cacheFilePath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error writing cache file: {cacheFilePath}");
        }
    }

    /// <summary>
    /// Clears a specific cache entry
    /// </summary>
    public void ClearCache(string cacheKey)
    {
        var cacheFilePath = GetCacheFilePath(cacheKey);

        if (File.Exists(cacheFilePath))
        {
            try
            {
                File.Delete(cacheFilePath);
                _logger.LogInformation($"Cleared cache for: {cacheKey}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting cache file: {cacheFilePath}");
            }
        }
    }

    /// <summary>
    /// Clears all cache files
    /// </summary>
    public void ClearAllCache()
    {
        if (!Directory.Exists(_cacheSettings.CacheDirectory))
        {
            return;
        }

        try
        {
            var files = Directory.GetFiles(_cacheSettings.CacheDirectory, "*.json");
            foreach (var file in files)
            {
                File.Delete(file);
            }
            _logger.LogInformation($"Cleared all cache files ({files.Length} files deleted)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache directory");
        }
    }

    /// <summary>
    /// Gets the full path for a cache file based on the cache key
    /// </summary>
    private string GetCacheFilePath(string cacheKey)
    {
        // Sanitize the cache key to make it a valid filename
        var sanitizedKey = string.Join("_", cacheKey.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_cacheSettings.CacheDirectory, $"{sanitizedKey}.json");
    }

    /// <summary>
    /// Checks if cache is enabled
    /// </summary>
    public bool IsCacheEnabled => _cacheSettings.UseCache;
}
