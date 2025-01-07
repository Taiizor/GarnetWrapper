namespace GarnetWrapper.Interfaces
{
    public interface IGarnetClient : IDisposable
    {
        Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null);
        Task<T> GetAsync<T>(string key);
        Task<bool> DeleteAsync(string key);
        Task<bool> ExistsAsync(string key);
        Task<TimeSpan?> GetTimeToLiveAsync(string key);
        Task<bool> SetExpiryAsync(string key, TimeSpan expiry);
        Task<long> IncrementAsync(string key, long value = 1);
        Task<long> DecrementAsync(string key, long value = 1);
        Task<bool> LockAsync(string key, TimeSpan expiryTime);
        Task<bool> UnlockAsync(string key);
        IAsyncEnumerable<string> ScanAsync(string pattern);
    }
}