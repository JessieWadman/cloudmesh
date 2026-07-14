namespace CloudMesh.Mediator.Compatibility;

public readonly struct Unit
{
    public static readonly Unit Value = default;
    public static implicit operator NoResponse(Unit unit) => NoResponse.Value;
}