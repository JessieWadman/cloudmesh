using CloudMesh.DataBlocks.Streams;

namespace CloudMesh.DataBlocks.Streams.FluentApi;

internal sealed class Final<TOriginalInput> : IPipelineFinal<TOriginalInput>
{
    private readonly PipelineDef def;
    public Final(PipelineDef def) => this.def = def;

    public IPipeline<TOriginalInput> Build()
    {
        // Create the shared error observation point first: stage/sink factories close over def.ErrorSink, so it
        // must exist before any of them run.
        var errorSink = new PipelineErrorSink(def.ErrorHandler);
        def.ErrorSink = errorSink;

        // Wire back-to-front: start at the sink and hand each stage its already-built downstream.
        var owned = new List<DataBlock>();

        ICanSubmit current;
        if (def.SinkFactory is not null)
        {
            var sinkBlock = def.SinkFactory();
            owned.Add(sinkBlock);
            current = sinkBlock;
        }
        else
        {
            current = def.Sink ?? throw new InvalidOperationException("Pipeline has no sink; call To(...).");
        }

        for (var i = def.Stages.Count - 1; i >= 0; i--)
        {
            var block = def.Stages[i](current);
            owned.Add(block);
            current = block;
        }

        return new RunningPipeline<TOriginalInput>(current, owned, def.SourcePump, errorSink);
    }
}
