namespace CoreMod;

public enum PatchKind
{
    Replace = 0,
    Before = 1,
    After = 2,
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PatchAttribute : Attribute
{
    public Type TargetType { get; }
    public string MethodName { get; }
    public PatchKind Kind { get; }

    public PatchAttribute(Type targetType, string methodName, PatchKind kind = PatchKind.Replace)
    {
        TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
        MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
        Kind = kind;
    }
}
