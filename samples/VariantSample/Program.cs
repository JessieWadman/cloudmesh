using CloudMesh.Variant;

// CloudMesh.Variant.Value is a .NET 11 union type. Unlike a compiler-declared `union`, which stores every
// case as an `object?` and therefore boxes value types, Value keeps primitives inline in a 24-byte struct and
// implements the union contract's non-boxing access pattern. Pattern matching over it allocates nothing.

int? presentInt = 42;
int? absentInt = null;

Value[] values =
[
    presentInt,        // int? with a value  — case type int?, stored inline
    absentInt,         // int? without one   — matches the null pattern
    "hello",
    3.5,
    Guid.NewGuid(),
    DateTime.UnixEpoch,
    default,           // null
];

foreach (var value in values)
{
    Console.WriteLine(Describe(value));
}

// Union pattern matching: the patterns apply to the union's contents, not to the Value struct itself.
// Each type pattern below compiles to a TryGetValue(out T) call, so no case boxes.
static string Describe(Value value) => value switch
{
    int i => $"int: {i}",
    double d => $"double: {d}",
    Guid g => $"guid: {g}",
    DateTime dt => $"date: {dt:O}",
    string s => $"string: {s}",
    null => "null",
    object o => $"other ({o.GetType().Name}): {o}",
};

// The implicit union conversion from each case type works the same way as for a declared union.
Value answer = 42;
Console.WriteLine($"answer is int: {answer is int}");
Console.WriteLine($"answer stores: {answer.Type}");

// Proof that matching an inline value type costs nothing.
Measure(answer);

static void Measure(Value value)
{
    // Warm up, so JIT compilation is not counted.
    _ = Sum(value, iterations: 1);

    var before = GC.GetAllocatedBytesForCurrentThread();
    var total = Sum(value, iterations: 1_000_000);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

    Console.WriteLine($"1,000,000 matches summed to {total} and allocated {allocated} bytes");
}

static long Sum(Value value, int iterations)
{
    long total = 0;

    for (var i = 0; i < iterations; i++)
    {
        total += value switch
        {
            int n => n,
            long n => n,
            null => 0,
            object => -1,
        };
    }

    return total;
}
