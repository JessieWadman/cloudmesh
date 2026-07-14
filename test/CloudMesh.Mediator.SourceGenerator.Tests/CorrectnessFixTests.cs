using System.Linq;
using CloudMesh.Mediator.SourceGenerator;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CloudMesh.Mediator.SourceGenerator.Tests;

/// <summary>
/// Tests for the P1–P5 correctness fixes. Every P1 case actually compiles the generated output and asserts
/// zero C# errors — the class of bug the earlier tests missed by only using public single-assembly types.
/// </summary>
public class CorrectnessFixTests
{
    // ---- P1.1 Accessibility --------------------------------------------------------------------------

    [Fact]
    public void Internal_request_and_handler_compile_cleanly()
    {
        // Repro (a): internal request + internal handler. Public overloads/registration would fail (CS0051);
        // the generator must emit an INTERNAL overload and still register the handler (internal is referenceable
        // within the same assembly).
        const string src = """
            using CloudMesh.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Sample;

            internal readonly record struct Sec(int V) : IRequest<int>;

            internal sealed class SecHandler : IRequestHandler<Sec, int>
            {
                public ValueTask<int> HandleAsync(Sec request, CancellationToken ct) => new(request.V);
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Empty(result.CompilationErrors); // MUST compile
        var reg = result.GeneratedText("Registration");
        Assert.Contains("typeof(global::Sample.SecHandler)", reg);

        var ext = result.GeneratedText("SenderExtensions");
        // Overload must be internal, not public (else CS0051 — inconsistent accessibility).
        Assert.Contains("internal static global::System.Threading.Tasks.ValueTask<int> SendAsync(", ext);
        Assert.DoesNotContain("public static global::System.Threading.Tasks.ValueTask<int> SendAsync(", ext);
    }

    [Fact]
    public void Nested_private_handler_is_skipped_and_compiles()
    {
        // Repro (b): a private sealed handler nested in a public Outer. It cannot be referenced from an
        // assembly-level static class, so it must be SKIPPED (not registered) and the output must compile.
        const string src = """
            using CloudMesh.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Sample;

            public readonly record struct Png(int V) : IRequest<int>;

            public sealed class Outer
            {
                private sealed class Inner : IRequestHandler<Png, int>
                {
                    public ValueTask<int> HandleAsync(Png request, CancellationToken ct) => new(request.V);
                }
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Empty(result.CompilationErrors); // MUST compile
        var reg = result.GeneratedText("Registration");
        // The private nested handler must NOT be registered.
        Assert.DoesNotContain("Inner", reg);
        // Png has a public request type but no accessible handler -> CMED001 (Info) but no build break.
        Assert.Equal(1, result.CountOf("CMED001"));
    }

    // ---- P1.2 Multi-library ambiguity ----------------------------------------------------------------

    [Fact]
    public void Two_assemblies_get_distinct_registration_method_names()
    {
        const string libA = """
            using CloudMesh.Mediator;
            using System.Threading;
            using System.Threading.Tasks;
            namespace LibA;
            public readonly record struct A(int V) : IRequest<int>;
            public sealed class AH : IRequestHandler<A, int> { public ValueTask<int> HandleAsync(A r, CancellationToken ct) => new(r.V); }
            """;
        const string libB = """
            using CloudMesh.Mediator;
            using System.Threading;
            using System.Threading.Tasks;
            namespace LibB;
            public readonly record struct B(int V) : IRequest<int>;
            public sealed class BH : IRequestHandler<B, int> { public ValueTask<int> HandleAsync(B r, CancellationToken ct) => new(r.V); }
            """;

        var resA = GeneratorTestHarness.Run(libA, assemblyName: "Company.LibraryA");
        var resB = GeneratorTestHarness.Run(libB, assemblyName: "Company.LibraryB");

        Assert.Empty(resA.CompilationErrors);
        Assert.Empty(resB.CompilationErrors);

        var methodA = "AddCloudMeshMediatorGenerated" + GeneratorTestHarness.Suffix("Company.LibraryA");
        var methodB = "AddCloudMeshMediatorGenerated" + GeneratorTestHarness.Suffix("Company.LibraryB");

        Assert.NotEqual(methodA, methodB);
        Assert.Contains(methodA, resA.GeneratedText("Registration"));
        Assert.Contains(methodB, resB.GeneratedText("Registration"));
        // Non-identifier chars in the assembly name are sanitized to underscores.
        Assert.Contains("Company_LibraryA", methodA);
    }

    // ---- P2 CMED001 across assemblies ----------------------------------------------------------------

    [Fact]
    public void CMED001_does_not_break_build_when_handler_is_in_referenced_assembly()
    {
        // Contracts assembly declares the request; handlers assembly (built separately) provides the handler.
        const string contracts = """
            using CloudMesh.Mediator;
            namespace Contracts;
            public readonly record struct Query(int V) : IRequest<int>;
            """;

        var (contractsRef, contractErrors) = GeneratorTestHarness.BuildLibrary(contracts, "Contracts");
        Assert.Empty(contractErrors);

        // The consuming compilation references Contracts but declares the request's handler itself... actually
        // the whole point: the request lives in a referenced assembly with NO handler here. CMED001 must be Info
        // (non-build-breaking) and the generated output must compile.
        const string app = """
            using CloudMesh.Mediator;
            using System.Threading;
            using System.Threading.Tasks;
            namespace App;
            // No handler for Contracts.Query in this compilation.
            public readonly record struct Local(int V) : IRequest<int>;
            public sealed class LocalHandler : IRequestHandler<Local, int>
            {
                public ValueTask<int> HandleAsync(Local request, CancellationToken ct) => new(request.V);
            }
            """;

        var result = GeneratorTestHarness.Run(app, "App", new[] { contractsRef });

        Assert.Empty(result.CompilationErrors);
        // CMED001 (if any) must be Info severity so TreatWarningsAsErrors cannot break the build.
        foreach (var d in result.GeneratorDiagnostics.Where(d => d.Id == "CMED001"))
            Assert.Equal(DiagnosticSeverity.Info, d.Severity);
    }

    [Fact]
    public void CMED001_default_severity_is_info()
    {
        const string src = """
            using CloudMesh.Mediator;
            namespace Sample;
            public readonly record struct Orphan(int V) : IRequest<int>;
            """;

        var result = GeneratorTestHarness.Run(src);
        var d = Assert.Single(result.GeneratorDiagnostics.Where(x => x.Id == "CMED001"));
        Assert.Equal(DiagnosticSeverity.Info, d.Severity);
    }

    // ---- P3.4 Interface identity via symbols ---------------------------------------------------------

    [Fact]
    public void Consumer_declared_same_named_interface_is_not_false_matched()
    {
        // A consumer defines their OWN CloudMesh.Mediator.Compatibility.IRequestHandler-shaped interface in a
        // DIFFERENT namespace. Symbol-based matching must not treat it as the mediator interface.
        const string src = """
            using CloudMesh.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Other.Mediator
            {
                public interface IRequestHandler<TRequest, TResponse> { }
            }

            namespace Sample
            {
                public readonly record struct Q(int V) : IRequest<int>;

                // Implements the FOREIGN interface, not CloudMesh's — must not be registered as a handler.
                public sealed class Foreign : Other.Mediator.IRequestHandler<Q, int> { }
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Empty(result.CompilationErrors);
        var reg = result.GeneratedText("Registration");
        Assert.DoesNotContain("typeof(global::Sample.Foreign)", reg);
        // Q has no real handler -> CMED001 fires (proving Foreign was not counted).
        Assert.Equal(1, result.CountOf("CMED001"));
    }

    // ---- P4.6 Open-generic behavior (generator emission) --------------------------------------------

    [Fact]
    public void Open_generic_behavior_is_emitted_as_open_descriptor()
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

            public sealed class LoggingBehavior<TReq, TResp> : IPipelineBehavior<TReq, TResp>
                where TReq : IRequest<TResp>
            {
                public ValueTask<TResp> HandleAsync(TReq request, RequestHandlerDelegate<TResp> next, CancellationToken ct) => next();
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Empty(result.CompilationErrors);
        var reg = result.GeneratedText("Registration");
        // Open-generic descriptor: typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>).
        Assert.Contains("typeof(global::CloudMesh.Mediator.IPipelineBehavior<,>)", reg);
        Assert.Contains("typeof(global::Sample.LoggingBehavior<,>)", reg);
    }

    // ---- Fast-dispatch emission (Deliverable B) ------------------------------------------------------

    [Fact]
    public void No_behavior_request_emits_devirtualized_downcast_branch()
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
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Empty(result.CompilationErrors);
        var ext = result.GeneratedText("SenderExtensions");
        // Down-cast a concrete Mediator receiver and call the SAME box-free primitive on it (JIT devirtualizes);
        // a custom ISender takes the interface call. No special dispatch method.
        Assert.Contains("sender is global::CloudMesh.Mediator.Mediator __m", ext);
        Assert.Contains("__m.SendAsync<global::Sample.Ping, int>(in request, cancellationToken)", ext);
        Assert.Contains("sender.SendAsync<global::Sample.Ping, int>(in request, cancellationToken)", ext);
        Assert.DoesNotContain("SendGeneratedFast", ext);
    }

    [Fact]
    public void Request_with_behavior_does_not_emit_downcast_branch()
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
        var ext = result.GeneratedText("SenderExtensions");
        // Behavior present at compile time -> no down-cast; route straight through the interface primitive.
        Assert.DoesNotContain("sender is global::CloudMesh.Mediator.Mediator __m", ext);
        Assert.Contains("=> sender.SendAsync<global::Sample.Ping, int>(in request, cancellationToken);", ext);
    }

    [Fact]
    public void Open_generic_behavior_disqualifies_downcast_for_all_requests()
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
            public sealed class LoggingBehavior<TReq, TResp> : IPipelineBehavior<TReq, TResp>
                where TReq : IRequest<TResp>
            {
                public ValueTask<TResp> HandleAsync(TReq request, RequestHandlerDelegate<TResp> next, CancellationToken ct) => next();
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Empty(result.CompilationErrors);
        var ext = result.GeneratedText("SenderExtensions");
        // An open-generic behavior applies to every request at runtime -> no down-cast for ANY request.
        Assert.DoesNotContain("sender is global::CloudMesh.Mediator.Mediator __m", ext);
    }

    // ---- CMED002 with multi-response request (must NOT false-positive) --------------------------------

    [Fact]
    public void Two_response_types_with_two_handlers_do_not_raise_CMED002()
    {
        // A request implementing IRequest<int> AND IRequest<string> with a handler for EACH is legal: the runtime
        // dispatches on the (request, response) pair. CMED002 must key on the pair, not the request alone.
        const string src = """
            using CloudMesh.Mediator;
            using System.Threading;
            using System.Threading.Tasks;
            namespace Sample;
            public sealed record Multi(int V) : IRequest<int>, IRequest<string>;
            public sealed class MultiIntHandler : IRequestHandler<Multi, int>
            {
                public ValueTask<int> HandleAsync(Multi request, CancellationToken ct) => new(request.V);
            }
            public sealed class MultiStringHandler : IRequestHandler<Multi, string>
            {
                public ValueTask<string> HandleAsync(Multi request, CancellationToken ct) => new(request.V.ToString());
            }
            """;

        var result = GeneratorTestHarness.Run(src);

        Assert.Empty(result.CompilationErrors);
        Assert.Equal(0, result.CountOf("CMED002")); // legal, not a duplicate
        Assert.Equal(0, result.CountOf("CMED001")); // both response pairs are handled
        var reg = result.GeneratedText("Registration");
        Assert.Contains("typeof(global::CloudMesh.Mediator.IRequestHandler<global::Sample.Multi, int>)", reg);
        Assert.Contains("typeof(global::CloudMesh.Mediator.IRequestHandler<global::Sample.Multi, string>)", reg);
    }

    [Fact]
    public void Two_handlers_for_same_response_still_raise_CMED002()
    {
        const string src = """
            using CloudMesh.Mediator;
            using System.Threading;
            using System.Threading.Tasks;
            namespace Sample;
            public readonly record struct Dup(int V) : IRequest<int>;
            public sealed class A : IRequestHandler<Dup, int> { public ValueTask<int> HandleAsync(Dup r, CancellationToken ct) => new(1); }
            public sealed class B : IRequestHandler<Dup, int> { public ValueTask<int> HandleAsync(Dup r, CancellationToken ct) => new(2); }
            """;

        var result = GeneratorTestHarness.Run(src);
        Assert.Equal(1, result.CountOf("CMED002"));
    }

    // ---- P5.7 Cacheability ---------------------------------------------------------------------------

    [Fact]
    public void Generator_output_is_cached_across_unrelated_whitespace_edit()
    {
        // Includes a request+handler AND a notification+handler so all three tracked streams are populated.
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

            public sealed class Note : INotification { }
            public sealed class NoteHandler : INotificationHandler<Note>
            {
                public ValueTask HandleAsync(Note notification, CancellationToken ct) => default;
            }
            """;

        var compilation1 = GeneratorTestHarness.CreateCompilation(src, "TestAssembly");

        GeneratorDriver driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(
            generators: new[] { new MediatorGenerator().AsSourceGenerator() },
            additionalTexts: default,
            parseOptions: new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview),
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        // First run.
        driver = driver.RunGenerators(compilation1);

        // Second run against a compilation with an unrelated trailing-whitespace/comment edit that does not
        // change any of the discovered symbols.
        var compilation2 = GeneratorTestHarness.CreateCompilation(src + "\n// unrelated trailing comment\n", "TestAssembly");
        driver = driver.RunGenerators(compilation2);

        var result = driver.GetRunResult().Results.Single();

        AssertAllCachedOrUnchanged(result, MediatorGenerator.TrackingNames.Registrations);
        AssertAllCachedOrUnchanged(result, MediatorGenerator.TrackingNames.Requests);
        AssertAllCachedOrUnchanged(result, MediatorGenerator.TrackingNames.Notifications);
    }

    private static void AssertAllCachedOrUnchanged(GeneratorRunResult result, string trackingName)
    {
        var steps = result.TrackedSteps
            .Where(kvp => kvp.Key == trackingName)
            .SelectMany(kvp => kvp.Value)
            .ToList();

        Assert.NotEmpty(steps);
        foreach (var step in steps)
        {
            foreach (var (_, reason) in step.Outputs)
            {
                Assert.True(
                    reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"Step '{trackingName}' output reason was {reason}; expected Cached/Unchanged (incrementality broken).");
            }
        }
    }
}
