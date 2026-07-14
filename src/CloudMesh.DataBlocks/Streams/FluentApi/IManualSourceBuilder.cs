namespace CloudMesh.DataBlocks.Streams.FluentApi;

/// <summary>
/// The initial stage of a manual-push source. It is a normal <see cref="IPipelineStage{TOriginalInput, TCurrent}"/>
/// whose input type equals its output type; the built pipeline is fed by calling
/// <see cref="IPipeline{TOriginalInput}.PushAsync"/>.
/// </summary>
/// <typeparam name="T">The item type pushed into the pipeline.</typeparam>
public interface IManualSourceBuilder<T> : IPipelineStage<T, T> { }
