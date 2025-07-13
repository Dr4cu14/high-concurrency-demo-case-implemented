namespace Leaderboard.Models
{
    // <summary>
    /// Represents a customer with their rank in the leaderboard.
    /// </summary>
    public class RankedCustomer : Customer
    {
        public int Rank { get; set; }
    }
}
