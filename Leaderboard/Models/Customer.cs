namespace Leaderboard.Models
{
    /// <summary>
    /// Represents a customer with a score.
    /// </summary>
    public class Customer
    {
        /// <summary>
        /// 
        /// </summary>
        public required long CustomerId { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public required decimal Score { get; set; } = 0; 
    }
}
