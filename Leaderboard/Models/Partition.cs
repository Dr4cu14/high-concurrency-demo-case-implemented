using System.Collections.Concurrent;

namespace Leaderboard.Models
{


    /// <summary>
    /// Represents a shard/partition of customers.
    /// Each partition manages its own data and concurrency control.
    /// </summary>
    public class Partition
    {
        /// <summary>
        /// Thread-safe dictionary storing customers in this partition.
        /// </summary>
        public ConcurrentDictionary<long, Customer> Customers { get; } = new();

        /// <summary>
        /// ReaderWriterLockSlim to allow concurrent reads and exclusive writes within this partition.
        /// </summary>
        public ReaderWriterLockSlim RwLock { get; } = new(LockRecursionPolicy.NoRecursion);

        /// <summary>
        /// Flag indicating if the partition's data has changed and requires re-sorting.
        /// </summary>
        public bool CacheDirty { get; set; } = true;
    }
}
