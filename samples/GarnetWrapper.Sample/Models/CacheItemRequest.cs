using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace GarnetWrapper.Sample.Models.Requests
{
    public class CacheItemRequest
    {
        [FromQuery(Name = "Value")]
        [JsonProperty("Value", Required = Required.Always)]
        public object Value { get; set; }

        [FromQuery(Name = "ExpiryTime")]
        [JsonProperty("ExpiryTime", Required = Required.Always)]
        public TimeSpan? ExpiryTime { get; set; }
    }
}