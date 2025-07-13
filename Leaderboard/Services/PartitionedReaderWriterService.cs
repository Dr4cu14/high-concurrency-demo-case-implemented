using Leaderboard.Models;
using System.Collections.Concurrent;

namespace Leaderboard.Services;

/// <summary>
/// Leaderboard service implementation using:
/// 1. Partitioning strategy to reduce lock contention across multiple shards.
/// 2. ReaderWriterLockSlim for each partition to allow concurrent reads and exclusive writes.
/// 
/// Optimal for high-concurrency scenarios with heavy write operations and large datasets.
/// Balances write scalability (via partitioning) with read performance (via read-write locks).
/// </summary>
public class PartitionedReaderWriterService : ILeaderboardService
{
    /// <summary>
    /// Dictionary of partitions, keyed by partition ID.
    /// Each partition manages a subset of customers.
    /// </summary>
    private readonly ConcurrentDictionary<int, Partition> _partitions;

    /// <summary>
    /// Number of partitions/shards. Adjust based on expected concurrency and data size.
    /// </summary>
    private readonly int _partitionCount;

    /// <summary>
    /// Global lock to control access to the sorted leaderboard cache.
    /// </summary>
    private readonly SemaphoreSlim _globalCacheLock = new(1, 1);

    /// <summary>
    /// Cached list of ranked customers, sorted by score and customer ID.
    /// </summary>
    private List<RankedCustomer> _sortedRankedCustomers = new();

    /// <summary>
    /// Flag indicating if the global leaderboard cache needs to be updated.
    /// </summary>
    private bool _globalCacheDirty = true;

    /// <summary>
    /// Initializes a new instance with specified partition count.
    /// </summary>
    /// <param name="partitionCount">Number of partitions (default: 16).</param>
    public PartitionedReaderWriterService(int partitionCount = 16)
    {
        _partitionCount = partitionCount;
        _partitions = new ConcurrentDictionary<int, Partition>();

        // Pre-initialize partitions to avoid contention during first access
        for (int i = 0; i < partitionCount; i++)
        {
            _partitions[i] = new Partition();
        }
    }

