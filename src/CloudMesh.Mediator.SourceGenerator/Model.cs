using System;

namespace CloudMesh.Mediator.SourceGenerator;

/// <summary>Which mediator interface a discovered registration corresponds to.</summary>
internal enum RegistrationKind
{
    RequestHandler,
    StreamRequestHandler,
    NotificationHandler,
    PipelineBehavior,
    StreamPipelineBehavior,
    CompatRequestHandler,
    CompatNotificationHandler,
    // Open-generic behaviors (e.g. LoggingBehavior<TReq,TResp> : IPipelineBehavior<TReq,TResp>).
    // Registered as an open-generic descriptor; MS DI closes them at resolution time.
    OpenGenericPipelineBehavior,
    OpenGenericStreamPipelineBehavior,
}

/// <summary>
/// A value-equatable, ordered stand-in for <see cref="Microsoft.CodeAnalysis.Accessibility"/> that carries no
/// Roslyn symbols. Lower values are less accessible; used to compute effective accessibility and to decide
/// whether generated code may reference/emit a type or method.
/// </summary>
internal enum Access
{
    NotApplicable = 0,
    Private = 1,
    ProtectedAndInternal = 2, // private protected
    Protected = 3,
    Internal = 4,
    ProtectedOrInternal = 5,  // protected internal
    Public = 6,
}

/// <summary>
/// A single discovered handler/behavior registration, fully described by strings/enums so it is value-equatable
/// and carries no Roslyn symbols into the incremental pipeline.
/// </summary>
internal readonly record struct RegistrationInfo(
    RegistrationKind Kind,
    // Fully-qualified (global::) name of the concrete implementing type. For open generics this is the
    // OPEN form, e.g. "global::Ns.LoggingBehavior&lt;,&gt;".
    string ImplementationType,
    // Fully-qualified name of the request/notification type (first type arg). Empty for open generics.
    string MessageType,
    // Fully-qualified name of the response type (second type arg), or null when not applicable.
    string? ResponseType,
    // Minimum effective accessibility across the implementation, message and response types. If this is
    // below Internal (i.e. private/protected/private-protected), generated code cannot reference it and the
    // registration is skipped.
    Access EffectiveAccess,
    // The implementing type's identifier location, for diagnostics reported on the declaration (e.g. CMED100).
    LocationInfo? Location) : IEquatable<RegistrationInfo>;

/// <summary>
/// A concrete request type (IRequest&lt;T&gt; or IStreamRequest&lt;T&gt;) declared in the compilation, used to
/// emit zero-boxing ergonomic overloads and to power CMED001 diagnostics.
/// </summary>
internal readonly record struct RequestTypeInfo(
    string RequestType,
    string ResponseType,
    bool IsStream,
    // Effective accessibility = min(request, response). Decides whether the overload is public, internal, or skipped.
    Access EffectiveAccess,
    // Location for diagnostics, as an opaque, equatable descriptor.
    LocationInfo? Location) : IEquatable<RequestTypeInfo>;

/// <summary>A concrete notification type declared in the compilation (for CMED003).</summary>
internal readonly record struct NotificationTypeInfo(
    string NotificationType,
    LocationInfo? Location) : IEquatable<NotificationTypeInfo>;

/// <summary>Value-equatable stand-in for a Roslyn Location so it can flow through the pipeline.</summary>
internal readonly record struct LocationInfo(
    string FilePath,
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter) : IEquatable<LocationInfo>;

/// <summary>
/// The combined per-candidate transform result. A single type declaration can contribute registrations
/// (if it is a handler/behavior) and/or a request/notification classification (if it is a message type),
/// so both pipelines are merged into one semantic transform to avoid doing the semantic work twice.
/// </summary>
internal readonly record struct CandidateResult(
    EquatableArray<RegistrationInfo> Registrations,
    RequestTypeInfo? Request,
    NotificationTypeInfo? Notification) : IEquatable<CandidateResult>;
