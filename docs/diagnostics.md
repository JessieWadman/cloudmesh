# CloudMesh.Mediator diagnostics

The `CloudMesh.Mediator` source generator (and its companion analyzer) emit compile-time diagnostics to catch
mediator misconfigurations early and suggest performance/migration improvements. IDs, categories, and default
severities are listed below.

| ID      | Category                        | Default  | Meaning |
|---------|---------------------------------|----------|---------|
| CMED001 | CloudMesh.Mediator              | Info     | An `IRequest<T>`/`IStreamRequest<T>` type has **no handler in this compilation**. Info by default because the handler may legitimately live in a *referenced* assembly (a contracts/handlers split). Elevate for single-assembly setups. |
| CMED002 | CloudMesh.Mediator              | **Error**| More than one handler for the **same `(request, response)` pair**. (A request implementing `IRequest<A>` and `IRequest<B>` may have one handler for each — that is *not* a duplicate.) |
| CMED003 | CloudMesh.Mediator              | Info     | An `INotification` type has **no handlers**. Legitimate (publishing is a no-op); informational only. |
| CMED100 | CloudMesh.Mediator.Migration    | Info     | A handler uses the **MediatR-compatibility shape** (`Handle`/`Task`). The native shape (`HandleAsync`/`ValueTask`) avoids an adapter and a `Task` allocation. |
| CMED200 | CloudMesh.Mediator.Performance  | Info     | A `SendAsync`/`StreamAsync` call binds to the **boxing fallback overload** for a **value-type (struct)** request — the request is boxed. Prefer the source-generated box-free overload (ensure the generator runs in the request's assembly) or call `SendAsync<TRequest, TResponse>(in request)` explicitly. |

`CMED001`, `CMED003`, `CMED100`, and `CMED200` default to **Info** (a.k.a. Suggestion), so they never break a
build that uses `TreatWarningsAsErrors`. `CMED002` is an **Error** because two handlers for one `(request, response)`
pair is always a bug. All severities are configurable via `.editorconfig`.

## Opt in to stricter enforcement

Add any of the following to your `.editorconfig` to make the suggestions build-breaking (or to silence one):

```ini
# Make CloudMesh.Mediator suggestions build-breaking (opt-in)
dotnet_diagnostic.CMED001.severity = warning   # missing handler (reliable only in single-assembly setups)
dotnet_diagnostic.CMED100.severity = warning   # discourage MediatR-compat shims
dotnet_diagnostic.CMED200.severity = warning   # discourage boxing sends

# Or silence one you don't care about:
# dotnet_diagnostic.CMED003.severity = none
```

Use `error` instead of `warning` to fail the build outright, or `none`/`silent` to suppress.

### Note on CMED001 across assemblies

CMED001 only sees handlers declared in the **current** compilation. In a contracts/handlers split (request types
in one assembly, handlers in another), the request's assembly will report CMED001 even though a handler exists
elsewhere — which is why it defaults to Info. Only elevate CMED001 to `warning`/`error` in projects where the
request **and** its handler are compiled together.

### Note on CMED200 and the source generator

CMED200 fires only when a call binds to the boxing fallback for a **value-type (struct)** request — that is the
case that actually boxes. Reference-type requests don't box (so they're not flagged), and it never fires when a
source-generated box-free overload is in scope, when you call the generic primitive
`SendAsync<TRequest, TResponse>(in request)`, or when the argument's static type is an interface/abstract
(genuinely dynamic dispatch). If you see CMED200 on a struct request, ensure the `CloudMesh.Mediator` source
generator runs in the assembly that **declares** that request type so the box-free overload is emitted.
