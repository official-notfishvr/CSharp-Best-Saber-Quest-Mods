using System.IO;
using CoreMod;
using GlobalNamespace;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

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
        DestroyPlatform(ref _activeMenuPlatform);
    }

    [Hook(typeof(MultiplayerLevelScenesTransitionSetupDataSO), nameof(MultiplayerLevelScenesTransitionSetupDataSO.InitAndSetupScenes), Phase = HookPhase.Postfix)]
    public static void OnMultiplayerLevelInit(MultiplayerLevelScenesTransitionSetupDataSO self)
    {
        _isMultiplayerScenePending = true;
        DestroyPlatform(ref _activeMenuPlatform);
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
            bundle.Unload(true);
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
        {
            DestroyPlatform(ref activePlatform);
            return null;
        }

        EnablePlatformBehaviours(activePlatform);

        return component;
    }

    private static void DestroyPlatform(ref GameObject? platform)
    {
        if (platform == null)
            return;

        DisablePlatformBehaviours(platform);
        UnityEngine.Object.Destroy(platform);
        platform = null;
    }

    private static void EnablePlatformBehaviours(GameObject platform)
    {
        EnableTrackRings(platform);
        EnableSpectrograms(platform);
        EnablePrefabLightmaps(platform);
    }

    private static void DisablePlatformBehaviours(GameObject platform)
    {
        DisableTrackRings(platform);
        DisableSpectrograms(platform);
    }

    private static void EnableTrackRings(GameObject platform)
    {
        var rings = platform.GetComponentsInChildren<TrackRings>(true);
        if (rings == null)
            return;

        for (var i = 0; i < rings.Length; i++)
            ((TrackRings)rings[i]).PlatformEnabled();
    }

    private static void DisableTrackRings(GameObject platform)
    {
        var rings = platform.GetComponentsInChildren<TrackRings>(true);
        if (rings == null)
            return;

        for (var i = 0; i < rings.Length; i++)
            ((TrackRings)rings[i]).PlatformDisabled();
    }

    private static void EnableSpectrograms(GameObject platform)
    {
        var spectrograms = platform.GetComponentsInChildren<Spectrogram>(true);
        if (spectrograms == null)
            return;

        for (var i = 0; i < spectrograms.Length; i++)
            ((Spectrogram)spectrograms[i]).PlatformEnabled();
    }

    private static void DisableSpectrograms(GameObject platform)
    {
        var spectrograms = platform.GetComponentsInChildren<Spectrogram>(true);
        if (spectrograms == null)
            return;

        for (var i = 0; i < spectrograms.Length; i++)
            ((Spectrogram)spectrograms[i]).PlatformDisabled();
    }

    private static void EnablePrefabLightmaps(GameObject platform)
    {
        var lightmaps = platform.GetComponentsInChildren<PrefabLightmapData>(true);
        if (lightmaps == null)
            return;

        for (var i = 0; i < lightmaps.Length; i++)
            ((PrefabLightmapData)lightmaps[i]).PlatformEnabled();
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

    public void Awake()
    {
        var transform = GetComponent<Transform>();
        if (transform != null && transform.get_gameObject() != null)
            transform.get_gameObject().SetActive(false);
    }
}

[CustomType]
public class CameraVisibility : MonoBehaviour
{
    public int visibilityMode;
    public bool affectChildren;

    public void Awake()
    {
        var transform = GetComponent<Transform>();
        if (transform == null || transform.get_gameObject() == null)
            return;

        if (visibilityMode == 1)
            SetLayer(transform.get_gameObject(), 4);
        else if (visibilityMode == 2)
            SetLayer(transform.get_gameObject(), 3);
    }

    private void SetLayer(GameObject target, int layer)
    {
        var targetTransform = target.GetComponent<Transform>();
        if (targetTransform == null)
            return;

        var transform = target.GetComponent<Transform>();
        if (transform == null || !affectChildren)
            return;

        var i = 0;
        while (i < transform.GetChildCount())
        {
            var child = transform.GetChild(i);
            if (child != null && child.get_gameObject() != null)
                SetLayer(child.get_gameObject(), layer);
            i = i + 1;
        }
    }
}

[CustomType]
public class TrackRings : MonoBehaviour
{
    public GameObject? trackLaneRingPrefab;
    public int ringCount = 10;
    public float ringPositionStep = 2f;
    public bool useRotationEffect;
    public int rotationSongEventType = 9;
    public float rotationStep = 5f;
    public int rotationPropagationSpeed = 1;
    public float rotationFlexySpeed = 1f;
    public float startupRotationAngle;
    public float startupRotationStep = 10f;
    public int startupRotationPropagationSpeed = 10;
    public float startupRotationFlexySpeed = 0.5f;
    public bool useStepEffect;
    public int stepSongEventType = 10;
    public float minPositionStep = 1f;
    public float maxPositionStep = 2f;
    public float moveSpeed = 1f;
    private TrackLaneRingsManager? _manager;

