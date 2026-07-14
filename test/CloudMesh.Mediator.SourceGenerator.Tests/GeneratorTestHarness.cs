using System.Collections.Immutable;
using System.Reflection;
using CloudMesh.Mediator;
using CloudMesh.Mediator.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace CloudMesh.Mediator.SourceGenerator.Tests;

/// <summary>
/// Runs <see cref="MediatorGenerator"/> over a snippet of source on an in-memory compilation that references
/// the runtime assembly, and exposes the generated trees + diagnostics for deterministic assertions.
/// </summary>
internal static class GeneratorTestHarness
{
    private static readonly ImmutableArray<MetadataReference> BaseReferences = BuildReferences();
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var refs = new List<MetadataReference>();

        var assemblies = new[]
        {
            typeof(object).Assembly,                                   // System.Private.CoreLib
            typeof(IMediator).Assembly,                                // CloudMesh.Mediator
            typeof(IServiceCollection).Assembly,                       // DI abstractions
            typeof(ServiceCollection).Assembly,                        // DI (concrete)
            typeof(ValueTask).Assembly,                                // System.Threading.Tasks
            typeof(IAsyncEnumerable<int>).Assembly,                    // System.Runtime
            typeof(Enumerable).Assembly,                               // System.Linq
        };

        var seen = new HashSet<string>();
        foreach (var asm in assemblies)
        {
            if (!string.IsNullOrEmpty(asm.Location) && seen.Add(asm.Location))
                refs.Add(MetadataReference.CreateFromFile(asm.Location));
        }

        // Add the full runtime assembly set so generated code touching BCL facades always resolves.
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? string.Empty;
        foreach (var path in tpa.Split(Path.PathSeparator))
        {
            if (path.Length == 0)
                continue;
            if (seen.Add(path))
            {
                try { refs.Add(MetadataReference.CreateFromFile(path)); }
                catch { /* skip unreadable entries */ }
            }
        }

        return refs.ToImmutableArray();
    }

    /// <summary>The sanitized method/class suffix the generator uses for the given assembly name.</summary>
    public static string Suffix(string assemblyName)
    {
        var chars = assemblyName.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (!ok) chars[i] = '_';
        }
        var s = new string(chars);
        if (s.Length == 0 || !((s[0] >= 'A' && s[0] <= 'Z') || (s[0] >= 'a' && s[0] <= 'z') || s[0] == '_'))
            s = "_" + s;
        return s;
    }

    public static CSharpCompilation CreateCompilation(
        string source, string assemblyName, IEnumerable<MetadataReference>? extraReferences = null)
    {
        var refs = extraReferences is null
            ? BaseReferences
            : BaseReferences.AddRange(extraReferences);

        return CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>Builds a referenced library from source and returns it as a metadata reference (in-memory emit).</summary>
    public static (MetadataReference Reference, ImmutableArray<Diagnostic> Errors) BuildLibrary(string source, string assemblyName)
    {
        var compilation = CreateCompilation(source, assemblyName);
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        var errors = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        ms.Position = 0;
        return (MetadataReference.CreateFromStream(ms), errors);
    }

    public static GeneratorResult Run(
        string source, string assemblyName = "TestAssembly", IEnumerable<MetadataReference>? extraReferences = null)
    {
        var compilation = CreateCompilation(source, assemblyName, extraReferences);

        var driver = CSharpGeneratorDriver.Create(
            generators: new[] { new MediatorGenerator().AsSourceGenerator() },
            additionalTexts: default,
            parseOptions: ParseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        var runDriver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var runResult = runDriver.GetRunResult();

        var emitDiagnostics = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        return new GeneratorResult(runResult, emitDiagnostics);
    }

    /// <summary>
    /// Runs the <see cref="BoxingSendAnalyzer"/> (CMED200) over the source. When <paramref name="runGenerator"/>
    /// is true, the mediator generator runs first so its box-free overloads are in scope (the negative-case
    /// setup); when false, only the boxing fallback exists (the positive-case setup, e.g. a request assembly
    /// without the generator).
    /// </summary>
    public static async System.Threading.Tasks.Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(
        string source, bool runGenerator, string assemblyName = "TestAssembly",
        IEnumerable<MetadataReference>? extraReferences = null)
    {
        Microsoft.CodeAnalysis.Compilation compilation = CreateCompilation(source, assemblyName, extraReferences);

        if (runGenerator)
        {
            var driver = CSharpGeneratorDriver.Create(
                generators: new[] { new MediatorGenerator().AsSourceGenerator() },
                additionalTexts: default,
                parseOptions: ParseOptions,
                optionsProvider: null);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out compilation, out _);
        }

        // Fail the test early if the (post-generation) compilation has C# errors — a botched setup would make
        // the analyzer results meaningless.
        var compileErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS"))
            .ToImmutableArray();
        if (!compileErrors.IsEmpty)
            throw new System.InvalidOperationException(
                "Analyzer test source failed to compile: " + string.Join("; ", compileErrors.Select(e => e.ToString())));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new BoxingSendAnalyzer());
        var withAnalyzers = compilation.WithAnalyzers(analyzers, options: (AnalyzerOptions?)null);

        // GetAnalyzerDiagnosticsAsync returns only the analyzers' diagnostics (including AD0001 if one throws).
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync().ConfigureAwait(false);
        return diagnostics;
    }
}

internal sealed record GeneratorResult(
    GeneratorDriverRunResult RunResult,
    ImmutableArray<Diagnostic> CompilationErrors)
{
    public ImmutableArray<Diagnostic> GeneratorDiagnostics =>
        RunResult.Results.SelectMany(r => r.Diagnostics).ToImmutableArray();

    public string AllGeneratedText =>
        string.Join("\n\n", RunResult.GeneratedTrees.Select(t => t.ToString()));

    public string GeneratedText(string hintNameContains) =>
        RunResult.GeneratedTrees
            .Where(t => t.FilePath.Contains(hintNameContains))
            .Select(t => t.ToString())
            .FirstOrDefault() ?? string.Empty;

    public int CountOf(string diagnosticId) =>
        GeneratorDiagnostics.Count(d => d.Id == diagnosticId);
}
