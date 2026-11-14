# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CloudMesh is a collection of focused .NET NuGet packages providing reusable building blocks for cloud solutions. Each package has a single responsibility and minimal cross-dependencies. Author: Jessie Wadman. License: MIT.

## Build & Test Commands

```bash
# Build entire solution
dotnet build CloudMesh.sln

# Build release
dotnet build CloudMesh.sln -c Release

# Run all tests
dotnet test

# Run tests for a specific framework
dotnet test -f net10.0

# Run a single test project
dotnet test test/CloudMesh.Core.Tests

# Run a specific test
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Run benchmarks
dotnet run -c Release --project benchmarks/MemoryStreamAccessorBenchmarks
```

No separate lint command exists; the compiler enforces quality via `TreatWarningsAsErrors=true` in `Directory.Build.props`.

## Compiler & Language Settings (Directory.Build.props)

- **Target frameworks:** net8.0, net9.0, net10.0, net11.0
- **LangVersion:** preview (latest C# features allowed)
- **TreatWarningsAsErrors:** true
- **Nullable:** enabled globally
- **AllowUnsafeBlocks:** true (unsafe code is used for performance)
- **ImplicitUsings:** enabled
- **CS1591 suppressed** (XML doc comments on public members not enforced)

Dependencies are centrally managed in `Directory.Packages.props` — always use `<PackageReference Include="..." />` without explicit versions in individual .csproj files.

## Architecture

### Package Dependency Graph

```
CloudMesh (meta-package)
├── CloudMesh.Core           ← foundation: threading (AsyncLock, Throttler), parsing, collections
├── CloudMesh.Guid64         ← Twitter Snowflake 64-bit sortable IDs
├── CloudMesh.Uuid           ← UUID v7 (time-sortable, faster than Guid.NewGuid)
├── CloudMesh.Variant        ← Non-boxing discriminated union via StructLayout
├── CloudMesh.MurmurHash     ← MurmurHash 2/3 (from Akka.NET)
├── CloudMesh.SystemClock    ← Deprecated; use TimeProvider instead
└── CloudMesh.DataBlocks     → depends on Core
    ← Actor-like message processing over System.Threading.Channels

CloudMesh.Persistence.DynamoDB → Core, Guid64, MurmurHash
    ← Generic repository pattern for DynamoDB with fluent query API

CloudMesh.Temporal → DotNotation
    ← Versioned properties with effective dates and point-in-time snapshots

CloudMesh.NetworkMutex.Postgres  → NetworkMutex.Abstractions
CloudMesh.NetworkMutex.DynamoDB  → NetworkMutex.Abstractions
    ← Distributed locks across machines
```

### DataBlocks (actor-like framework)

Channel-based in-process message processing. Key types:
- `DataBlock` — base consumer with typed message handlers via `ReceiveAsync<T>()`
- `BufferBlock<T>` — batches by capacity + time window
- `AggregationDataBlock<T>` — fan-in with time-windowed aggregation
- `RoundRobinDataBlock` / `SpillOverDataBlock` — fan-out strategies
- `Envelope` — wraps messages with sender ref and sequence ID
- `CaptureBlock` — collects messages for testing

### DynamoDB Persistence

`DynamoDBRepository<T>` with fluent queries, secondary index support, partial updates, and an in-memory mock (`InMemoryDynamoDBRepository<T>`) for unit tests. DI registration via extension methods.

### Variant

Stack-allocated discriminated union using `StructLayout(LayoutKind.Explicit)` with field overlapping — always ≤32 bytes, zero heap allocation.

## Testing

- **Framework:** xUnit 2.9.3 with Moq
- **Test projects:** under `test/` — mirror `src/` package names with `.Tests` suffix
- **CI runs tests against all four target frameworks** (net8.0, net9.0, net10.0, net11.0) on PRs

## CI/CD

- **PR validation** (`.github/workflows/pull-requests.yml`): tests against net8.0, net9.0, net10.0, net11.0 in parallel
- **Publish** (`.github/workflows/main.yml`): tests net10.0 and net11.0, builds Release, publishes NuGet packages
- Every CI job installs the 8.0/9.0/10.0 SDKs plus the 11.0 SDK — the newest SDK is required to evaluate `TargetFrameworks` even when a job builds a single framework
- Version format: `2.0.$GITHUB_RUN_NUMBER`
