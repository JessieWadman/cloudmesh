using Microsoft.CodeAnalysis;

namespace CloudMesh.Mediator.SourceGenerator;

/// <summary>Stable diagnostic descriptors emitted by the mediator source generator.</summary>
internal static class Diagnostics
{
    private const string Category = "CloudMesh.Mediator";

    /// <summary>
    /// A request type with no handler discovered in THIS compilation. Default severity is Info (non-build-breaking)
    /// because handlers may legitimately live in a referenced assembly (a contracts/handlers split), where the
    /// generator cannot see them. For single-assembly setups where the check is reliable, elevate it via
    /// <c>.editorconfig</c>: <c>dotnet_diagnostic.CMED001.severity = warning</c> (or <c>error</c>).
    /// </summary>
    public static readonly DiagnosticDescriptor MissingRequestHandler = new(
        id: "CMED001",
        title: "Request type has no handler in this compilation",
        messageFormat: "Request type '{0}' has no handler in this compilation; if its handler lives in a referenced assembly this is expected, otherwise sending it will fail at runtime",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Every IRequest<T>/IStreamRequest<T> type should have exactly one handler. Info by default because a handler may live in a referenced assembly; elevate to warning/error via .editorconfig for single-assembly setups.");

    /// <summary>More than one handler for the same (request, response) pair. Error.</summary>
    public static readonly DiagnosticDescriptor DuplicateRequestHandler = new(
        id: "CMED002",
        title: "Multiple handlers for one request/response pair",
        messageFormat: "Request type '{0}' with response '{1}' has {2} handlers ({3}); exactly one handler is allowed per (request, response) pair",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A request must be handled by exactly one handler per response type. A request implementing IRequest<A> and IRequest<B> may have one handler for each.");

    /// <summary>A notification type with no handlers. Informational (legitimate).</summary>
    public static readonly DiagnosticDescriptor NotificationWithoutHandlers = new(
        id: "CMED003",
        title: "Notification type has no handlers",
        messageFormat: "Notification type '{0}' has no handlers; publishing it will be a no-op",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Notifications may legitimately have zero handlers; this is informational only.");

    private const string MigrationCategory = "CloudMesh.Mediator.Migration";

    /// <summary>
    /// A handler using the MediatR-compatibility shape (method <c>Handle</c>, returns <see cref="System.Threading.Tasks.Task"/>).
    /// Informational migration hint: the native shape avoids an adapter and a <c>Task</c> allocation. Elevate via
    /// <c>.editorconfig</c>: <c>dotnet_diagnostic.CMED100.severity = warning</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor CompatShimHandler = new(
        id: "CMED100",
        title: "Handler uses the MediatR-compatibility shape",
        messageFormat: "Handler '{0}' uses the MediatR-compatibility shape (Handle/Task). The native CloudMesh.Mediator.IRequestHandler/INotificationHandler (HandleAsync/ValueTask) avoids an adapter and a Task allocation.",
        category: MigrationCategory,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "MediatR-shaped handlers are supported via a runtime adapter. Migrating to the native shape removes the adapter and a per-call Task allocation.");

    private const string PerformanceCategory = "CloudMesh.Mediator.Performance";

    /// <summary>
    /// A send/stream call bound to the boxing fallback overload for a concrete request type (the request is boxed).
    /// Informational performance hint. Reported by <see cref="BoxingSendAnalyzer"/>. Elevate via
    /// <c>.editorconfig</c>: <c>dotnet_diagnostic.CMED200.severity = warning</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor BoxingSend = new(
        id: "CMED200",
        title: "Send/stream call boxes the value-type request",
        messageFormat: "Sending value-type request '{0}' via the fallback overload boxes it. Prefer the source-generated box-free overload — ensure the CloudMesh.Mediator source generator runs in the assembly declaring '{0}' — or call SendAsync<{0}, {1}>(in request) explicitly.",
        category: PerformanceCategory,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The ergonomic SendAsync/StreamAsync fallback boxes a value-type request when it is converted to the IRequest<T> parameter. A source-generated box-free overload eliminates the box; ensure the generator runs in the request's assembly.");
}
