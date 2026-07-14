# CloudMesh

The umbrella package for **CloudMesh** — a collection of small, focused, high-performance .NET building blocks for
cloud solutions. Each component ships as its own NuGet package with a single responsibility and minimal
dependencies, so you normally install just the pieces you need.

- **Targets:** .NET 8, 9, 10 — **License:** MIT

## Prefer the focused packages

CloudMesh is designed à la carte — reference the individual packages directly:

```bash
dotnet add package CloudMesh.Core
dotnet add package CloudMesh.Mediator
dotnet add package CloudMesh.Variant
dotnet add package CloudMesh.Uuid
# ...and so on
```

See the full list, with descriptions and links to each package's documentation, in the
[CloudMesh repository README](https://github.com/JessieWadman/cloudmesh#packages-at-a-glance).

Highlights:

- **CloudMesh.Core** — async coordination primitives, fast parsing, buffered IO, utility collections.
- **CloudMesh.Mediator** — a fast, zero-allocation mediator for in-process CQRS (a MediatR alternative).
- **CloudMesh.Variant** — a boxing-free discriminated union for value types.
- **CloudMesh.Uuid** / **CloudMesh.Guid64** — fast, sortable identifiers.
- **CloudMesh.DataBlocks** — an in-process actor/pipeline library.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
