namespace GarnetWrapper.Sample.Models.Responses
{
    public class CacheItemResponse
    {
        public string Key { get; set; }
        public object Value { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}