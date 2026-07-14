using MessagePipe;

namespace MediatorBenchmarks.Libraries.MessagePipeLib;

// --- Scenario 1: Send, no behaviors ---
// MessagePipe is NOT MediatR-shaped: handlers are resolved directly as
// IAsyncRequestHandler<TReq,TResp> and invoked via InvokeAsync. There is no central mediator.
public sealed record Ping(string Message);

public sealed class PingHandler : IAsyncRequestHandler<Ping, string>
{
    public ValueTask<string> InvokeAsync(Ping request, CancellationToken cancellationToken = default)
        => new(request.Message);
}

// --- Scenario 2: Publish to 3 handlers ---
// Pub/sub uses IPublisher<T>/ISubscriber<T>. Handlers are subscribed at runtime (in GlobalSetup),
// not auto-discovered. We subscribe three delegate handlers that each increment a counter.
// Counters for the three subscribed handlers live in Program.cs (MpCounters), since MessagePipe
// pub/sub handlers are subscribed as delegates at runtime rather than as discovered handler types.
public sealed record Pinged(string Message);

// NOTE: MessagePipe has no request/response pipeline-behavior equivalent (it uses filters, which are
// per-handler and shaped differently). Scenario 3 (Send with 2 pipeline behaviors) is therefore
// OMITTED for MessagePipe only.
