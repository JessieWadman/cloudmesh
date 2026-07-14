; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------------------------------------
CMED001 | CloudMesh.Mediator | Info | Request type has no handler in this compilation
CMED002 | CloudMesh.Mediator | Error | Multiple handlers for one request/response pair
CMED003 | CloudMesh.Mediator | Info | Notification type has no handlers
CMED100 | CloudMesh.Mediator.Migration | Info | Handler uses the MediatR-compatibility shape
CMED200 | CloudMesh.Mediator.Performance | Info | Send/stream call boxes the request
