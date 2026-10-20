using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoreMod;

public static partial class BSMLLite
{
    public static HoverHint AddHoverHint(GameObject gameObject, string text) => default!;

    public static Button CreateUIButton(Transform parent, string buttonText, Action? onClick = null) => default!;

    public static Button CreateUIButton(Transform parent, string buttonText, Vector2 anchoredPosition, Action? onClick = null) => default!;

    public static Button CreateUIButton(Transform parent, string buttonText, Vector2 anchoredPosition, Vector2 sizeDelta, Action? onClick = null) => default!;

    public static Button CreateUIButton(Transform parent, string buttonText, string buttonTemplate, Action? onClick = null) => default!;

    public static Button CreateUIButton(Transform parent, string buttonText, string buttonTemplate, Vector2 anchoredPosition, Action? onClick = null) => default!;

    public static Button CreateUIButton(Transform parent, string buttonText, string buttonTemplate, Vector2 anchoredPosition, Vector2 sizeDelta, Action? onClick = null) => default!;

    public static void SetButtonText(Button button, string text) { }

    public static void SetButtonTextSize(Button button, float fontSize) { }

    public static void ToggleButtonWordWrapping(Button button, bool enableWordWrapping) { }

    public static void SetButtonIcon(Button button, Sprite icon) { }

    public static void SetButtonBackground(Button button, Sprite background) { }

    public static void SetButtonSprites(Button button, Sprite inactive, Sprite active) { }
}
