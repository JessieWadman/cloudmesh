using System.Threading.Channels;
using CloudMesh.DataBlocks.Streams;
using CloudMesh.DataBlocks.Streams.FluentApi;

namespace CloudMesh.DataBlocks;

// ===============================================================================================================
// The fluent API surface (your sketch: TOriginalInput/TCurrent naming + IArrayPipelineStage.Reduce. One fix —
// Map/MapAsync operate on TCurrent, not TOriginalInput, so stages actually chain.)
// ===============================================================================================================

/// <summary>
/// The entry point for the fluent "data streams" pipeline API over <see cref="DataBlock"/>. Pick a source, chain
/// operators, choose a sink with <c>To(...)</c>, then <c>Build()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every stage is a <see cref="DataBlock"/> that awaits its downstream submit, so <b>backpressure propagates
/// upstream</b> through the whole chain. Building wires the stages back-to-front (sink first). Disposing the built
/// pipeline drains and flushes every stage in order (so buffered/aggregated items are emitted).
/// </para>
/// <para>
/// <b>Error model.</b> When a stage's user code throws, the offending item is dropped. If an <c>OnError</c> handler
/// was registered it is invoked and the pipeline keeps running (resilient); otherwise the first failure faults
/// <see cref="Streams.FluentApi.IPipeline{TOriginalInput}.Completion"/>. Disposing always drains cleanly and never
/// re-throws pipeline faults.
/// </para>
/// </remarks>
public static class Pipeline
{
    /// <summary>A source you feed by calling <see cref="Streams.FluentApi.IPipeline{TOriginalInput}.PushAsync"/> on the built pipeline.</summary>
    /// <typeparam name="T">The item type pushed into the pipeline.</typeparam>
    /// <returns>A source builder to chain operators onto.</returns>
    public static IManualSourceBuilder<T> OnManualPush<T>() => new SourceStage<T>(new PipelineDef());

    /// <summary>A source that pumps an <see cref="IAsyncEnumerable{T}"/> once the pipeline is built.</summary>
    /// <typeparam name="T">The item type produced by the source.</typeparam>
    /// <param name="source">The async sequence to pump; each element is submitted to the head of the pipeline.</param>
    /// <returns>A source builder to chain operators onto.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public static IEnumerableSourceBuilder<T, T> From<T>(IAsyncEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var def = new PipelineDef
        {
            SourcePump = async (head, ct) =>
            {
                await foreach (var item in source.WithCancellation(ct))
                    await head.SubmitAsync(item, null);
            }
        };
        return new SourceStage<T>(def);
    }

    /// <summary>
    /// A source that pumps a <see cref="ChannelReader{T}"/> once the pipeline is built: it drains the reader via
    /// <see cref="ChannelReader{T}.ReadAllAsync(CancellationToken)"/> and, when the channel completes, drains the
    /// pipeline and completes <see cref="Streams.FluentApi.IPipeline{TOriginalInput}.Completion"/>.
    /// </summary>
    /// <typeparam name="T">The item type produced by the channel.</typeparam>
    /// <param name="reader">The channel reader to pump; each item is submitted to the head of the pipeline.</param>
    /// <returns>A source builder to chain operators onto.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is null.</exception>
    public static IEnumerableSourceBuilder<T, T> From<T>(ChannelReader<T> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var def = new PipelineDef
        {
            SourcePump = async (head, ct) =>
            {
                await foreach (var item in reader.ReadAllAsync(ct))
                    await head.SubmitAsync(item, null);
            }
        };
        return new SourceStage<T>(def);
    }
}
