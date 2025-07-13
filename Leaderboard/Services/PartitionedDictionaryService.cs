using Leaderboard.Models;
using System.Collections.Concurrent;

namespace Leaderboard.Services;

/// <summary>
/// Leaderboard service implementation using partitioned ConcurrentDictionary.
/// Suitable for high concurrency with heavy write operations by reducing lock contention.
/// </summary>
public class PartitionedDictionaryService : ILeaderboardService
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<long, Customer>> _partitions;
    private readonly int _partitionCount;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private List<RankedCustomer> _sortedRankedCustomers = new();
    private DateTime _lastCacheUpdate = DateTime.MinValue;

    public PartitionedDictionaryService(int partitionCount = 16)
    {
        _partitionCount = partitionCount;
        _partitions = new ConcurrentDictionary<int, ConcurrentDictionary<long, Customer>>();

        // Pre-initialize partitions for better performance
        for (int i = 0; i < partitionCount; i++)
        {
            _partitions[i] = new ConcurrentDictionary<long, Customer>();
        }
    }

    public Task<decimal> UpdateScoreAsync(long customerId, decimal scoreChange)
    {
        if (scoreChange < -1000 || scoreChange > 1000)
            throw new ArgumentOutOfRangeException(nameof(scoreChange), "Score change must be between -1000 and 1000");

        // Calculate partition key using modulo
        var partitionKey = (int)(customerId % _partitionCount);

        // Get or add partition
        var partition = _partitions.GetOrAdd(
            partitionKey,
            _ => new ConcurrentDictionary<long, Customer>()
        );

        // Update customer score within the partition
        var updatedCustomer = partition.AddOrUpdate(
            key: customerId,
            addValueFactory: id => new Customer { CustomerId = id, Score = scoreChange },
            updateValueFactory: (id, existing) =>
            {
                existing.Score += scoreChange;
                return existing;
            }
        );

        // Trigger cache update
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
        if (DateTime.Now - _lastCacheUpdate < TimeSpan.FromMilliseconds(100))
            return _sortedRankedCustomers;

        await _cacheLock.WaitAsync();
        try
        {
            if (DateTime.Now - _lastCacheUpdate < TimeSpan.FromMilliseconds(100))
                return _sortedRankedCustomers;

            // Merge all partitions
            var allCustomers = new List<Customer>();
            foreach (var partition in _partitions.Values)
            {
                allCustomers.AddRange(partition.Values);
            }

            var eligibleCustomers = allCustomers
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
            _lastCacheUpdate = DateTime.MinValue;
        }
        finally
        {
            _cacheLock.Release();
        }
    }
}