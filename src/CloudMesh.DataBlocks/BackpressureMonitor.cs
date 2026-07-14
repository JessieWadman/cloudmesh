namespace CloudMesh.DataBlocks
{
    /// <summary>
    /// A process-wide hook for observing backpressure. When a block's mailbox is full and a send has to wait (or a
    /// fan-out router can't place a message immediately), the block's <see cref="IDataBlockRef.Path"/> is reported
    /// here — wire it up to a metric or log to spot overloaded blocks.
    /// </summary>
    public static class BackpressureMonitor
    {
        /// <summary>
        /// Invoked with the <see cref="IDataBlockRef.Path"/> of a block that is experiencing backpressure. Assign
        /// a callback to observe it; exceptions thrown by the callback are swallowed.
        /// </summary>
        public static Action<string>? OnBackpressureDetected;
    }
}
