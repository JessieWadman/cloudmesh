namespace CloudMesh.DataBlocks.Streams.FluentApi;

/// <summary>
/// The initial stage of a self-pumping source (an <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/> or a
/// <see cref="System.Threading.Channels.ChannelReader{T}"/>). It is a normal <see cref="IPipelineStage{TOriginalInput, TCurrent}"/>;
/// once built, the source pumps itself, and disposing the pipeline awaits the source and drains every stage.
/// </summary>
/// <typeparam name="TOriginalInput">The pipeline's original input type.</typeparam>
/// <typeparam name="TCurrent">The item type flowing out of the source.</typeparam>
public interface IEnumerableSourceBuilder<TOriginalInput, TCurrent> : IPipelineStage<TOriginalInput, TCurrent> { }
