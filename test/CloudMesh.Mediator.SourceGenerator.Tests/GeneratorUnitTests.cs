using Xunit;

namespace CloudMesh.Mediator.SourceGenerator.Tests;

public class GeneratorUnitTests
{
    private const string Ping = """
        using CloudMesh.Mediator;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Sample;

        public readonly record struct Ping(int Value) : IRequest<int>;

        public sealed class PingHandler : IRequestHandler<Ping, int>
        {
            public ValueTask<int> HandleAsync(Ping request, CancellationToken ct) => new(request.Value);
        }
        """;

    [Fact]
    public void Emits_registration_for_request_handler()
    {
        var result = GeneratorTestHarness.Run(Ping);

        Assert.Empty(result.CompilationErrors);
        var reg = result.GeneratedText("Registration");
        Assert.Contains("AddCloudMeshMediatorGenerated", reg);
        Assert.Contains("AddCloudMeshMediatorCore", reg);
        Assert.Contains("typeof(global::CloudMesh.Mediator.IRequestHandler<global::Sample.Ping, int>)", reg);
        Assert.Contains("typeof(global::Sample.PingHandler)", reg);
    }

    [Fact]
    public void Emits_zero_boxing_send_overload_for_request_type()
    {
        var result = GeneratorTestHarness.Run(Ping);

        var ext = result.GeneratedText("SenderExtensions");
        Assert.Contains("public static global::System.Threading.Tasks.ValueTask<int> SendAsync(", ext);
        Assert.Contains("in global::Sample.Ping request", ext);
        Assert.Contains("sender.SendAsync<global::Sample.Ping, int>(in request, cancellationToken)", ext);
    }

    [Fact]
    public void Emits_stream_overload_for_stream_request()
    {
        const string src = """
            using CloudMesh.Mediator;
            using System.Collections.Generic;
            using System.Threading;

            namespace Sample;

            public readonly record struct Tick(int N) : IStreamRequest<int>;

            public sealed class TickHandler : IStreamRequestHandler<Tick, int>
            {
                public async IAsyncEnumerable<int> HandleAsync(Tick request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
                {
                    for (var i = 0; i < request.N; i++) { yield return i; await System.Threading.Tasks.Task.Yield(); }
                }
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Empty(result.CompilationErrors);
        var ext = result.GeneratedText("SenderExtensions");
        Assert.Contains("StreamAsync(", ext);
        Assert.Contains("sender.StreamAsync<global::Sample.Tick, int>(in request, cancellationToken)", ext);

        var reg = result.GeneratedText("Registration");
        Assert.Contains("typeof(global::CloudMesh.Mediator.IStreamRequestHandler<global::Sample.Tick, int>)", reg);
    }

    [Fact]
    public void CMED001_fires_for_request_without_handler()
    {
        const string src = """
            using CloudMesh.Mediator;
            namespace Sample;
            public readonly record struct Orphan(int V) : IRequest<int>;
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Equal(1, result.CountOf("CMED001"));
        Assert.Equal(0, result.CountOf("CMED002"));
    }

    [Fact]
    public void CMED001_does_not_fire_when_handler_present()
    {
        var result = GeneratorTestHarness.Run(Ping);
        Assert.Equal(0, result.CountOf("CMED001"));
    }

    [Fact]
    public void CMED002_fires_for_duplicate_handlers()
    {
        const string src = """
            using CloudMesh.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Sample;

            public readonly record struct Dup(int V) : IRequest<int>;

            public sealed class HandlerA : IRequestHandler<Dup, int>
            {
                public ValueTask<int> HandleAsync(Dup request, CancellationToken ct) => new(1);
            }
            public sealed class HandlerB : IRequestHandler<Dup, int>
            {
                public ValueTask<int> HandleAsync(Dup request, CancellationToken ct) => new(2);
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Equal(1, result.CountOf("CMED002"));
        Assert.Equal(0, result.CountOf("CMED001"));
    }

    [Fact]
    public void CMED003_fires_for_notification_without_handlers()
    {
        const string src = """
            using CloudMesh.Mediator;
            namespace Sample;
            public sealed class Bang : INotification { }
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Equal(1, result.CountOf("CMED003"));
    }

