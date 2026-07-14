namespace CloudMesh.DataBlocks.Streams;

/// <summary>
/// Wraps an exception thrown by user code (a selector, predicate, action, accumulator, etc.) inside a pipeline
/// stage, together with the item being processed when it failed. This is the exception type that faults
/// <see cref="FluentApi.IPipeline{TOriginalInput}.Completion"/> when no <c>OnError</c> handler is registered, and
/// the same instance is what an <c>OnError</c> handler receives as its <see cref="System.Exception"/> argument
/// (with the offending item passed separately).
/// </summary>
public sealed class PipelineException : Exception
{
    /// <summary>The item that was being processed when the stage threw, or <see langword="null"/> if unavailable.</summary>
    public object? Item { get; }

    /// <summary>
    /// Creates a <see cref="PipelineException"/> describing a stage failure.
    /// </summary>
    /// <param name="item">The item being processed when the failure occurred.</param>
    /// <param name="innerException">The original exception thrown by the user delegate.</param>
    public PipelineException(object? item, Exception innerException)
        : base(BuildMessage(item, innerException), innerException)
    {
        Item = item;
    }

    private static string BuildMessage(object? item, Exception innerException)
    {
        var itemText = item is null ? "<null>" : item.ToString();
        return $"A pipeline stage failed while processing item '{itemText}': {innerException.Message}";
    }
}
