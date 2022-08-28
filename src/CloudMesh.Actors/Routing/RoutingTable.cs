using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;

namespace CloudMesh.Actors.Routing
{
    public class RoutingTable : IRoutingTable
    {
        private ImmutableArray<RoutingTableEntry> entries = ImmutableArray<RoutingTableEntry>.Empty;
        private readonly ReaderWriterLockSlim locker = new();
        private readonly HashSet<IRoutingTableUpdate> watchers = new();
        private readonly TaskCompletionSource initialRoutingTableSet = new();

        public RoutingTable()
        {
        }

        public ValueTask UpdateAsync(ImmutableArray<RoutingTableEntry> newEntries)
        {
            var orderedList = newEntries.OrderBy(e => e.InstanceId).ToImmutableArray();
            bool hasChanged = false;
            locker.EnterWriteLock();
            try
            {
                var isFirstTime = entries.Length == 0;
                hasChanged = !orderedList.SequenceEqual(entries);
                entries = orderedList;
                if (isFirstTime && entries.Length > 0)
                    initialRoutingTableSet.SetResult();
            }
            finally
            {
                locker.ExitWriteLock();
            }

            if (hasChanged)
            {
                // We snapshot the watcher list, so we release the lock quickly
                IRoutingTableUpdate[] _watchers;
                lock (watchers)
                {
                    _watchers = this.watchers.ToArray();
                }

                foreach (var watcher in _watchers)
                    watcher.RoutingTableUpdated(orderedList);
            }

            Debug.WriteLine($"Routing table updated:\n" + string.Join("\n", orderedList
                .Select(entry => $"    SN={entry.ServiceName}, IP={entry.IpAddress}, IsLocal={entry.IsLocal}")));

            return ValueTask.CompletedTask;
        }

        public ImmutableArray<RoutingTableEntry> GetAll() => entries;
        public ImmutableArray<RoutingTableEntry> GetByService(string serviceName)
            => entries.Where(e => e.ServiceName == serviceName).ToImmutableArray();

        public void RegisterNotifications(IRoutingTableUpdate target)
        {
            lock (watchers)
            {
                if (!watchers.Contains(target))
                    watchers.Add(target);
            }
        }

        public void UnregisterNotifications(IRoutingTableUpdate target)
        {
            lock (watchers)
            {
                if (watchers.Contains(target))
                    watchers.Remove(target);
            }
        }

        private Task GetInitialRoutingTableTask()
        {
            locker.EnterReadLock();
            try
            {
                if (entries.Length > 0)
                    return Task.CompletedTask;
                return initialRoutingTableSet.Task;
            }
            finally
            {
                locker.ExitReadLock();
            }
        }

        public ValueTask<bool> WaitForInitialRoutingTableAsync(TimeSpan timeout)
        {
            var task = GetInitialRoutingTableTask();
            if (task.IsCompleted)
                return new(true);
            return new(Impl());

            async Task<bool> Impl()
            {
                return await Task.WhenAny(new[] { task, Task.Delay(timeout) }) == task;
            }
        }
    }
}
