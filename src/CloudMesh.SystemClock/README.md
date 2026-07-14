# CloudMesh.SystemClock

> ⚠️ **Deprecated — use the BCL [`TimeProvider`](https://learn.microsoft.com/dotnet/api/system.timeprovider)
> instead.**

This package predates .NET 8's built-in `TimeProvider`. It provided an injectable system clock (`ISystemClock`)
so tests could use predictable, well-known points in time. .NET 8 covers that need natively, so this package is
retired: its types are marked `[Obsolete]` and **throw `NotSupportedException` if constructed**.

- **Targets:** .NET 8, 9 — **License:** MIT

## Migrating

Replace `ISystemClock` with `TimeProvider`:

```csharp
// before
public MyService(ISystemClock clock) { _now = clock.UtcNow; }

// after — inject the BCL TimeProvider (register TimeProvider.System, or a FakeTimeProvider in tests)
public MyService(TimeProvider clock) { _now = clock.GetUtcNow(); }
```

`TimeProvider.System` is the real clock; `Microsoft.Extensions.TimeProvider.Testing`'s `FakeTimeProvider` gives
you the controllable clock you used `MockSystemClock` for.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
