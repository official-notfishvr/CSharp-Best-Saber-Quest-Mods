namespace CsToCppTranspiler;

[AttributeUsage(AttributeTargets.Method)]
public class HookAttribute : Attribute
{
    public string MethodName { get; }
    public string? ClassName { get; set; }

    public HookAttribute(string methodName)
    {
        MethodName = methodName;
    }
}

[AttributeUsage(AttributeTargets.Class)]
public class ModAttribute : Attribute
{
    public string Id { get; }
    public string Version { get; }

    public ModAttribute(string id, string version)
    {
        Id = id;
        Version = version;
    }
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ConfigAttribute : Attribute
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public object? DefaultValue { get; set; }
}
