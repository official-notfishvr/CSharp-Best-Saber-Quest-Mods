using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using UnityEngine;

[assembly: AssemblyTitle("SampleMod")]
[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace SampleMod;

[Plugin(RuntimeOptions.SingleStartInit)]
public sealed class Plugin
{
    private Harmony? _harmony;

    [Init]
    public void Init(Config config)
    {
        PatchConfiguration.Enabled = config.Generated<PluginConfig>().Enabled;
        _harmony = new Harmony("com.example.samplemod");
    }

    [OnStart]
    public void OnStart()
    {
        _harmony?.PatchAll(typeof(Plugin).Assembly);
    }

    [OnExit]
    public void OnExit()
    {
        _harmony?.UnpatchSelf();
        _harmony = null;
    }
}

internal class PluginConfig
{
    public virtual bool Enabled { get; set; } = true;
}

internal static class PatchConfiguration
{
    public static bool Enabled = true;
}

[HarmonyPatch(typeof(StandardLevelDetailViewController), nameof(StandardLevelDetailViewController.DidActivate))]
internal static class LevelDetailActivatedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(StandardLevelDetailViewController __instance)
    {
        if (!PatchConfiguration.Enabled)
            return true;

        var detailView = __instance._standardLevelDetailView;
        if (detailView == null)
            return true;

        if (detailView._buttonsWrapper != null)
            detailView._buttonsWrapper.SetActive(true);

        if (detailView._actionButtonText != null)
            detailView._actionButtonText.text = "Skill Issue [pre]";

        if (detailView._beatmapLevelVersionText != null)
            detailView._beatmapLevelVersionText.text = "Mod Active";

        if (detailView._actionButton != null)
            detailView._actionButton.interactable = true;
        return true;
    }

    [HarmonyPostfix]
    private static void Postfix(StandardLevelDetailViewController __instance)
    {
        if (!PatchConfiguration.Enabled)
            return;

        var detailView = __instance._standardLevelDetailView;
        if (detailView == null)
            return;

        if (detailView._actionButtonText != null)
            detailView._actionButtonText.text = "Skill Issue [post]";

        if (detailView._practiceButton != null)
            detailView._practiceButton.interactable = true;
    }
}

[HarmonyPatch(typeof(MainMenuViewController), "DidActivate")]
internal static class MainMenuActivatedPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        Debug.Log("[SampleMod] Main menu activated.");
        Debug.Log(Time.time + 1.0f);
        if (Time.time > 10.0f)
            Debug.Log("[SampleMod] Time comparison branch translated.");
        var iterations = 0;
        while (iterations < 2)
        {
            if (iterations == 1)
                break;
            iterations++;
        }
        switch (iterations)
        {
            case 1:
                Debug.Log("[SampleMod] Switch case one.");
                break;
            case 2:
                Debug.Log("[SampleMod] Loop and switch translated.");
                break;
            case 3:
                Debug.Log("[SampleMod] Switch case three.");
                break;
        }
        Debug.LogWarning("[SampleMod] Quest warning log mapping example.");
        Debug.LogError("[SampleMod] Quest error log mapping example.");
    }
}
