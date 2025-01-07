using GarnetWrapper.Interfaces;
using GarnetWrapper.Sample.Models.Requests;
using GarnetWrapper.Sample.Models.Responses;
using Microsoft.AspNetCore.Mvc;

namespace GarnetWrapper.Sample.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Produces("application/json")]
    public class CacheController : ControllerBase
    {
        private readonly IGarnetClient _cache;
        private readonly ILogger<CacheController> _logger;

        public CacheController(IGarnetClient cache, ILogger<CacheController> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Gets a value from cache by key
        /// </summary>
        [HttpGet("{key}")]
        [ProducesResponseType(typeof(CacheItemResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(string key)
        {
            object value = await _cache.GetAsync<object>(key);
            if (value == null)
            {
                return NotFound(new CacheItemResponse
                {
                    Key = key,
                    Success = false,
                    Message = "Item not found"
                });
            }

            TimeSpan? ttl = await _cache.GetTimeToLiveAsync(key);

            return Ok(new CacheItemResponse
            {
                Key = key,
                Value = value,
                ExpiresAt = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : null,
                Success = true
            });
        }

        /// <summary>
        /// Sets a value in cache with optional expiry
        /// </summary>
        [HttpPost("{key}")]
        [ProducesResponseType(typeof(CacheItemResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Set(string key, [FromBody] CacheItemRequest request)
        {
            if (request.Value == null)
            {
                return BadRequest(new CacheItemResponse
                {
                    Key = key,
                    Success = false,
                    Message = "Value cannot be null"
                });
            }

            await _cache.SetAsync(key, request.Value, request.ExpiryTime);

            return Ok(new CacheItemResponse
            {
                Key = key,
                Value = request.Value,
                ExpiresAt = request.ExpiryTime.HasValue ? DateTime.UtcNow.Add(request.ExpiryTime.Value) : null,
                Success = true
            });
        }

        /// <summary>
        /// Deletes a value from cache
        /// </summary>
        [HttpDelete("{key}")]
        [ProducesResponseType(typeof(CacheItemResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Delete(string key)
        {
            await _cache.DeleteAsync(key);

            return Ok(new CacheItemResponse
            {
                Key = key,
                Success = true,
                Message = "Item deleted successfully"
            });
        }

        /// <summary>
        /// Checks if a key exists in cache
        /// </summary>
        [HttpHead("{key}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Exists(string key)
        {
            bool exists = await _cache.ExistsAsync(key);
            return exists ? Ok() : NotFound();
        }
    }
}