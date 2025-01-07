namespace GarnetWrapper.Tests.Integration
{
    /// <summary>
    /// Exception thrown when a test should be skipped
    /// </summary>
    public class SkipException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the SkipException class
        /// </summary>
        /// <param name="message">The message that describes the error</param>
        public SkipException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the SkipException class
        /// </summary>
        /// <param name="message">The message that describes the error</param>
        /// <param name="innerException">The exception that is the cause of the current exception</param>
        public SkipException(string message, Exception innerException) : base(message, innerException)
        {
        }
    } 
}