    [Fact]
    public void CMED003_does_not_fire_when_notification_handler_present()
    {
        const string src = """
            using CloudMesh.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Sample;

            public sealed class Bang : INotification { }

            public sealed class BangHandler : INotificationHandler<Bang>
            {
                public ValueTask HandleAsync(Bang notification, CancellationToken ct) => default;
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Equal(0, result.CountOf("CMED003"));
        Assert.Empty(result.CompilationErrors);
        var reg = result.GeneratedText("Registration");
        Assert.Contains("typeof(global::CloudMesh.Mediator.INotificationHandler<global::Sample.Bang>)", reg);
    }

    [Fact]
    public void Registers_pipeline_behaviors()
    {
        const string src = """
            using CloudMesh.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Sample;

            public readonly record struct Ping(int V) : IRequest<int>;
            public sealed class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> HandleAsync(Ping request, CancellationToken ct) => new(request.V);
            }
            public sealed class LogBehavior : IPipelineBehavior<Ping, int>
            {
                public ValueTask<int> HandleAsync(Ping request, RequestHandlerDelegate<int> next, CancellationToken ct) => next();
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Empty(result.CompilationErrors);
        var reg = result.GeneratedText("Registration");
        Assert.Contains("typeof(global::CloudMesh.Mediator.IPipelineBehavior<global::Sample.Ping, int>)", reg);
        Assert.Contains("typeof(global::Sample.LogBehavior)", reg);
    }

    [Fact]
    public void Registers_compat_handler_with_native_adapter()
    {
        const string src = """
            using System.Threading;
            using System.Threading.Tasks;
            using CloudMesh.Mediator;
            using Compat = CloudMesh.Mediator.Compatibility;

            namespace Sample;

            public readonly record struct Ask(int V) : IRequest<string>;

            public sealed class AskHandler : Compat.IRequestHandler<Ask, string>
            {
                public Task<string> Handle(Ask request, CancellationToken ct) => Task.FromResult(request.V.ToString());
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Empty(result.CompilationErrors);
        var reg = result.GeneratedText("Registration");
        // Registered under its own compat interface...
        Assert.Contains("typeof(global::CloudMesh.Mediator.Compatibility.IRequestHandler<global::Sample.Ask, string>)", reg);
        // ...and the native adapter satisfies IRequestHandler for CMED purposes.
        Assert.Contains("CompatRequestHandlerAdapter<global::Sample.Ask, string>", reg);
        // Compat handler counts as the request's handler -> no CMED001.
        Assert.Equal(0, result.CountOf("CMED001"));
    }

    [Fact]
    public void Generated_registration_compiles_with_no_errors_for_empty_compilation()
    {
        const string src = """
            namespace Sample;
            public sealed class Nothing { }
            """;

        var result = GeneratorTestHarness.Run(src);
        Assert.Empty(result.CompilationErrors);
        Assert.Contains("AddCloudMeshMediatorGenerated", result.GeneratedText("Registration"));
    }

    [Fact]
    public void CMED100_fires_for_compat_request_handler()
    {
        const string src = """
            using System.Threading;
            using System.Threading.Tasks;
            using CloudMesh.Mediator;
            using Compat = CloudMesh.Mediator.Compatibility;

            namespace Sample;

            public readonly record struct Ask(int V) : IRequest<string>;

            public sealed class AskHandler : Compat.IRequestHandler<Ask, string>
            {
                public Task<string> Handle(Ask request, CancellationToken ct) => Task.FromResult(request.V.ToString());
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        var d = Assert.Single(result.GeneratorDiagnostics.Where(x => x.Id == "CMED100"));
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Info, d.Severity);
        Assert.Contains("AskHandler", d.GetMessage());
    }

    [Fact]
    public void CMED100_fires_for_compat_notification_handler()
    {
        const string src = """
            using System.Threading;
            using System.Threading.Tasks;
            using CloudMesh.Mediator;
            using Compat = CloudMesh.Mediator.Compatibility;

            namespace Sample;

            public sealed class Bang : INotification { }

            public sealed class BangHandler : Compat.INotificationHandler<Bang>
            {
                public Task Handle(Bang notification, CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        var d = Assert.Single(result.GeneratorDiagnostics.Where(x => x.Id == "CMED100"));
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Info, d.Severity);
    }

    [Fact]
    public void CMED100_does_not_fire_for_native_handler()
    {
        const string src = """
            using System.Threading;
            using System.Threading.Tasks;
            using CloudMesh.Mediator;

            namespace Sample;

            public readonly record struct Ping(int V) : IRequest<int>;

            public sealed class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> HandleAsync(Ping request, CancellationToken ct) => new(request.V);
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Equal(0, result.CountOf("CMED100"));
    }
}
