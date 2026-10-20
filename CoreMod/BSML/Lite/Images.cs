using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoreMod;

public static partial class BSMLLite
{
    public static ImageView CreateImage(Transform parent, Sprite sprite) => default!;

    public static ImageView CreateImage(Transform parent, Sprite sprite, Vector2 anchoredPosition) => default!;

    public static ImageView CreateImage(Transform parent, Sprite sprite, Vector2 anchoredPosition, Vector2 sizeDelta) => default!;

    public static RawImage CreateRawImage(Transform parent, Texture texture) => default!;

    public static RawImage CreateRawImage(Transform parent, Texture texture, Vector2 anchoredPosition) => default!;

    public static RawImage CreateRawImage(Transform parent, Texture texture, Vector2 anchoredPosition, Vector2 sizeDelta) => default!;

    public static Sprite FileToSprite(string filePath) => default!;

    public static Sprite TextureToSprite(Texture2D texture) => default!;

    public static Sprite Base64ToSprite(string base64) => default!;

    public static Sprite ArrayToSprite(byte[] bytes) => default!;
}
