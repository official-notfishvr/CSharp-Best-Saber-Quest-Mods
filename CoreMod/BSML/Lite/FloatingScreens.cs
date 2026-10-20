using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoreMod;

public static partial class BSMLLite
{
    public static BSML.FloatingScreen CreateFloatingScreen(Vector2 screenSize, Vector3 position, Vector3 rotation, float curvatureRadius = 0.0f, bool hasBackground = true, bool createHandle = true, BSML.Side handleSide = BSML.Side.Full) => default!;

    public static BSML.ProgressBar CreateProgressBar(Vector3 position, string headerText, string subText1 = "", string subText2 = "") => default!;

    public static BSML.ProgressBar CreateProgressBar(Vector3 position, Vector3 rotation, string headerText, string subText1 = "", string subText2 = "") => default!;

    public static BSML.ProgressBar CreateProgressBar(Vector3 position, Vector3 rotation, Vector3 scale, string headerText, string subText1 = "", string subText2 = "") => default!;
}
