using Leaderboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace Leaderboard.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LeaderboardController : ControllerBase
    {
        private readonly ILeaderboardService _service;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        public LeaderboardController(ILeaderboardService service)
        {
            _service = service;
        }

        /// <summary>
        /// Updates the score of a customer.
        /// </summary>
        /// <param name="customerid">The ID of the customer.</param>
        /// <param name="score">The amount to change the score by.</param>
        /// <returns>The updated score.</returns>
        [HttpPost("customer/{customerid}/score/{score}")]
        public async Task<IActionResult> UpdateScore(
            [FromRoute] long customerid,
            [FromRoute] decimal score)
        {
            if (customerid <= 0)
                return BadRequest("CustomerID must be a positive integer");

            try
            {
                var newScore = await _service.UpdateScoreAsync(customerid, score);
                return Ok(newScore);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Gets customers within a specified rank range.
        /// </summary>
        /// <param name="start">The starting rank.</param>
        /// <param name="end">The ending rank.</param>
        /// <returns>A list of customers with their ranks and scores.</returns>
        [HttpGet("leaderboard")]
        public async Task<IActionResult> GetByRank(
            [FromQuery] int start,
            [FromQuery] int end)
        {
            var result = await _service.GetByRankRangeAsync(start, end);
            return Ok(result);
        }

        /// <summary>
        /// Gets a customer and their neighboring customers by rank.
        /// </summary>
        /// <param name="customerid">The ID of the target customer.</param>
        /// <param name="high">The number of higher-ranked neighbors (default: 0).</param>
        /// <param name="low">The number of lower-ranked neighbors (default: 0).</param>
        /// <returns>A list of customers including the target and their neighbors.</returns>
        [HttpGet("leaderboard/{customerid}")]
        public async Task<IActionResult> GetWithNeighbors(
            [FromRoute] long customerid,
            [FromQuery] int high = 0,
            [FromQuery] int low = 0)
        {
            var result = await _service.GetWithNeighborsAsync(customerid, high, low);
            return Ok(result);
        }
    }
}
