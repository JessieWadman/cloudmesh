# CloudMesh.Temporal

**Effective-dated records** for .NET — store a value's history *and* its scheduled future changes in one
object, then reconstruct how it looked (or will look) on any date.

Instead of mutating an object in place, `Temporal<T>` records changes as a timeline of
`(effective date → property → value)` entries. Ask for a **point-in-time snapshot** and it replays every change
effective on or before that date to materialize the object as it stood then. Past values, the present, and
pending future changes all live side by side in a single instance.

- **Targets:** .NET 8, 9, 10 — **License:** MIT
- Nested property selectors (`e => e.Address.City`), powered by
  [CloudMesh.DotNotation](https://github.com/JessieWadman/cloudmesh/tree/main/src/CloudMesh.DotNotation).
- JSON (de)serialization of the pending-change timeline, and history compaction.

---

## Install

```bash
dotnet add package CloudMesh.Temporal
```

## Quick start

```csharp
using CloudMesh.Temporal;

public class Employee
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public decimal Salary { get; set; }
}

var employee = new Temporal<Employee>();

employee.Set(default, e => e.Name, "Bob");                         // baseline value
employee.Set(default, e => e.Salary, 50_000m);
employee.Set(new DateOnly(2024, 12, 01), e => e.Name, "Bobby");    // scheduled future change
employee.Set(new DateOnly(2025, 01, 01), e => e.Salary, 55_000m);  // future raise

// Point-in-time snapshots:
employee.GetAt(default).Name;                    // "Bob"
employee.GetAt(new DateOnly(2024, 11, 30)).Name; // "Bob"    (change not yet effective)
employee.GetAt(new DateOnly(2024, 12, 01)).Name; // "Bobby"
employee.GetAt(new DateOnly(2025, 06, 01)).Salary; // 55_000  (raise applied)

// Read a single property as-of a date, without building a whole snapshot:
decimal salaryToday = employee.Get(DateOnly.FromDateTime(DateTime.UtcNow), e => e.Salary);
```

## Core concepts

### The timeline

A change is `(effective date, property, value)`. `GetAt(d)` builds a snapshot by replaying, in chronological
order, every change whose effective date is **on or before `d`** — so later values overwrite earlier ones for
the same property, and anything dated after `d` is ignored. Use `default(DateOnly)` for baseline values
(identifiers, defaults) that apply "since the beginning of time".

### Setting a future value clears later pending changes

By default, `Set(...)` treats a value as authoritative **from its date onward**, so it removes any
already-scheduled changes to the *same* property at *later* dates:

```csharp
employee.Set(new DateOnly(2025, 01, 01), e => e.Salary, 55_000m);
employee.Set(new DateOnly(2025, 06, 01), e => e.Salary, 60_000m);

// Overriding January's raise wipes the June one too:
employee.Set(new DateOnly(2025, 01, 01), e => e.Salary, 52_000m);
employee.GetAt(new DateOnly(2025, 07, 01)).Salary; // 52_000

// Opt out to keep later scheduled changes intact:
employee.Set(new DateOnly(2025, 01, 01), e => e.Salary, 52_000m, clearFutureChanges: false);
```

### Nested properties

Selectors may drill into nested objects; the snapshot auto-creates the intermediate objects it needs:

```csharp
employee.Set(default, e => e.Address.City, "Stockholm");
```

### Stamping invariants onto every snapshot

Derive from `Temporal<T>` and override `BeforeReturnSnapshot` to apply values (e.g. a primary key) to every
materialized snapshot:

```csharp
public sealed class EmployeeRecord : Temporal<Employee>
{
    public int Id { get; init; }
    protected override void BeforeReturnSnapshot(Employee snapshot, DateOnly pointInTime)
        => snapshot.Id = Id;
}
```

### Compaction and persistence

- **`ReduceTo(date)`** folds all history on or before `date` into a single baseline entry (dropping values
  that equal their type default), while leaving later scheduled changes untouched — handy for bounding how
  much history an instance carries.
- **`SerializePendingChanges` / `DeserializePendingChanges`** move the timeline to and from JSON via
  `Utf8JsonWriter`/`Utf8JsonReader`. `GetPointInTimes()` enumerates the recorded effective dates.

## Use cases

- Employee/contract records with future-dated raises, role changes, or price lists.
- Feature flags and configuration that flip on a schedule.
- Any "as-of" / bitemporal-style query where you need to reproduce state at an arbitrary date.

## Gotchas

- **`T` needs a public parameterless constructor and writable properties** — snapshots are created via
  `Activator.CreateInstance<T>()` and populated through DotNotation.
- **Effective dates are `DateOnly`** (day granularity); a change takes effect for the whole of its date and all
  later dates until superseded.
- **`Set` defaults to clearing later changes** to the same property — pass `clearFutureChanges: false` when
  you're inserting a value *between* existing scheduled changes.
- **A value that can't be assigned to its target property** throws `InvalidCastException` from `GetAt`.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