    public void PlatformEnabled()
    {
        if (trackLaneRingPrefab == null || _manager != null)
            return;

        var prefabRing = trackLaneRingPrefab.GetComponent<TrackLaneRing>();
        if (prefabRing == null)
            prefabRing = trackLaneRingPrefab.AddComponent<TrackLaneRing>();

        var transform = GetComponent<Transform>();
        if (transform == null || transform.get_gameObject() == null)
            return;

        _manager = transform.get_gameObject().AddComponent<TrackLaneRingsManager>();
        _manager._trackLaneRingPrefab = prefabRing;
        _manager._ringCount = ringCount;
        _manager._ringPositionStep = ringPositionStep;
        _manager._spawnAsChildren = true;
    }

    public void PlatformDisabled() { }
}

[CustomType]
public class TrackMirror : MonoBehaviour { }

[CustomType]
public class ColorMaterial : MonoBehaviour
{
    public string propertyName = "_Color";
    public int materialColorType;
}

[CustomType]
public class TubeLight : MonoBehaviour
{
    public float width = 0.5f;
    public float length = 1f;
    public float center = 0.5f;
    public Color color;
    public float colorAlphaMultiplier = 1f;
    public float bloomFogIntensityMultiplier = 1f;
    public float boostToWhite;
    public int lightsID;
}

[CustomType]
public class SongEventHandler : MonoBehaviour
{
    public int eventType;
    public int value;
    public bool anyValue;
    public UnityEvent? OnTrigger;
}

[CustomType]
public class ComboReachedEvent : MonoBehaviour
{
    public int ComboTarget = 50;
    public UnityEvent? NthComboReached;
}

[CustomType]
public class EveryNthComboFilter : MonoBehaviour
{
    public int ComboStep = 50;
    public UnityEvent? NthComboReached;
}

[CustomType]
public class SaberSliceFilter : MonoBehaviour
{
    public int saberType;
    public UnityEvent? SaberSlice;
}

[CustomType]
public class TextEventFilter : MonoBehaviour
{
    public int counterType;
    public TextMeshPro? textMeshPro;
}

[CustomType]
public class EventManager : MonoBehaviour
{
    public UnityEvent? OnSlice;
    public UnityEvent? OnMiss;
    public UnityEvent? OnComboBreak;
    public UnityEvent? MultiplierUp;
    public UnityEvent? SaberStartColliding;
    public UnityEvent? SaberStopColliding;
    public UnityEvent? OnLevelStart;
    public UnityEvent? OnLevelFail;
    public UnityEvent? OnLevelFinish;
    public UnityEvent? OnBlueLightOn;
    public UnityEvent? OnRedLightOn;
}

[CustomType]
public class RotationEventEffect : MonoBehaviour
{
    public int eventType;
    public Vector3 rotationVector;
}

[CustomType]
public class PairRotationEventEffect : MonoBehaviour
{
    public int eventL;
    public int eventR;
    public int switchOverrideRandomValuesEvent;
    public Transform? transformL;
    public Transform? transformR;
    public Vector3 rotationVector;
    public bool useZPositionForAngleOffset;
    public float zPositionAngleOffsetScale = 1f;
}

[CustomType]
public class Spectrogram : MonoBehaviour
{
    public GameObject? columnPrefab;
    public Vector3 separator;
    public float minHeight = 1f;
    public float maxHeight = 10f;
    public float columnWidth = 1f;
    public float columnDepth = 1f;

    public void PlatformEnabled()
    {
        UpdateColumnHeights();
    }

    public void PlatformDisabled()
    {
        UpdateColumnHeights();
    }

    private void UpdateColumnHeights()
    {
        var transform = GetComponent<Transform>();
        if (transform == null)
            return;

        var i = 0;
        while (i < transform.GetChildCount())
        {
            var child = transform.GetChild(i);
            i = i + 1;
        }
    }
}

[CustomType]
public class SpectrogramAnimationState : MonoBehaviour
{
    public AnimationClip? animationClip;
    public int sample;
    public bool averageAllSamples;
}

[CustomType]
public class SpectrogramMaterial : MonoBehaviour
{
    public string? PropertyName;
    public string? AveragePropertyName;
}

[CustomType]
public class PrefabLightmapData : MonoBehaviour
{
    public Renderer[]? renderInfoRenderer;
    public int[]? renderInfoLightmapIndex;
    public Vector4[]? renderInfoLightmapOffsetScale;
    public Texture2D[]? lightmaps;
    public Texture2D[]? lightmapsDir;
    public Texture2D[]? shadowMasks;
    public Light[]? lightInfoLight;
    public int[]? lightInfoLightmapBakeType;
    public int[]? lightInfoMixedLightingMode;

    public void PlatformEnabled()
    {
        if (renderInfoRenderer == null || renderInfoLightmapIndex == null || renderInfoLightmapOffsetScale == null)
            return;

        var i = 0;
        while (i < renderInfoRenderer.Length)
        {
            var renderer = renderInfoRenderer[i];
            i = i + 1;
        }
    }
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class SerializeField : Attribute
{
}
