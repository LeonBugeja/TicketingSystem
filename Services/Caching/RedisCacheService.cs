using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TicketingSystem.Services.Caching
{
    public class RedisCacheService : IRedisCacheService
    {
        private readonly IDistributedCache? _cache;
        private readonly JsonSerializerOptions _jsonOptions;

        public RedisCacheService(IDistributedCache? cache)
        {
            _cache = cache;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = ReferenceHandler.Preserve,
                WriteIndented = true
            };
        }

        public T? GetData<T>(string key)
        {
            var data = _cache?.GetString(key);
            if (data is null)
            {
                return default(T);
            }
            return JsonSerializer.Deserialize<T>(data, _jsonOptions)!;
        }

        public void SetData<T>(string key, T data)
        {
            var options = new DistributedCacheEntryOptions()
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7)
            };
            _cache?.SetString(key, JsonSerializer.Serialize(data, _jsonOptions), options);
        }
    }
}