using CloudMesh.DataBlocks.Streams;

namespace CloudMesh.DataBlocks.Streams.FluentApi;

// The deferred recipe: an ordered list of stage factories (source → sink), plus the sink and (optional) source pump.
internal sealed class PipelineDef
{
    public readonly List<Func<ICanSubmit, DataBlock>> Stages = new();

    // An externally-supplied sink (To(ICanSubmit)); not owned/disposed by the pipeline.
    public ICanSubmit? Sink;

    // A factory for a pipeline-owned sink block (To(action)/To(ChannelWriter)). Deferred to Build() time so the
    // sink block can close over the ErrorSink created there.
    public Func<DataBlock>? SinkFactory;

    public bool OwnsSink;
    public Func<ICanSubmit, CancellationToken, Task>? SourcePump;

    // The resilient per-item error handler registered via OnError, if any.
    public Action<Exception, object?>? ErrorHandler;

    // The shared error observation point, created at Build() time. Stage factories close over this field, so it
    // must be assigned before the factories run (see Final.Build). Blocks route caught user-code exceptions here.
    public PipelineErrorSink? ErrorSink;
}