    /// <summary>
    /// Updates the score of a customer.
    /// This is a write operation that acquires a write lock on the customer's partition.
    /// </summary>
    /// <param name="customerId">ID of the customer.</param>
    /// <param name="scoreChange">Amount to change the score by (positive to increase, negative to decrease).</param>
    /// <returns>The updated score.</returns>
    public Task<decimal> UpdateScoreAsync(long customerId, decimal scoreChange)
    {
        if (scoreChange < -1000 || scoreChange > 1000)
            throw new ArgumentOutOfRangeException(nameof(scoreChange), "Score change must be between -1000 and 1000");

        // Determine partition using modulo hashing
        var partitionKey = (int)(customerId % _partitionCount);
        var partition = _partitions[partitionKey];

        // Acquire write lock (exclusive) for this partition
        partition.RwLock.EnterWriteLock();
        try
        {
            // Update customer score within the partition's dictionary
            var customer = partition.Customers.AddOrUpdate(
                key: customerId,
                addValueFactory: id => new Customer { CustomerId = id, Score = scoreChange },
                updateValueFactory: (id, existing) =>
                {
                    existing.Score += scoreChange;
                    return existing;
                }
            );

            // Mark partition cache as dirty (requires re-sorting)
            partition.CacheDirty = true;
            // Mark global cache as dirty since leaderboard order may have changed
            _globalCacheDirty = true;

            return Task.FromResult(customer.Score);
        }
        finally
        {
            // Release write lock
            partition.RwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Retrieves customers within a specified rank range.
    /// This is a read operation that uses shared locks for concurrent access.
    /// </summary>
    /// <param name="start">Starting rank (inclusive).</param>
    /// <param name="end">Ending rank (inclusive).</param>
    /// <returns>List of customers with their ranks.</returns>
    public async Task<List<RankedCustomer>> GetByRankRangeAsync(int start, int end)
    {
        if (start < 1 || end < start)
            throw new ArgumentException("Invalid rank range");

        // Acquire global read lock to check cache state
        await _globalCacheLock.WaitAsync();
        try
        {
            if (_globalCacheDirty)
            {
                // Release read lock and acquire write lock to update cache
                _globalCacheLock.Release();
                await _globalCacheLock.WaitAsync();

                try
                {
                    // Double-check to avoid redundant updates
                    if (_globalCacheDirty)
                    {
                        UpdateGlobalCache();
                        _globalCacheDirty = false;
                    }
                }
                finally
                {
                    // Ensure lock is released even if an exception occurs
                }
            }

            // Return cached results
            return _sortedRankedCustomers
                .Where(c => c.Rank >= start && c.Rank <= end)
                .ToList();
        }
        finally
        {
            // Release global lock
            _globalCacheLock.Release();
        }
    }

    /// <summary>
    /// Retrieves a customer and their neighboring customers by rank.
    /// This is a read operation that uses shared locks for concurrent access.
    /// </summary>
    /// <param name="customerId">ID of the target customer.</param>
    /// <param name="high">Number of higher-ranked neighbors to include.</param>
    /// <param name="low">Number of lower-ranked neighbors to include.</param>
    /// <returns>List of customers including target and neighbors.</returns>
    public async Task<List<RankedCustomer>> GetWithNeighborsAsync(long customerId, int high = 0, int low = 0)
    {
        if (high < 0 || low < 0)
            throw new ArgumentException("Neighbor count cannot be negative");

        // Acquire global read lock
        await _globalCacheLock.WaitAsync();
        try
        {
            if (_globalCacheDirty)
            {
                // Upgrade to write lock if cache needs update
                _globalCacheLock.Release();
                await _globalCacheLock.WaitAsync();

                try
                {
                    if (_globalCacheDirty)
                    {
                        UpdateGlobalCache();
                        _globalCacheDirty = false;
                    }
                }
                finally
                {
                    // Ensure lock is released
                }
            }

            // Retrieve target customer and neighbors from cache
            var target = _sortedRankedCustomers.FirstOrDefault(c => c.CustomerId == customerId);
            if (target == null)
                return new List<RankedCustomer>();

            var startIndex = Math.Max(0, target.Rank - high - 1);
            var endIndex = Math.Min(_sortedRankedCustomers.Count - 1, target.Rank + low - 1);
            var count = endIndex - startIndex + 1;

            return _sortedRankedCustomers.GetRange(startIndex, count);
        }
        finally
        {
            // Release global lock
            _globalCacheLock.Release();
        }
    }

    /// <summary>
    /// Updates the global leaderboard cache by merging and sorting all partitions.
    /// This method acquires read locks on all partitions to ensure data consistency during the merge.
    /// </summary>
    private void UpdateGlobalCache()
    {
        var allCustomers = new List<Customer>();

        // Iterate over all partitions and collect customers under read locks
        foreach (var partition in _partitions.Values)
        {
            partition.RwLock.EnterReadLock();
            try
            {
                // Add all customers from this partition to the global list
                allCustomers.AddRange(partition.Customers.Values);
            }
            finally
            {
                // Release read lock for this partition
                partition.RwLock.ExitReadLock();
            }
        }

        // Sort customers by score (descending) and customer ID (ascending)
        var eligibleCustomers = allCustomers
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.CustomerId)
            .ToList();

        // Assign ranks and build the final ranked list
        var ranked = new List<RankedCustomer>();
        for (var i = 0; i < eligibleCustomers.Count; i++)
        {
            ranked.Add(new RankedCustomer
            {
                CustomerId = eligibleCustomers[i].CustomerId,
                Score = eligibleCustomers[i].Score,
                Rank = i + 1 // Rank starts at 1
            });
        }

        // Update the global cache
        _sortedRankedCustomers = ranked;
    }
}