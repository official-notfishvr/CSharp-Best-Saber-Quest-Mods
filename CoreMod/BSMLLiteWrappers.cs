namespace BSML.Lite;

public readonly struct GameObjectWrapper
{
    private readonly object? _value;

    public GameObjectWrapper(object? value)
    {
        _value = value;
    }
}

public readonly struct TransformWrapper
{
    private readonly object? _value;

    public TransformWrapper(object? value)
    {
        _value = value;
    }
}
