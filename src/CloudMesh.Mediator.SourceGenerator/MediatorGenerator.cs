using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CloudMesh.Mediator.SourceGenerator;

/// <summary>
/// Incremental source generator for CloudMesh.Mediator. For the compilation being built it discovers every
/// concrete handler/behavior/notification-handler (including the MediatR-shaped Compatibility.* ones and
/// open-generic pipeline behaviors) and emits:
///  (a) a reflection-free per-assembly <c>AddCloudMeshMediatorGenerated{AssemblyName}</c> DI registration method,
///  (b) zero-boxing <c>SendAsync(in Req)</c> / <c>StreamAsync(in Req)</c> ergonomic overloads,
///  (c) compile-time diagnostics CMED001/CMED002/CMED003.
/// The pipeline models are value-equatable (see <see cref="RegistrationInfo"/> etc.) so no Roslyn symbols leak
/// into the cache — incrementality is preserved and proven by a cacheability test.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MediatorGenerator : IIncrementalGenerator
{
    /// <summary>Tracking names for the cacheability test (assert steps are Cached/Unchanged after a no-op edit).</summary>
    internal static class TrackingNames
    {
        public const string Candidates = "Candidates";
        public const string Registrations = "Registrations";
        public const string Requests = "Requests";
        public const string Notifications = "Notifications";
    }

    private static readonly SymbolDisplayFormat FullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.ExpandNullable |
            // Preserve the nullable reference-type annotation on responses: a request declared
            // IRequest<TDto?> must emit SendAsync<Req, TDto?> so the `where TRequest : IRequest<TResponse>`
            // constraint is satisfied. Without this the `?` is dropped and closed-generic emission fails
            // CS8631 for every query/command returning a nullable reference type.
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Single merged transform: each candidate type is resolved once and may yield registrations and/or a
        // request/notification classification. This avoids doing the semantic work twice per candidate.
        var candidates = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidateTypeDeclaration(node),
                transform: static (ctx, ct) => Transform(ctx, ct))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!.Value)
            .WithTrackingName(TrackingNames.Candidates);

        // Fan out the merged result into the three logical streams, each value-equatable.
        var registrations = candidates
            .SelectMany(static (c, _) => (IEnumerable<RegistrationInfo>)c.Registrations)
            .WithTrackingName(TrackingNames.Registrations);

        var requestTypes = candidates
            .Where(static c => c.Request is not null)
            .Select(static (c, _) => c.Request!.Value)
            .WithTrackingName(TrackingNames.Requests);

        var notificationTypes = candidates
            .Where(static c => c.Notification is not null)
            .Select(static (c, _) => c.Notification!.Value)
            .WithTrackingName(TrackingNames.Notifications);

        // Wrap the collected arrays in EquatableArray so RegisterSourceOutput's input has STRUCTURAL equality.
        // (Bare ImmutableArray<T> compares by reference and can silently break output caching.)
        var collectedRegistrations = registrations.Collect().Select(static (a, _) => new EquatableArray<RegistrationInfo>(a));
        var collectedRequests = requestTypes.Collect().Select(static (a, _) => new EquatableArray<RequestTypeInfo>(a));
        var collectedNotifications = notificationTypes.Collect().Select(static (a, _) => new EquatableArray<NotificationTypeInfo>(a));

        // The runtime library itself references this generator (so it flows to consumers as an analyzer),
        // but the runtime must NOT get its own registration method. Identify the runtime by SYMBOL identity:
        // the compilation is the runtime iff it DECLARES CloudMesh.Mediator.Mediator (not merely references it).
        // Also capture a sanitized assembly-name suffix so each assembly gets a uniquely-named method.
        var assemblyInfo = context.CompilationProvider.Select(static (c, _) =>
        {
            var mediator = c.GetTypeByMetadataName("CloudMesh.Mediator.Mediator");
            var isRuntime = mediator is not null &&
                            SymbolEqualityComparer.Default.Equals(mediator.ContainingAssembly, c.Assembly);
            return new AssemblyInfo(isRuntime, Sanitize(c.AssemblyName ?? "Generated"));
        });

        var combined = collectedRegistrations
            .Combine(collectedRequests)
            .Combine(collectedNotifications)
            .Combine(assemblyInfo);

        context.RegisterSourceOutput(combined, static (spc, data) =>
        {
            var ((( regs, reqs), notes), asm) = data;
            if (asm.IsRuntime)
                return; // Skip emission for the runtime library assembly.
            Emitter.Emit(spc, regs, reqs, notes, asm.Suffix, FormatToLocation);
        });
    }

    /// <summary>Value-equatable per-compilation info: whether this is the runtime, and the method-name suffix.</summary>
    private readonly record struct AssemblyInfo(bool IsRuntime, string Suffix);

    // ---- Syntactic predicate: type declaration with a non-empty base list. ------------------------------
    private static bool IsCandidateTypeDeclaration(SyntaxNode node)
        => node is TypeDeclarationSyntax { BaseList: { Types.Count: > 0 } }
            and (ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax);

    // ---- Merged semantic transform. --------------------------------------------------------------------
    private static CandidateResult? Transform(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not INamedTypeSymbol symbol)
            return null;
        if (symbol.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return null;

        var known = KnownSymbols.From(ctx.SemanticModel.Compilation);
        if (!known.IsUsable)
            return null;

        var typeDecl = (TypeDeclarationSyntax)ctx.Node;

        // Registrations (handlers/behaviors) — only concrete types. Open-generic BEHAVIORS are allowed
        // (registered as open-generic descriptors); other open generics / abstract / static are not.
        var registrations = GetRegistrations(symbol, known, typeDecl);

        // Message classification (request/notification) — only concrete, closed types produce overloads/diagnostics.
        RequestTypeInfo? request = null;
        NotificationTypeInfo? notification = null;
        if (!symbol.IsAbstract && !symbol.IsStatic && symbol.Arity == 0)
            (request, notification) = GetMessageType(symbol, known, typeDecl);

        if (registrations.Count == 0 && request is null && notification is null)
            return null;

        return new CandidateResult(registrations, request, notification);
    }

    private static EquatableArray<RegistrationInfo> GetRegistrations(
        INamedTypeSymbol symbol, in KnownSymbols known, TypeDeclarationSyntax typeDecl)
    {
        if (symbol.IsAbstract || symbol.IsStatic)
            return EquatableArray<RegistrationInfo>.Empty;

        var isOpenGeneric = symbol.Arity > 0;
        var location = LocationOf(typeDecl);

        ImmutableArray<RegistrationInfo>.Builder? builder = null;

        foreach (var iface in symbol.AllInterfaces)
        {
            if (!iface.IsGenericType)
                continue;

            var def = iface.OriginalDefinition;
            var args = iface.TypeArguments;

            RegistrationKind? kind = null;
            var isBehavior = false;

            if (SymbolEqualityComparer.Default.Equals(def, known.IRequestHandler))
                kind = RegistrationKind.RequestHandler;
            else if (SymbolEqualityComparer.Default.Equals(def, known.IStreamRequestHandler))
                kind = RegistrationKind.StreamRequestHandler;
            else if (SymbolEqualityComparer.Default.Equals(def, known.INotificationHandler))
                kind = RegistrationKind.NotificationHandler;
            else if (SymbolEqualityComparer.Default.Equals(def, known.IPipelineBehavior))
            {
                kind = RegistrationKind.PipelineBehavior;
                isBehavior = true;
            }
            else if (SymbolEqualityComparer.Default.Equals(def, known.IStreamPipelineBehavior))
            {
                kind = RegistrationKind.StreamPipelineBehavior;
                isBehavior = true;
            }
            else if (SymbolEqualityComparer.Default.Equals(def, known.CompatIRequestHandler))
                kind = RegistrationKind.CompatRequestHandler;
            else if (SymbolEqualityComparer.Default.Equals(def, known.CompatINotificationHandler))
                kind = RegistrationKind.CompatNotificationHandler;

            if (kind is null)
                continue;

            if (isOpenGeneric)
            {
                // Only pipeline behaviors are supported as open generics (MS DI closes them at resolution).
                // A non-behavior open generic (e.g. an open-generic request handler) is not supported; skip.
                if (!isBehavior)
                    continue;

                // The behavior's interface args must be exactly the class's own type parameters
                // (i.e. IPipelineBehavior<TReq,TResp> where TReq/TResp are the class type parameters).
                if (!ArgsAreOwnTypeParameters(symbol, args))
                    continue;

                var openImpl = OpenGenericName(symbol);
                var openKind = kind == RegistrationKind.PipelineBehavior
                    ? RegistrationKind.OpenGenericPipelineBehavior
                    : RegistrationKind.OpenGenericStreamPipelineBehavior;

                var access = EffectiveAccessibility(symbol);
                if (access < Access.Internal)
                    continue; // not referenceable from generated code

                builder ??= ImmutableArray.CreateBuilder<RegistrationInfo>();
                builder.Add(new RegistrationInfo(openKind, openImpl, string.Empty, null, access, location));
                continue;
            }

            // Closed registration.
            var impl = symbol.ToDisplayString(FullyQualifiedFormat);
            var messageType = args[0].ToDisplayString(FullyQualifiedFormat);
            string? responseType = args.Length > 1 ? args[1].ToDisplayString(FullyQualifiedFormat) : null;

            // Effective accessibility = min over impl + message + (response). If below Internal, generated
            // code (an assembly-level static class) cannot reference it -> skip (fixes CS0051/CS0122).
            var eff = Min(EffectiveAccessibility(symbol),
                          EffectiveAccessibility(args[0] as INamedTypeSymbol));
            if (args.Length > 1)
                eff = Min(eff, EffectiveAccessibility(args[1] as INamedTypeSymbol));
            // For compat kinds, the adapter also references the message/response; same rule applies.
            if (eff < Access.Internal)
                continue;

            builder ??= ImmutableArray.CreateBuilder<RegistrationInfo>();
            builder.Add(new RegistrationInfo(kind.Value, impl, messageType, responseType, eff, location));
        }

        return builder is null
            ? EquatableArray<RegistrationInfo>.Empty
            : new EquatableArray<RegistrationInfo>(builder.ToImmutable());
    }

    private static (RequestTypeInfo?, NotificationTypeInfo?) GetMessageType(
        INamedTypeSymbol symbol, in KnownSymbols known, TypeDeclarationSyntax typeDecl)
    {
        RequestTypeInfo? request = null;
        NotificationTypeInfo? notification = null;

        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.IsGenericType)
            {
                var def = iface.OriginalDefinition;
                if (SymbolEqualityComparer.Default.Equals(def, known.IRequest))
                    request = MakeRequest(symbol, iface, isStream: false, typeDecl);
                else if (SymbolEqualityComparer.Default.Equals(def, known.IStreamRequest))
                    request = MakeRequest(symbol, iface, isStream: true, typeDecl);
            }
            else if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, known.INotification))
            {
                notification = new NotificationTypeInfo(
                    symbol.ToDisplayString(FullyQualifiedFormat), LocationOf(typeDecl));
            }
        }

        return (request, notification);
    }

    private static RequestTypeInfo MakeRequest(
        INamedTypeSymbol symbol, INamedTypeSymbol iface, bool isStream, TypeDeclarationSyntax typeDecl)
    {
        var responseArg = iface.TypeArguments[0];
        var eff = Min(EffectiveAccessibility(symbol), EffectiveAccessibility(responseArg as INamedTypeSymbol));
        return new RequestTypeInfo(
            symbol.ToDisplayString(FullyQualifiedFormat),
            responseArg.ToDisplayString(FullyQualifiedFormat),
            isStream,
            eff,
            LocationOf(typeDecl));
    }

    // ---- accessibility helpers -------------------------------------------------------------------------
    private static Access EffectiveAccessibility(INamedTypeSymbol? type)
    {
        if (type is null)
            return Access.NotApplicable;

        // Special/primitive types (int, string) have NotApplicable declared accessibility but are public.
        var current = type;
        var result = Access.Public;
        while (current is not null)
        {
            var declared = Map(current.DeclaredAccessibility);
            if (declared != Access.NotApplicable)
                result = Min(result, declared);
            current = current.ContainingType;
        }
        return result;
    }

    private static Access Map(Accessibility a) => a switch
    {
        Accessibility.Private => Access.Private,
        Accessibility.ProtectedAndInternal => Access.ProtectedAndInternal,
        Accessibility.Protected => Access.Protected,
        Accessibility.Internal => Access.Internal,
        Accessibility.ProtectedOrInternal => Access.ProtectedOrInternal,
        Accessibility.Public => Access.Public,
        _ => Access.NotApplicable,
    };

    private static Access Min(Access a, Access b)
    {
        if (a == Access.NotApplicable) return b;
        if (b == Access.NotApplicable) return a;
        return a < b ? a : b;
    }

    private static bool ArgsAreOwnTypeParameters(INamedTypeSymbol symbol, ImmutableArray<ITypeSymbol> args)
    {
        foreach (var arg in args)
        {
            if (arg is not ITypeParameterSymbol tp ||
                !SymbolEqualityComparer.Default.Equals(tp.ContainingSymbol, symbol))
                return false;
        }
        return args.Length == symbol.Arity;
    }

    private static string OpenGenericName(INamedTypeSymbol symbol)
    {
        // Produce e.g. "global::Ns.LoggingBehavior<,>" for typeof() of an open generic.
        var closed = symbol.ToDisplayString(FullyQualifiedFormat);
        var idx = closed.IndexOf('<');
        var baseName = idx >= 0 ? closed.Substring(0, idx) : closed;
        var commas = new string(',', System.Math.Max(0, symbol.Arity - 1));
        return baseName + "<" + commas + ">";
    }

    // ---- misc ------------------------------------------------------------------------------------------
    private static string Sanitize(string assemblyName)
    {
        var chars = assemblyName.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (!ok)
                chars[i] = '_';
        }
        var s = new string(chars);
        if (s.Length == 0 || !((s[0] >= 'A' && s[0] <= 'Z') || (s[0] >= 'a' && s[0] <= 'z') || s[0] == '_'))
            s = "_" + s;
        return s;
    }

    private static LocationInfo? LocationOf(TypeDeclarationSyntax node)
    {
        var loc = node.Identifier.GetLocation();
        if (loc == Location.None)
            return null;
        var span = loc.GetLineSpan();
        return new LocationInfo(
            span.Path,
            span.StartLinePosition.Line,
            span.StartLinePosition.Character,
            span.EndLinePosition.Line,
            span.EndLinePosition.Character);
    }

    internal static Location FormatToLocation(LocationInfo? info)
    {
        if (info is null)
            return Location.None;
        var i = info.Value;
        return Location.Create(
            i.FilePath,
            default,
            new Microsoft.CodeAnalysis.Text.LinePositionSpan(
                new Microsoft.CodeAnalysis.Text.LinePosition(i.StartLine, i.StartCharacter),
                new Microsoft.CodeAnalysis.Text.LinePosition(i.EndLine, i.EndCharacter)));
    }

    /// <summary>Well-known mediator interface symbols, resolved from a compilation by metadata name.</summary>
    private readonly struct KnownSymbols
    {
        public readonly INamedTypeSymbol? IRequest;
        public readonly INamedTypeSymbol? IStreamRequest;
        public readonly INamedTypeSymbol? INotification;
        public readonly INamedTypeSymbol? IRequestHandler;
        public readonly INamedTypeSymbol? IStreamRequestHandler;
        public readonly INamedTypeSymbol? INotificationHandler;
        public readonly INamedTypeSymbol? IPipelineBehavior;
        public readonly INamedTypeSymbol? IStreamPipelineBehavior;
        public readonly INamedTypeSymbol? CompatIRequestHandler;
        public readonly INamedTypeSymbol? CompatINotificationHandler;

        private KnownSymbols(Compilation c)
        {
            IRequest = c.GetTypeByMetadataName("CloudMesh.Mediator.IRequest`1");
            IStreamRequest = c.GetTypeByMetadataName("CloudMesh.Mediator.IStreamRequest`1");
            INotification = c.GetTypeByMetadataName("CloudMesh.Mediator.INotification");
            IRequestHandler = c.GetTypeByMetadataName("CloudMesh.Mediator.IRequestHandler`2");
            IStreamRequestHandler = c.GetTypeByMetadataName("CloudMesh.Mediator.IStreamRequestHandler`2");
            INotificationHandler = c.GetTypeByMetadataName("CloudMesh.Mediator.INotificationHandler`1");
            IPipelineBehavior = c.GetTypeByMetadataName("CloudMesh.Mediator.IPipelineBehavior`2");
            IStreamPipelineBehavior = c.GetTypeByMetadataName("CloudMesh.Mediator.IStreamPipelineBehavior`2");
            CompatIRequestHandler = c.GetTypeByMetadataName("CloudMesh.Mediator.Compatibility.IRequestHandler`2");
            CompatINotificationHandler = c.GetTypeByMetadataName("CloudMesh.Mediator.Compatibility.INotificationHandler`1");
        }

        public static KnownSymbols From(Compilation c) => new(c);

        // At minimum the handler/behavior/notification interfaces must resolve for the generator to do anything.
        public bool IsUsable =>
            IRequestHandler is not null && IStreamRequestHandler is not null &&
            INotificationHandler is not null && IPipelineBehavior is not null &&
            IStreamPipelineBehavior is not null && INotification is not null &&
            IRequest is not null && IStreamRequest is not null;
    }
}
