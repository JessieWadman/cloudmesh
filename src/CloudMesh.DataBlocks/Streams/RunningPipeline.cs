using CloudMesh.DataBlocks.Streams.FluentApi;

namespace CloudMesh.DataBlocks.Streams;

internal sealed class RunningPipeline<TOriginalInput> : IPipeline<TOriginalInput>
{
    private readonly ICanSubmit _head;
    private readonly List<DataBlock> _owned;   // [sink .. head]
    private readonly CancellationTokenSource? _pumpCts;
    private readonly Task? _pump;
    private readonly PipelineErrorSink _errorSink;

    // Guards the single drain: whichever of the source-pump completion or DisposeAsync gets here first drains and
    // completes the error sink; the other awaits that same task.
    private readonly object _drainGate = new();
    private Task? _drain;

    public RunningPipeline(ICanSubmit head, List<DataBlock> owned,
        Func<ICanSubmit, CancellationToken, Task>? sourcePump, PipelineErrorSink errorSink)
    {
        _head = head;
        _owned = owned;
        _errorSink = errorSink;

        if (sourcePump is not null)
        {
            _pumpCts = new CancellationTokenSource();
            // A self-pumping source drains and completes on its own once exhausted, so a consumer can observe the
            // result via Completion without disposing.
            _pump = PumpThenDrainAsync(sourcePump);
        }
    }

    public Task Completion => _errorSink.Completion;

    public ValueTask PushAsync(TOriginalInput input, CancellationToken ct = default) => _head.SubmitAsync(input, null);

    private async Task PumpThenDrainAsync(Func<ICanSubmit, CancellationToken, Task> sourcePump)
    {
        try
        {
            await sourcePump(_head, _pumpCts!.Token);
        }
        catch (OperationCanceledException)
        {
        }

        await DrainAsync();
    }

    private Task DrainAsync()
    {
        lock (_drainGate)
        {
            _drain ??= DrainCoreAsync();
        }
        return _drain;
    }

    private async Task DrainCoreAsync()
    {
        try
        {
            // Dispose head-first so each stage drains into a still-alive downstream.
            for (var i = _owned.Count - 1; i >= 0; i--)
                await _owned[i].DisposeAsync();
        }
        finally
        {
            // Observe faults (or a clean drain) via Completion; DisposeAsync never re-throws them.
            _errorSink.Complete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Let a From/channel source finish emitting first; it triggers its own drain on completion.
        if (_pump is not null)
        {
            try { await _pump; }
            catch (OperationCanceledException) { }
        }

        await DrainAsync();

        _pumpCts?.Dispose();
    }
}
