using Leaderboard.Models;
using System.Collections.Concurrent;

namespace Leaderboard.Services;

/// <summary>
/// Leaderboard service implementation using ReaderWriterLockSlim.
/// Suitable for read-heavy workloads where concurrent reads are frequent.
/// </summary>
public class SingleDictionaryReaderWriterLockService : ILeaderboardService
{

    private readonly ConcurrentDictionary<long, Customer> _customers = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private List<RankedCustomer> _sortedRankedCustomers = new();
    private bool _cacheDirty = true;

    public Task<decimal> UpdateScoreAsync(long customerId, decimal scoreChange)
    {
        if (scoreChange < -1000 || scoreChange > 1000)
            throw new ArgumentOutOfRangeException(nameof(scoreChange), "Score change must be between -1000 and 1000");

        // Acquire write lock (exclusive)
        _lock.EnterWriteLock();
        try
        {
            var customer = _customers.AddOrUpdate(
                key: customerId,
                addValueFactory: id => new Customer { CustomerId = id, Score = scoreChange },
                updateValueFactory: (id, existing) =>
                {
                    existing.Score += scoreChange;
                    return existing;
                }
            );

            // Mark cache as dirty
            _cacheDirty = true;
            return Task.FromResult(customer.Score);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public Task<List<RankedCustomer>> GetByRankRangeAsync(int start, int end)
    {
        if (start < 1 || end < start)
            throw new ArgumentException("Invalid rank range");

        // Acquire read lock (shared)
        _lock.EnterReadLock();
        try
        {
            // Check if cache needs update
            if (_cacheDirty)
            {
                // Upgrade to write lock (must release read lock first)
                _lock.ExitReadLock();
                _lock.EnterWriteLock();
                try
                {
                    // Double-check (in case another thread updated it)
                    if (_cacheDirty)
                    {
                        UpdateCache();
                        _cacheDirty = false;
                    }
                }
                finally
                {
                    // Downgrade back to read lock
                    _lock.ExitWriteLock();
                    _lock.EnterReadLock();
                }
            }

            return Task.FromResult(_sortedRankedCustomers
                .Where(c => c.Rank >= start && c.Rank <= end)
                .ToList());
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public Task<List<RankedCustomer>> GetWithNeighborsAsync(long customerId, int high = 0, int low = 0)
    {
        if (high < 0 || low < 0)
            throw new ArgumentException("Neighbor count cannot be negative");

        _lock.EnterReadLock();
        try
        {
            if (_cacheDirty)
            {
                _lock.ExitReadLock();
                _lock.EnterWriteLock();
                try
                {
                    if (_cacheDirty)
                    {
                        UpdateCache();
                        _cacheDirty = false;
                    }
                }
                finally
                {
                    _lock.ExitWriteLock();
                    _lock.EnterReadLock();
                }
            }

            var target = _sortedRankedCustomers.FirstOrDefault(c => c.CustomerId == customerId);
            if (target == null)
                return Task.FromResult(new List<RankedCustomer>());

            var startIndex = Math.Max(0, target.Rank - high - 1);
            var endIndex = Math.Min(_sortedRankedCustomers.Count - 1, target.Rank + low - 1);
            var count = endIndex - startIndex + 1;

            return Task.FromResult(_sortedRankedCustomers.GetRange(startIndex, count));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private void UpdateCache()
    {
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
    }
}