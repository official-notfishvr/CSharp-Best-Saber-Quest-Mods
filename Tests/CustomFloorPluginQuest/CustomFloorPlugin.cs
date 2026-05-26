using System.IO;
using CoreMod;
using GlobalNamespace;
using UnityEngine;

namespace CustomFloorPlugin;

[Mod("com.csharp.quest.customplatforms", "0.1.0")]
public static class QuestCustomFloorMod
{
    private static GameObject? _activeMenuPlatform;
    private static GameObject? _activeGameplayPlatform;
    private static bool _isMultiplayerScenePending;

    [Config(Description = "Directory used when platform paths are not absolute.")]
    public static string PlatformDirectory { get; set; } = "/sdcard/ModData/com.beatgames.beatsaber/Mods/CustomPlatforms";

    [Config(Description = "Platform bundle to load in the menu scene.")]
    public static string MenuPlatformPath { get; set; } = "TechWay 30.plat";

    [Config(Description = "Platform bundle to load in standard gameplay scenes.")]
    public static string GameplayPlatformPath { get; set; } = "TechWay 30.plat";

    [Config(Description = "Platform bundle to load in multiplayer gameplay scenes. Falls back to GameplayPlatformPath when empty.")]
    public static string MultiplayerPlatformPath { get; set; } = "";

    [Hook(typeof(MenuEnvironmentManager), nameof(MenuEnvironmentManager.Start), Phase = HookPhase.Postfix)]
    public static void OnMenuEnvironmentStart(MenuEnvironmentManager self)
    {
        ApplyMenuPlatform(self);
    }

    [Hook(typeof(MenuEnvironmentManager), nameof(MenuEnvironmentManager.ShowEnvironmentType), Phase = HookPhase.Postfix)]
    public static void OnMenuEnvironmentChanged(MenuEnvironmentManager self, MenuEnvironmentManager.MenuEnvironmentType menuEnvironmentType)
    {
        ApplyMenuPlatform(self);
    }

    [Hook(typeof(GameCoreSceneSetup), nameof(GameCoreSceneSetup.Start), Phase = HookPhase.Postfix)]
    public static void OnGameCoreSceneSetupStart(GameCoreSceneSetup self)
    {
        ApplyGameplayPlatform();
    }

    [Hook(typeof(StandardLevelScenesTransitionSetupDataSO), nameof(StandardLevelScenesTransitionSetupDataSO.InitAndSetupScenes), Phase = HookPhase.Postfix)]
    public static void OnStandardLevelInit(StandardLevelScenesTransitionSetupDataSO self, PlayerSpecificSettings playerSpecificSettings, string backButtonText, bool startPaused)
    {
        _isMultiplayerScenePending = false;
    }

    [Hook(typeof(MultiplayerLevelScenesTransitionSetupDataSO), nameof(MultiplayerLevelScenesTransitionSetupDataSO.InitAndSetupScenes), Phase = HookPhase.Postfix)]
    public static void OnMultiplayerLevelInit(MultiplayerLevelScenesTransitionSetupDataSO self)
    {
        _isMultiplayerScenePending = true;
    }

    [Hook(typeof(MenuTransitionsHelper), nameof(MenuTransitionsHelper.StopStandardLevel), Phase = HookPhase.Postfix)]
    public static void OnStopStandardLevel(MenuTransitionsHelper self)
    {
        _isMultiplayerScenePending = false;
        DestroyPlatform(ref _activeGameplayPlatform);
    }

    private static void ApplyMenuPlatform(MenuEnvironmentManager manager)
    {
        var managerTransform = manager.GetComponent<Transform>();
        if (managerTransform == null)
            return;

        var platform = SpawnPlatform(MenuPlatformPath, managerTransform, ref _activeMenuPlatform);
        if (platform == null)
        {
            ApplyMenuVisibility(manager, false, false);
            return;
        }

        ApplyMenuVisibility(manager, true, platform.hideDefaultPlatform);
    }

    private static void ApplyGameplayPlatform()
    {
        var root = GetGameplayEnvironmentRoot();
        if (root == null)
            return;

        var configuredPath = ChooseGameplayPlatformPath();
        var platform = SpawnPlatform(configuredPath, root, ref _activeGameplayPlatform);
        if (platform == null)
        {
            ApplyGameplayVisibility(root, null, false);
            return;
        }

        ApplyGameplayVisibility(root, platform, true);
    }

    private static string ChooseGameplayPlatformPath()
    {
        if (_isMultiplayerScenePending)
        {
            if (!string.IsNullOrEmpty(MultiplayerPlatformPath))
                return MultiplayerPlatformPath;
        }

        return GameplayPlatformPath;
    }

