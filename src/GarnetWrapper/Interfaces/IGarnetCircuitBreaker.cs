namespace GarnetWrapper.Interfaces
{
    /// <summary>
    /// Interface for circuit breaker implementation
    /// </summary>
    public interface IGarnetCircuitBreaker
    {
        /// <summary>
        /// Executes an action with circuit breaker protection
        /// </summary>
        /// <typeparam name="T">Return type of the action</typeparam>
        /// <param name="action">Action to execute</param>
        /// <param name="circuitKey">Optional circuit key for separate circuit breakers</param>
        /// <returns>Result of the action</returns>
        Task<T> ExecuteAsync<T>(Func<Task<T>> action, string circuitKey = "default");
    }
}