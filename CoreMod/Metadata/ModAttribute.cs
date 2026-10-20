namespace CoreMod;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ModAttribute : Attribute
{
    public string Id { get; }
    public string Version { get; }

    public ModAttribute(string id, string version)
    {
        Id = id;
        Version = version;
    }
}
