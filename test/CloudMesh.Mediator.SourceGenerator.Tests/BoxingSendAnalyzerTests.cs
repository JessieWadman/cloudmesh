using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CloudMesh.Mediator.SourceGenerator.Tests;

/// <summary>
/// CMED200 (BoxingSendAnalyzer) tests. The correctness gate is: NEVER fire on a box-free call. The negative
/// cases run the generator first (so its box-free overloads are in scope, exactly as in a real consumer);
/// the positive case runs WITHOUT the generator (a request assembly that lacks it) so only the boxing fallback
/// binds.
/// </summary>
public class BoxingSendAnalyzerTests
{
    // A caller sends a concrete STRUCT request. This is the shape that either boxes (no generator) or is box-free
    // (generator emitted SendAsync(in StructReq)).
    private const string StructSendSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using CloudMesh.Mediator;

        namespace Sample;

        public readonly record struct StructReq(int V) : IRequest<int>;

        public sealed class StructReqHandler : IRequestHandler<StructReq, int>
        {
            public ValueTask<int> HandleAsync(StructReq request, CancellationToken ct) => new(request.V);
        }

        public static class Caller
        {
            public static ValueTask<int> Go(ISender sender) => sender.SendAsync(new StructReq(1));
        }
        """;

    private static int Cmed200Count(ImmutableArray<Diagnostic> diags) => diags.Count(d => d.Id == "CMED200");

    [Fact]
    public async Task Positive_boxing_send_of_concrete_type_without_generated_overload_fires()
    {
        // Case (d): no generator -> only the boxing fallback SendAsync<TResponse>(IRequest<TResponse>) exists,
        // so sender.SendAsync(new StructReq(1)) binds to it and boxes the struct -> CMED200 fires.
        var diags = await GeneratorTestHarness.RunAnalyzerAsync(StructSendSource, runGenerator: false);

        var d = Assert.Single(diags.Where(x => x.Id == "CMED200"));
        Assert.Equal(DiagnosticSeverity.Info, d.Severity);
        Assert.Contains("StructReq", d.GetMessage());
    }

    [Fact]
    public async Task Negative_generated_overload_in_scope_does_not_fire()
    {
        // Case (a): generator ran -> SendAsync(in StructReq) is in scope and is more specific, so the call binds
        // to the box-free overload, NOT the fallback -> NO CMED200.
        var diags = await GeneratorTestHarness.RunAnalyzerAsync(StructSendSource, runGenerator: true);
        Assert.Equal(0, Cmed200Count(diags));
    }

    [Fact]
    public async Task Negative_explicit_generic_primitive_does_not_fire()
    {
        // Case (b): the explicit box-free primitive SendAsync<StructReq,int>(in r) binds to ISender's method,
        // not the SenderExtensions fallback -> NO CMED200. (No generator needed; the primitive is on ISender.)
        const string src = """
            using System.Threading;
            using System.Threading.Tasks;
            using CloudMesh.Mediator;

            namespace Sample;

            public readonly record struct StructReq(int V) : IRequest<int>;
            public sealed class StructReqHandler : IRequestHandler<StructReq, int>
            {
                public ValueTask<int> HandleAsync(StructReq request, CancellationToken ct) => new(request.V);
            }
            public static class Caller
            {
                public static ValueTask<int> Go(ISender sender)
                {
                    var r = new StructReq(1);
                    return sender.SendAsync<StructReq, int>(in r);
                }
            }
            """;

        var diags = await GeneratorTestHarness.RunAnalyzerAsync(src, runGenerator: false);
        Assert.Equal(0, Cmed200Count(diags));
    }

    [Fact]
    public async Task Negative_interface_typed_argument_does_not_fire()
    {
        // Case (c): the argument's STATIC type is IRequest<string> (interface) -> genuinely dynamic send, not
        // actionable -> NO CMED200 even though it binds to the boxing fallback.
        const string src = """
            using System.Threading;
            using System.Threading.Tasks;
            using CloudMesh.Mediator;

            namespace Sample;

            public static class Caller
            {
                public static ValueTask<string> Go(ISender sender, IRequest<string> request)
                    => sender.SendAsync(request);
            }
            """;

        var diags = await GeneratorTestHarness.RunAnalyzerAsync(src, runGenerator: false);
        Assert.Equal(0, Cmed200Count(diags));
    }

    [Fact]
    public async Task Positive_boxing_stream_of_concrete_type_fires()
    {
        const string src = """
            using System.Collections.Generic;
            using System.Threading;
            using CloudMesh.Mediator;

            namespace Sample;

            public readonly record struct StreamReq(int N) : IStreamRequest<int>;
            public sealed class StreamReqHandler : IStreamRequestHandler<StreamReq, int>
            {
                public async IAsyncEnumerable<int> HandleAsync(StreamReq request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
                { for (var i = 0; i < request.N; i++) { yield return i; await System.Threading.Tasks.Task.Yield(); } }
            }
            public static class Caller
            {
                public static IAsyncEnumerable<int> Go(ISender sender) => sender.StreamAsync(new StreamReq(3));
            }
            """;

        var diags = await GeneratorTestHarness.RunAnalyzerAsync(src, runGenerator: false);

        var d = Assert.Single(diags.Where(x => x.Id == "CMED200"));
        Assert.Contains("StreamReq", d.GetMessage());
    }

    [Fact]
    public async Task Does_not_fire_when_no_send_calls()
    {
        // The fallback is available but there is no send call -> nothing to flag.
        const string src = """
            using CloudMesh.Mediator;
            namespace Sample;
            public readonly record struct Q(int V) : IRequest<int>;
            """;

        var diags = await GeneratorTestHarness.RunAnalyzerAsync(src, runGenerator: false);
        Assert.Equal(0, Cmed200Count(diags));
    }
}
