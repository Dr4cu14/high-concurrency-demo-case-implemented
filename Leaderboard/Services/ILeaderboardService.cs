using Leaderboard.Models;
using System.Threading.Tasks;

namespace Leaderboard.Services
{
    /// <summary>
    /// Defines the contract for managing the leaderboard.
    /// </summary>
    public interface ILeaderboardService
    {
        /// <summary>
        /// Updates the score of a customer.
        /// </summary>
        /// <param name="customerId">The ID of the customer.</param>
        /// <param name="scoreChange">The amount to change the score by (positive to increase, negative to decrease).</param>
        /// <returns>The updated score of the customer.</returns>
        Task<decimal> UpdateScoreAsync(long customerId, decimal scoreChange);

        /// <summary>
        /// Gets customers within a specified rank range.
        /// </summary>
        /// <param name="start">The starting rank (inclusive).</param>
        /// <param name="end">The ending rank (inclusive).</param>
        /// <returns>A list of customers with their ranks and scores.</returns>
        Task<List<RankedCustomer>> GetByRankRangeAsync(int start, int end);

        /// <summary>
        /// Gets a customer and their neighboring customers by rank.
        /// </summary>
        /// <param name="customerId">The ID of the target customer.</param>
        /// <param name="high">The number of higher-ranked neighbors to include.</param>
        /// <param name="low">The number of lower-ranked neighbors to include.</param>
        /// <returns>A list of customers including the target and their neighbors.</returns>
        Task<List<RankedCustomer>> GetWithNeighborsAsync(long customerId, int high = 0, int low = 0);
    }
}
