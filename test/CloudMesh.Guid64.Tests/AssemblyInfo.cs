using Xunit;

// Guid64.Engine keeps process-wide state (last timestamp, sequence, node id). Run serially so the
// monotonic/uniqueness assertions and the NodeId mutation test don't interleave.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
