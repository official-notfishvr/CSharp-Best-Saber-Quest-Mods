using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoreMod;

public static partial class BSMLLite
{
    public static BSML.ModalView CreateModal(Transform parent, bool dismissOnBlockerClicked = true) => default!;

    public static BSML.ModalView CreateModal(Transform parent, Vector2 sizeDelta, Action? onBlockerClicked = null, bool dismissOnBlockerClicked = true) => default!;

    public static BSML.ModalView CreateModal(Transform parent, Vector2 anchoredPosition, Vector2 sizeDelta, Action? onBlockerClicked = null, bool dismissOnBlockerClicked = true) => default!;

    public static GameObject CreateScrollableModalContainer(BSML.ModalView modal) => default!;

    public static VerticalLayoutGroup CreateModifierContainer(Transform parent) => default!;
}