    private static Transform? GetGameplayEnvironmentRoot()
    {
        var objects = Resources.FindObjectsOfTypeAll<LightWithIdManager>();
        if (objects == null || objects.Length == 0)
            return null;

        var lightManager = (LightWithIdManager)objects[0];
        var lightManagerTransform = lightManager.GetComponent<Transform>();
        if (lightManagerTransform == null)
            return null;

        return lightManagerTransform.GetParent();
    }

    private static CustomPlatform? SpawnPlatform(string configuredPath, Transform parent, ref GameObject? activePlatform)
    {
        DestroyPlatform(ref activePlatform);

        var fullPath = ResolveConfiguredPath(configuredPath);
        if (string.IsNullOrEmpty(fullPath))
            return null;

        if (!File.Exists(fullPath))
            return null;

        var bundle = AssetBundle.LoadFromFile(fullPath);
        if (bundle == null)
            return null;

        var asset = bundle.LoadAsset("_CustomPlatform", typeof(GameObject));
        if (asset == null)
        {
            bundle.Unload(false);
            return null;
        }

        var prefab = (GameObject)asset;
        activePlatform = (GameObject)UnityEngine.Object.Instantiate(prefab, parent);
        bundle.Unload(false);
        if (activePlatform == null)
            return null;

        activePlatform.SetActive(true);

        var component = activePlatform.GetComponent<CustomPlatform>();
        if (component == null)
            return null;

        return component;
    }

    private static void DestroyPlatform(ref GameObject? platform)
    {
        if (platform == null)
            return;

        UnityEngine.Object.Destroy(platform);
        platform = null;
    }

    private static string ResolveConfiguredPath(string configuredPath)
    {
        if (string.IsNullOrEmpty(configuredPath))
            return "";

        if (File.Exists(configuredPath))
            return configuredPath;

        if (string.IsNullOrEmpty(PlatformDirectory))
            return configuredPath;

        var combinedPath = Path.Combine(PlatformDirectory, configuredPath);
        if (File.Exists(combinedPath))
            return combinedPath;

        return configuredPath;
    }

    private static void ApplyMenuVisibility(MenuEnvironmentManager manager, bool hasCustomPlatform, bool hideDefaultPlatform)
    {
        var environments = manager._data;
        if (environments == null)
            return;

        var i = 0;
        while (i < environments.Length)
        {
            var wrapper = environments[i]._wrapper;
            if (wrapper != null)
            {
                var root = wrapper.GetComponent<Transform>();
                if (root != null)
                {
                    SetPathActive(root, "MenuFogRing", !hasCustomPlatform);
                    SetPathActive(root, "Notes", !hasCustomPlatform);
                    SetPathActive(root, "PileOfNotes", !hasCustomPlatform);

                    var showGround = !hasCustomPlatform || !hideDefaultPlatform;
                    SetPathActive(root, "BasicMenuGround", showGround);
                }
            }

            i = i + 1;
        }
    }

    private static void ApplyGameplayVisibility(Transform root, CustomPlatform? platform, bool hasCustomPlatform)
    {
        if (!hasCustomPlatform || platform == null)
        {
            SetPlayersPlaceActive(root, true);
            SetNamedObjectsActive(root, SmallRingNames, true);
            SetNamedObjectsActive(root, BigRingNames, true);
            SetNamedObjectsActive(root, VisualizerNames, true);
            SetNamedObjectsActive(root, TowerNames, true);
            SetNamedObjectsActive(root, HighwayNames, true);
            SetNamedObjectsActive(root, BackColumnNames, true);
            SetNamedObjectsActive(root, BackLaserNames, true);
            SetNamedObjectsActive(root, DoubleColorLaserNames, true);
            SetNamedObjectsActive(root, RotatingLaserNames, true);
            SetNamedObjectsActive(root, TrackLightNames, true);
            return;
        }

        SetPlayersPlaceActive(root, !platform.hideDefaultPlatform);
        SetNamedObjectsActive(root, SmallRingNames, !platform.hideSmallRings);
        SetNamedObjectsActive(root, BigRingNames, !platform.hideBigRings);
        SetNamedObjectsActive(root, VisualizerNames, !platform.hideEQVisualizer);
        SetNamedObjectsActive(root, TowerNames, !platform.hideTowers);
        SetNamedObjectsActive(root, HighwayNames, !platform.hideHighway);
        SetNamedObjectsActive(root, BackColumnNames, !platform.hideBackColumns);
        SetNamedObjectsActive(root, BackLaserNames, !platform.hideBackLasers);
        SetNamedObjectsActive(root, DoubleColorLaserNames, !platform.hideDoubleColorLasers);
        SetNamedObjectsActive(root, RotatingLaserNames, !platform.hideRotatingLasers);
        SetNamedObjectsActive(root, TrackLightNames, !platform.hideTrackLights);
    }

