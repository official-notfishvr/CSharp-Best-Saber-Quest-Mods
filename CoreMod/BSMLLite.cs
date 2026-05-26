using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoreMod;

public static class BSMLLite
{
    public static Vector2 Vector2(float x, float y) => default;

    public static Vector3 Vector3(float x, float y, float z) => default;

    public static Color Color(float r, float g, float b, float a = 1.0f) => default;

    public static GameObject CreateCanvas() => default!;

    public static HoverHint AddHoverHint(object gameObject, string text) => default!;

    public static Button CreateUIButton(object parent, string buttonText, Action? onClick = null) => default!;

    public static Button CreateUIButton(object parent, string buttonText, Vector2 anchoredPosition, Action? onClick = null) => default!;

    public static Button CreateUIButton(object parent, string buttonText, Vector2 anchoredPosition, Vector2 sizeDelta, Action? onClick = null) => default!;

    public static Button CreateUIButton(object parent, string buttonText, string buttonTemplate, Action? onClick = null) => default!;

    public static Button CreateUIButton(object parent, string buttonText, string buttonTemplate, Vector2 anchoredPosition, Action? onClick = null) => default!;

    public static Button CreateUIButton(object parent, string buttonText, string buttonTemplate, Vector2 anchoredPosition, Vector2 sizeDelta, Action? onClick = null) => default!;

    public static void SetButtonText(Button button, string text) { }

    public static void SetButtonTextSize(Button button, float fontSize) { }

    public static void ToggleButtonWordWrapping(Button button, bool enableWordWrapping) { }

    public static void SetButtonIcon(Button button, Sprite icon) { }

    public static void SetButtonBackground(Button button, Sprite background) { }

    public static void SetButtonSprites(Button button, Sprite inactive, Sprite active) { }

    public static CurvedTextMeshPro CreateText(object parent, string text) => default!;

    public static CurvedTextMeshPro CreateText(object parent, string text, Vector2 anchoredPosition) => default!;

    public static CurvedTextMeshPro CreateText(object parent, string text, Vector2 anchoredPosition, Vector2 sizeDelta) => default!;

    public static CurvedTextMeshPro CreateText(object parent, string text, float fontSize) => default!;

    public static CurvedTextMeshPro CreateText(object parent, string text, float fontSize, Vector2 anchoredPosition) => default!;

    public static CurvedTextMeshPro CreateText(object parent, string text, float fontSize, Vector2 anchoredPosition, Vector2 sizeDelta) => default!;

    public static CurvedTextMeshPro CreateText(object parent, string text, FontStyles fontStyle) => default!;

    public static CurvedTextMeshPro CreateText(object parent, string text, FontStyles fontStyle, Vector2 anchoredPosition) => default!;

    public static CurvedTextMeshPro CreateText(object parent, string text, FontStyles fontStyle, Vector2 anchoredPosition, Vector2 sizeDelta) => default!;

    public static CurvedTextMeshPro CreateText(object parent, string text, FontStyles fontStyle, float fontSize, Vector2 anchoredPosition, Vector2 sizeDelta) => default!;

    public static ImageView CreateImage(object parent, Sprite sprite) => default!;

    public static ImageView CreateImage(object parent, Sprite sprite, Vector2 anchoredPosition) => default!;

    public static ImageView CreateImage(object parent, Sprite sprite, Vector2 anchoredPosition, Vector2 sizeDelta) => default!;

    public static RawImage CreateRawImage(object parent, Texture texture) => default!;

    public static RawImage CreateRawImage(object parent, Texture texture, Vector2 anchoredPosition) => default!;

    public static RawImage CreateRawImage(object parent, Texture texture, Vector2 anchoredPosition, Vector2 sizeDelta) => default!;

    public static Sprite FileToSprite(string filePath) => default!;

    public static Sprite TextureToSprite(Texture2D texture) => default!;

    public static Sprite Base64ToSprite(string base64) => default!;

    public static Sprite ArrayToSprite(byte[] bytes) => default!;

    public static VerticalLayoutGroup CreateVerticalLayoutGroup(object parent) => default!;

    public static HorizontalLayoutGroup CreateHorizontalLayoutGroup(object parent) => default!;

    public static GridLayoutGroup CreateGridLayoutGroup(object parent) => default!;

    public static StackLayoutGroup CreateStackLayoutGroup(object parent) => default!;

    public static GameObject CreateScrollableSettingsContainer(object parent) => default!;

    public static GameObject CreateScrollView(object parent) => default!;

    public static BSML.ModalView CreateModal(object parent, bool dismissOnBlockerClicked = true) => default!;

    public static BSML.ModalView CreateModal(object parent, Vector2 sizeDelta, Action? onBlockerClicked = null, bool dismissOnBlockerClicked = true) => default!;

    public static BSML.ModalView CreateModal(object parent, Vector2 anchoredPosition, Vector2 sizeDelta, Action? onBlockerClicked = null, bool dismissOnBlockerClicked = true) => default!;

    public static GameObject CreateScrollableModalContainer(BSML.ModalView modal) => default!;

    public static VerticalLayoutGroup CreateModifierContainer(object parent) => default!;

    public static BSML.FloatingScreen CreateFloatingScreen(Vector2 screenSize, Vector3 position, Vector3 rotation, float curvatureRadius = 0.0f, bool hasBackground = true, bool createHandle = true, BSML.Side handleSide = BSML.Side.Full) => default!;

    public static BSML.ProgressBar CreateProgressBar(Vector3 position, string headerText, string subText1 = "", string subText2 = "") => default!;

    public static BSML.ProgressBar CreateProgressBar(Vector3 position, Vector3 rotation, string headerText, string subText1 = "", string subText2 = "") => default!;

    public static BSML.ProgressBar CreateProgressBar(Vector3 position, Vector3 rotation, Vector3 scale, string headerText, string subText1 = "", string subText2 = "") => default!;
}
