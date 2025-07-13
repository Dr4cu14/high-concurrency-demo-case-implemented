using Leaderboard.Models;
using System.Collections.Concurrent;

namespace Leaderboard.Services
{
    /// <summary>
    /// Leaderboard service implementation using a single ConcurrentDictionary.
    /// Suitable for moderate concurrency with balanced read/write operations.
    /// </summary>
    public class SingleDictionaryService: ILeaderboardService
    {

        private readonly ConcurrentDictionary<long, Customer> _customers = new();
        private readonly SemaphoreSlim _cacheLock = new(1, 1);
        private List<RankedCustomer> _sortedRankedCustomers = new();
        private DateTime _lastCacheUpdate = DateTime.MinValue;

        public Task<decimal> UpdateScoreAsync(long customerId, decimal scoreChange)
        {
            if (scoreChange < -1000 || scoreChange > 1000)
                throw new ArgumentOutOfRangeException(nameof(scoreChange), "Score change must be between -1000 and 1000");

            var updatedCustomer = _customers.AddOrUpdate(
                key: customerId,
                addValueFactory: id => new Customer { CustomerId = id, Score = scoreChange },
                updateValueFactory: (id, existing) =>
                {
                    existing.Score += scoreChange;
                    return existing;
                }
            );

            // Trigger cache update (non-blocking)
            _ = UpdateCacheAsync();

            return Task.FromResult(updatedCustomer.Score);
        }

        public async Task<List<RankedCustomer>> GetByRankRangeAsync(int start, int end)
        {
            if (start < 1 || end < start)
                throw new ArgumentException("Invalid rank range");

            var rankedCustomers = await GetSortedRankedCustomersAsync();
            return rankedCustomers
                .Where(c => c.Rank >= start && c.Rank <= end)
                .ToList();
        }

        public async Task<List<RankedCustomer>> GetWithNeighborsAsync(long customerId, int high = 0, int low = 0)
        {
            if (high < 0 || low < 0)
                throw new ArgumentException("Neighbor count cannot be negative");

            var rankedCustomers = await GetSortedRankedCustomersAsync();
            var target = rankedCustomers.FirstOrDefault(c => c.CustomerId == customerId);
            if (target == null)
                return new List<RankedCustomer>();

            var startIndex = Math.Max(0, target.Rank - high - 1);
            var endIndex = Math.Min(rankedCustomers.Count - 1, target.Rank + low - 1);
            var count = endIndex - startIndex + 1;

            return rankedCustomers.GetRange(startIndex, count);
        }

        private async Task<List<RankedCustomer>> GetSortedRankedCustomersAsync()
        {
            // Return cached results if not expired
            if (DateTime.Now - _lastCacheUpdate < TimeSpan.FromMilliseconds(100))
                return _sortedRankedCustomers;

            // Lock to update cache
            await _cacheLock.WaitAsync();
            try
            {
                if (DateTime.Now - _lastCacheUpdate < TimeSpan.FromMilliseconds(100))
                    return _sortedRankedCustomers;

                var eligibleCustomers = _customers.Values
                    .Where(c => c.Score > 0)
                    .OrderByDescending(c => c.Score)
                    .ThenBy(c => c.CustomerId)
                    .ToList();

                var ranked = new List<RankedCustomer>();
                for (var i = 0; i < eligibleCustomers.Count; i++)
                {
                    ranked.Add(new RankedCustomer
                    {
                        CustomerId = eligibleCustomers[i].CustomerId,
                        Score = eligibleCustomers[i].Score,
                        Rank = i + 1
                    });
                }

                _sortedRankedCustomers = ranked;
                _lastCacheUpdate = DateTime.Now;
                return ranked;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private async Task UpdateCacheAsync()
        {
            await _cacheLock.WaitAsync();
            try
            {
                _lastCacheUpdate = DateTime.MinValue; // Force cache refresh
            }
            finally
            {
                _cacheLock.Release();
            }
        }

    }
}
