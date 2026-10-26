using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Settings;
using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;

[assembly: AssemblyTitle("PauseKey")]
[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace PauseKey;

[Plugin(RuntimeOptions.SingleStartInit)]
public sealed class Plugin
{
    private Harmony? _harmony;

    [Init]
    public void Init(Config config)
    {
        PluginConfig.Instance = config.Generated<PluginConfig>();
        PauseKeySettings.PauseBinding = PluginConfig.Instance.PauseBinding;
        _harmony = new Harmony("com.example.pausekey");
    }

    [OnStart]
    public void OnStart()
    {
        _harmony?.PatchAll(typeof(Plugin).Assembly);
        BeatSaberMarkupLanguage.Util.MainMenuAwaiter.MainMenuInitializing += OnMainMenuInitializing;
    }

    [OnExit]
    public void OnExit()
    {
        _harmony?.UnpatchSelf();
        _harmony = null;
        BeatSaberMarkupLanguage.Util.MainMenuAwaiter.MainMenuInitializing -= OnMainMenuInitializing;
        if (BSMLSettings.Instance != null)
            BSMLSettings.Instance.RemoveSettingsMenu(SettingsUI.Instance);
    }

    private static void OnMainMenuInitializing() => BSMLSettings.Instance.AddSettingsMenu("Pause Key", "PauseKey.SettingsUI.bsml", SettingsUI.Instance);
}

internal class PluginConfig
{
    public static PluginConfig Instance { get; set; } = null!;
    public virtual int PauseBinding { get; set; } = 1;
}

internal sealed class SettingsUI
{
    public static readonly SettingsUI Instance = new();

    [UIValue("pause-input-choices")]
    private readonly List<object> _choices = new() { "Default menu button", "Left trigger", "Right trigger", "Left thumbstick", "Right thumbstick" };

    [UIValue("pause-input-value")]
    public string PauseInput
    {
        get => (string)_choices[PauseKeySettings.PauseBinding];
        set
        {
            PauseKeySettings.PauseBinding = _choices.IndexOf(value);
            PluginConfig.Instance.PauseBinding = PauseKeySettings.PauseBinding;
        }
    }
}

internal static class PauseKeySettings
{
    public static int PauseBinding = 1;
}

[HarmonyPatch(typeof(UnityXRHelper), nameof(UnityXRHelper.GetMenuButton))]
internal static class PauseButtonPatch
{
    [HarmonyPostfix]
    private static void GetMenuButtonPostfix(UnityXRHelper __instance, ref bool __result)
    {
        if (PauseKeySettings.PauseBinding == 0)
            return;
        var pressed = false;
        switch (PauseKeySettings.PauseBinding)
        {
            case 1:
                pressed = __instance.GetTriggerValue(UnityEngine.XR.XRNode.LeftHand) >= 0.75f;
                break;
            case 2:
                pressed = __instance.GetTriggerValue(UnityEngine.XR.XRNode.RightHand) >= 0.75f;
                break;
            case 3:
                pressed = UnityEngine.Vector2.SqrMagnitude(__instance.GetThumbstickValue(UnityEngine.XR.XRNode.LeftHand)) >= 0.7225f;
                break;
            case 4:
                pressed = UnityEngine.Vector2.SqrMagnitude(__instance.GetThumbstickValue(UnityEngine.XR.XRNode.RightHand)) >= 0.7225f;
                break;
        }
        __result = pressed;
    }
}

[HarmonyPatch(typeof(UnityXRHelper), nameof(UnityXRHelper.GetMenuButtonDown))]
internal static class PauseButtonDownPatch
{
    private static bool WasPressed;

    [HarmonyPostfix]
    private static void GetMenuButtonDownPostfix(UnityXRHelper __instance, ref bool __result)
    {
        if (PauseKeySettings.PauseBinding == 0)
        {
            WasPressed = false;
            return;
        }

        var pressed = false;
        switch (PauseKeySettings.PauseBinding)
        {
            case 1:
                pressed = __instance.GetTriggerValue(UnityEngine.XR.XRNode.LeftHand) >= 0.75f;
                break;
            case 2:
                pressed = __instance.GetTriggerValue(UnityEngine.XR.XRNode.RightHand) >= 0.75f;
                break;
            case 3:
                pressed = UnityEngine.Vector2.SqrMagnitude(__instance.GetThumbstickValue(UnityEngine.XR.XRNode.LeftHand)) >= 0.7225f;
                break;
            case 4:
                pressed = UnityEngine.Vector2.SqrMagnitude(__instance.GetThumbstickValue(UnityEngine.XR.XRNode.RightHand)) >= 0.7225f;
                break;
        }
        var pressedDown = false;
        if (pressed && !WasPressed)
            pressedDown = true;
        __result = pressedDown;
        WasPressed = pressed;
    }
}
