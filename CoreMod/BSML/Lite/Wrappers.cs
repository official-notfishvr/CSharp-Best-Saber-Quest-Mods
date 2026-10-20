using UnityEngine;

namespace BSML.Lite;

public readonly struct GameObjectWrapper
{
    private readonly GameObject? _value;

    public GameObjectWrapper(GameObject? value)
    {
        _value = value;
    }

    public GameObjectWrapper(Component? value)
    {
        _value = value == null ? null : value.get_GameObject();
    }
}

public readonly struct TransformWrapper
{
    private readonly Transform? _value;

    public TransformWrapper(Transform? value)
    {
        _value = value;
    }

    public TransformWrapper(GameObject? value)
    {
        _value = value == null ? null : value.transform;
    }

    public TransformWrapper(Component? value)
    {
        _value = value == null ? null : value.get_GameObject().transform;
    }
}
