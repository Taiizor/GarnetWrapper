namespace GarnetWrapper.Options
{
    public class GarnetOptions
    {
        public string ConnectionString { get; set; } = "localhost:6379";
        public int DatabaseId { get; set; } = 0;
        public bool EnableCompression { get; set; } = false;
        public TimeSpan? DefaultExpiry { get; set; }
        public int MaxRetries { get; set; } = 3;
        public int RetryTimeout { get; set; } = 5000; // milliseconds
    }
}