using System.Threading.Channels;

namespace CloudMesh.DataBlocks.Streams.FluentApi;

/// <summary>
/// Terminal operators — pick a sink for the stage's items and get an <see cref="IPipelineFinal{TOriginalInput}"/>
/// to build the pipeline.
/// </summary>
/// <typeparam name="TOriginalInput">The pipeline's original input type.</typeparam>
/// <typeparam name="TCurrent">The item type consumed by the sink.</typeparam>
public interface IPipelineTargets<TOriginalInput, TCurrent>
{
    /// <summary>Sends each item to an existing block (or any <see cref="ICanSubmit"/>). The target is not owned or disposed by the pipeline.</summary>
    /// <param name="target">The block that receives each item.</param>
    /// <returns>A terminated pipeline ready to build.</returns>
    IPipelineFinal<TOriginalInput> To(ICanSubmit target);

    /// <summary>Consumes each item with a synchronous action.</summary>
    /// <param name="action">The action run on each item.</param>
    /// <returns>A terminated pipeline ready to build.</returns>
    IPipelineFinal<TOriginalInput> To(Action<TCurrent> action);

    /// <summary>Consumes each item with an async action. The action is awaited, so a slow sink applies backpressure upstream.</summary>
    /// <param name="action">The async action run on each item.</param>
    /// <returns>A terminated pipeline ready to build.</returns>
    IPipelineFinal<TOriginalInput> To(Func<TCurrent, CancellationToken, ValueTask> action);

    /// <summary>
    /// Writes each item to a <see cref="ChannelWriter{T}"/>, and completes the writer when the pipeline drains, so a
    /// downstream <see cref="ChannelReader{T}.ReadAllAsync(CancellationToken)"/> loop terminates. A bounded channel
    /// applies backpressure upstream.
    /// </summary>
    /// <param name="writer">The channel writer to feed; completed on drain.</param>
    /// <returns>A terminated pipeline ready to build.</returns>
    IPipelineFinal<TOriginalInput> To(ChannelWriter<TCurrent> writer);
}
