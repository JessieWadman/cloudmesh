using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CloudMesh.Mediator.SourceGenerator;

/// <summary>
/// CMED200: flags a send/stream call that binds to the BOXING fallback overload
/// (<c>CloudMesh.Mediator.SenderExtensions.SendAsync/StreamAsync</c>) for a CONCRETE request type. Such a call
/// boxes the request; a source-generated box-free overload (or the explicit generic primitive) avoids it.
/// </summary>
/// <remarks>
/// This is a separate <see cref="DiagnosticAnalyzer"/> hosted in the same analyzer assembly as the generator.
/// It fires only when the bound target is one of the two boxing fallbacks AND the argument's STATIC type is a
/// concrete class/struct (not interface/abstract) — so it never fires on genuinely dynamic sends, and never on a
/// call that resolved to a generated <c>SendAsync(in ConcreteReq)</c> overload or the generic primitive
/// <c>ISender.SendAsync&lt;TRequest,TResponse&gt;(in TRequest)</c> (those are different target methods).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BoxingSendAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Diagnostics.BoxingSend);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static compilationStart =>
        {
            var senderExtensions = compilationStart.Compilation.GetTypeByMetadataName("CloudMesh.Mediator.SenderExtensions");
            if (senderExtensions is null)
                return; // CloudMesh.Mediator not referenced here — nothing to analyze.

            // Resolve the two boxing fallback methods (open generic definitions) by name + parameter shape.
            INamedTypeSymbol? sendMethodOwner = senderExtensions;
            IMethodSymbol? boxingSend = null;
            IMethodSymbol? boxingStream = null;

            foreach (var member in sendMethodOwner.GetMembers())
            {
                if (member is not IMethodSymbol { IsStatic: true, IsExtensionMethod: true } m || m.Parameters.Length != 3)
                    continue;
                // Signature: (this ISender, IRequest<TResponse>|IStreamRequest<TResponse>, CancellationToken)
                if (m.Name == "SendAsync" && m.Arity == 1)
                    boxingSend = m.OriginalDefinition;
                else if (m.Name == "StreamAsync" && m.Arity == 1)
                    boxingStream = m.OriginalDefinition;
            }

            if (boxingSend is null && boxingStream is null)
                return;

            compilationStart.RegisterOperationAction(
                ctx => AnalyzeInvocation(ctx, boxingSend, boxingStream),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext ctx, IMethodSymbol? boxingSend, IMethodSymbol? boxingStream)
    {
        var invocation = (IInvocationOperation)ctx.Operation;

        // When an extension method is called with instance syntax (sender.SendAsync(...)), TargetMethod is the
        // REDUCED form; normalize to the original static definition via ReducedFrom before comparing.
        var method = invocation.TargetMethod;
        var unreduced = method.ReducedFrom ?? method;
        var target = unreduced.OriginalDefinition;

        var isSend = boxingSend is not null && SymbolEqualityComparer.Default.Equals(target, boxingSend);
        var isStream = boxingStream is not null && SymbolEqualityComparer.Default.Equals(target, boxingStream);
        if (!isSend && !isStream)
            return;

        // Find the request argument by its PARAMETER TYPE (IRequest<> / IStreamRequest<>), not a positional index:
        // the IOperation argument/receiver mapping for an extension method invoked with instance vs. static syntax
        // is not positionally stable. The request parameter is the one that is neither the ISender receiver nor
        // the CancellationToken.
        ITypeSymbol? argType = null;
        foreach (var arg in invocation.Arguments)
        {
            var paramType = arg.Parameter?.Type;
            if (paramType is INamedTypeSymbol { IsGenericType: true } named &&
                named.Name is "IRequest" or "IStreamRequest" &&
                named.ContainingNamespace?.ToDisplayString() == "CloudMesh.Mediator")
            {
                // Use the type BEFORE the implicit conversion to the interface parameter (that conversion IS the
                // box). arg.Value is typically an IConversionOperation (request -> IRequest<T>); its Operand carries
                // the concrete static request type.
                var value = arg.Value;
                argType = value is IConversionOperation conv ? conv.Operand.Type : value.Type;
                break;
            }
        }
        if (argType is null)
            return;

        // Fire ONLY for a value-type (struct) request — that is the case that actually boxes when converted to the
        // IRequest<T> parameter. Reference-type requests don't box (they take the slower reflection fallback, but
        // that's a separate, fuzzier concern), and interface/abstract static types are genuinely dynamic sends.
        if (argType.TypeKind is not TypeKind.Struct)
            return;

        var responseType = method.TypeArguments.Length == 1
            ? method.TypeArguments[0]
            : null;

        var requestName = argType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var responseName = responseType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "TResponse";

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.BoxingSend, invocation.Syntax.GetLocation(), requestName, responseName));
    }
}
