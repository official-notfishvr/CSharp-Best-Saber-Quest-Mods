namespace CoreMod;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ConfigAttribute : Attribute
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
}
