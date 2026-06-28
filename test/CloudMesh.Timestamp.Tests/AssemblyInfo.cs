using Xunit;

// FastClock keeps process-wide state (origin, last-adjustment, interval). Run serially so the interval
// mutations in its tests don't interleave with other tests' reads.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
