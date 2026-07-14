namespace CloudMesh.DataBlocks.Streams.FluentApi;

// The initial stage of a source — carries the empty source-builder marker interfaces from the sketch.
internal sealed class SourceStage<T> : Stage<T, T>, IManualSourceBuilder<T>, IEnumerableSourceBuilder<T, T>
{
    internal SourceStage(PipelineDef def) : base(def) { }
}