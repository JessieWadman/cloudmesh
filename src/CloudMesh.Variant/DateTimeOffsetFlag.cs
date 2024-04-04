﻿namespace CloudMesh.Variant;

public readonly partial struct Value
{
    private sealed class DateTimeOffsetFlag : TypeFlag<DateTimeOffset>
    {
        public static DateTimeOffsetFlag Instance { get; } = new();

        public override DateTimeOffset To(in Value value)
            => new(new DateTime(value._union.Ticks, DateTimeKind.Utc));
    }
}