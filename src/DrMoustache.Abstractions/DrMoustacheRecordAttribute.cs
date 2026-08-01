namespace DrMoustache.Abstractions;

/// <summary>Marks a type as a record shape supplied to the rendering engine.</summary>
/// <param name="name">Business name for the shape, e.g. <c>failed-order</c>.
/// Reported in validation errors.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class DrMoustacheRecordAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