    private static void SetPlayersPlaceActive(Transform root, bool active)
    {
        SetPathActive(root, "PlayersPlace", active);
        SetPathActive(root, "IsActiveObjects/Construction/PlayersPlace", active);
    }

    private static void SetPathActive(Transform root, string path, bool active)
    {
        var child = root.Find(path);
        if (child != null && child.get_gameObject() != null)
            child.get_gameObject().SetActive(active);
    }

    private static void SetNamedObjectsActive(Transform root, string[] names, bool active)
    {
        for (var i = 0; i < names.Length; i++)
        {
            var child = TransformExtensions.FindChildRecursively(root, names[i]);
            if (child != null && child.get_gameObject() != null)
                child.get_gameObject().SetActive(active);
        }
    }

    private static readonly string[] SmallRingNames =
    [
        "SmallTrackLaneRings",
        "SmallTrackLaneRingsGroup",
        "TriangleTrackLaneRings",
        "PanelsTrackLaneRings",
        "Panels4TrackLaneRings",
        "LightLinesTrackLaneRings",
        "PairLaserTrackLaneRings",
        "PanelsLightsTrackLaneRings",
        "TrackLaneRings1",
    ];

    private static readonly string[] BigRingNames =
    [
        "BigTrackLaneRings",
        "BigTrackLaneRingsGroup",
        "BigLightsTrackLaneRings",
        "BigCenterLightsTrackLaneRings",
        "DistantRings",
        "TrackLaneRings2",
    ];

    private static readonly string[] VisualizerNames =
    [
        "Spectrograms",
        "SpectrogramsTheSecond",
    ];

    private static readonly string[] TowerNames =
    [
        "Buildings",
        "NearBuildingLeft",
        "NearBuildingRight",
        "FarBuildings",
        "HallConstruction",
        "TopCones",
        "BottomCones",
    ];

    private static readonly string[] HighwayNames =
    [
        "TrackMirror",
        "TrackConstruction",
        "Construction",
        "Floor",
        "FloorConstruction",
        "VConstruction",
        "Underground",
        "Cube",
        "TrackBL",
        "TrackBR",
        "TrackTR",
        "TrackTL",
    ];

    private static readonly string[] BackColumnNames =
    [
        "PillarPair",
        "SmallPillarPair",
        "RearPillar",
        "PillarTrackLaneRingsR",
    ];

    private static readonly string[] BackLaserNames =
    [
        "FrontLights",
        "FrontLasers",
        "Logo",
        "LogoLight",
        "Moon",
        "GateLight",
        "Window",
        "FrontLogo",
    ];

    private static readonly string[] DoubleColorLaserNames =
    [
        "DoubleColorLaser",
        "DoubleColorLaserL",
        "DoubleColorLaserR",
        "Laser",
        "BottomPairLasers",
        "Main Lasers Top",
        "Main Lasers Bottom",
    ];

    private static readonly string[] RotatingLaserNames =
    [
        "RotatingLasersPair",
        "TunnelRotatingLasersPair",
        "SpotlightGroupLeft",
        "SpotlightGroupRight",
    ];

    private static readonly string[] TrackLightNames =
    [
        "GlowLines",
        "GlowLineL",
        "GlowLineR",
        "GlowLineFarL",
        "GlowLineFarR",
        "GlowLineH",
        "GlowLineC",
        "NeonTubeL",
        "NeonTubeR",
        "NeonTube",
        "NeonTubeDirectionalL",
        "NeonTubeDirectionalR",
        "LeftLaser",
        "RightLaser",
        "RunwayLasers",
        "Aurora",
        "TopLaser",
    ];
}

[CustomType]
public class CustomPlatform : MonoBehaviour
{
    public string platName = "MyCustomPlatform";
    public string platAuthor = "MyName";
    public Sprite? icon;
    public bool hideHighway;
    public bool hideTowers;
    public bool hideDefaultPlatform;
    public bool hideEQVisualizer;
    public bool hideSmallRings;
    public bool hideBigRings;
    public bool hideBackColumns;
    public bool hideBackLasers;
    public bool hideDoubleColorLasers;
    public bool hideRotatingLasers;
    public bool hideTrackLights;

    [SerializeField]
    internal string platHash = "";

    [SerializeField]
    internal string fullPath = "";

    [SerializeField]
    internal bool isDescriptor = true;
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class SerializeField : Attribute
{
}
