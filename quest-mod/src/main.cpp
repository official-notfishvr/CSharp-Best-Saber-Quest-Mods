#include "main.hpp"
#include "scotland2/shared/modloader.h"
#include "custom-types/shared/register.hpp"
#include "custom-types/shared/macros.hpp"

#include "GlobalNamespace/GameCoreSceneSetup.hpp"
#include "GlobalNamespace/LightWithIdManager.hpp"
#include "GlobalNamespace/MenuEnvironmentManager.hpp"
#include "GlobalNamespace/MenuTransitionsHelper.hpp"
#include "GlobalNamespace/MultiplayerLevelScenesTransitionSetupDataSO.hpp"
#include "GlobalNamespace/PlayerSpecificSettings.hpp"
#include "GlobalNamespace/StandardLevelScenesTransitionSetupDataSO.hpp"
#include "GlobalNamespace/TransformExtensions.hpp"
#include "System/IO/File.hpp"
#include "System/IO/Path.hpp"
#include "System/RuntimeTypeHandle.hpp"
#include "System/String.hpp"
#include "System/Type.hpp"
#include "UnityEngine/AssetBundle.hpp"
#include "UnityEngine/Component.hpp"
#include "UnityEngine/GameObject.hpp"
#include "UnityEngine/MonoBehaviour.hpp"
#include "UnityEngine/Object.hpp"
#include "UnityEngine/Resources.hpp"
#include "UnityEngine/Sprite.hpp"
#include "UnityEngine/Transform.hpp"

static modloader::ModInfo modInfo{"com.csharp.quest.customplatforms", "0.1.0", 0};

static UnityEngine::GameObject* CustomFloorPlugin_QuestCustomFloorMod__activeMenuPlatform = nullptr;
static UnityEngine::GameObject* CustomFloorPlugin_QuestCustomFloorMod__activeGameplayPlatform = nullptr;
static bool CustomFloorPlugin_QuestCustomFloorMod__isMultiplayerScenePending = false;
static ArrayW<::StringW> CustomFloorPlugin_QuestCustomFloorMod_SmallRingNames = nullptr;
static ArrayW<::StringW> CustomFloorPlugin_QuestCustomFloorMod_BigRingNames = nullptr;
static ArrayW<::StringW> CustomFloorPlugin_QuestCustomFloorMod_VisualizerNames = nullptr;
static ArrayW<::StringW> CustomFloorPlugin_QuestCustomFloorMod_TowerNames = nullptr;
static ArrayW<::StringW> CustomFloorPlugin_QuestCustomFloorMod_HighwayNames = nullptr;
static ArrayW<::StringW> CustomFloorPlugin_QuestCustomFloorMod_BackColumnNames = nullptr;
static ArrayW<::StringW> CustomFloorPlugin_QuestCustomFloorMod_BackLaserNames = nullptr;
static ArrayW<::StringW> CustomFloorPlugin_QuestCustomFloorMod_DoubleColorLaserNames = nullptr;
static ArrayW<::StringW> CustomFloorPlugin_QuestCustomFloorMod_RotatingLaserNames = nullptr;
static ArrayW<::StringW> CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames = nullptr;

DECLARE_CLASS_CODEGEN_DLL(CustomFloorPlugin, CustomPlatform, UnityEngine::MonoBehaviour, "CustomFloorPlugin") {
    DECLARE_CTOR(__ctor);
    DECLARE_INSTANCE_FIELD(::StringW, platName);
    DECLARE_INSTANCE_FIELD(::StringW, platAuthor);
    DECLARE_INSTANCE_FIELD(UnityEngine::Sprite*, icon);
    DECLARE_INSTANCE_FIELD(bool, hideHighway);
    DECLARE_INSTANCE_FIELD(bool, hideTowers);
    DECLARE_INSTANCE_FIELD(bool, hideDefaultPlatform);
    DECLARE_INSTANCE_FIELD(bool, hideEQVisualizer);
    DECLARE_INSTANCE_FIELD(bool, hideSmallRings);
    DECLARE_INSTANCE_FIELD(bool, hideBigRings);
    DECLARE_INSTANCE_FIELD(bool, hideBackColumns);
    DECLARE_INSTANCE_FIELD(bool, hideBackLasers);
    DECLARE_INSTANCE_FIELD(bool, hideDoubleColorLasers);
    DECLARE_INSTANCE_FIELD(bool, hideRotatingLasers);
    DECLARE_INSTANCE_FIELD(bool, hideTrackLights);
    DECLARE_INSTANCE_FIELD(::StringW, platHash);
    DECLARE_INSTANCE_FIELD(::StringW, fullPath);
    DECLARE_INSTANCE_FIELD(bool, isDescriptor);
};

DEFINE_TYPE(CustomFloorPlugin, CustomPlatform);

void CustomFloorPlugin::CustomPlatform::__ctor() {
    INVOKE_CTOR();
    INVOKE_BASE_CTOR(CustomFloorPlugin::CustomPlatform::___TypeRegistration::get()->baseType());
    platName = il2cpp_utils::newcsstr("MyCustomPlatform");
    platAuthor = il2cpp_utils::newcsstr("MyName");
    platHash = il2cpp_utils::newcsstr("");
    fullPath = il2cpp_utils::newcsstr("");
    isDescriptor = true;
}

Configuration &getConfig() {
    static Configuration config(modInfo);
    return config;
}

static void InitializeLocalStaticFields() {
    CustomFloorPlugin_QuestCustomFloorMod_SmallRingNames = ArrayW<::StringW>(9);
    CustomFloorPlugin_QuestCustomFloorMod_SmallRingNames[0] = il2cpp_utils::newcsstr("SmallTrackLaneRings");
    CustomFloorPlugin_QuestCustomFloorMod_SmallRingNames[1] = il2cpp_utils::newcsstr("SmallTrackLaneRingsGroup");
    CustomFloorPlugin_QuestCustomFloorMod_SmallRingNames[2] = il2cpp_utils::newcsstr("TriangleTrackLaneRings");
    CustomFloorPlugin_QuestCustomFloorMod_SmallRingNames[3] = il2cpp_utils::newcsstr("PanelsTrackLaneRings");
    CustomFloorPlugin_QuestCustomFloorMod_SmallRingNames[4] = il2cpp_utils::newcsstr("Panels4TrackLaneRings");
    CustomFloorPlugin_QuestCustomFloorMod_SmallRingNames[5] = il2cpp_utils::newcsstr("LightLinesTrackLaneRings");
    CustomFloorPlugin_QuestCustomFloorMod_SmallRingNames[6] = il2cpp_utils::newcsstr("PairLaserTrackLaneRings");
    CustomFloorPlugin_QuestCustomFloorMod_SmallRingNames[7] = il2cpp_utils::newcsstr("PanelsLightsTrackLaneRings");
    CustomFloorPlugin_QuestCustomFloorMod_SmallRingNames[8] = il2cpp_utils::newcsstr("TrackLaneRings1");
    CustomFloorPlugin_QuestCustomFloorMod_BigRingNames = ArrayW<::StringW>(6);
    CustomFloorPlugin_QuestCustomFloorMod_BigRingNames[0] = il2cpp_utils::newcsstr("BigTrackLaneRings");
    CustomFloorPlugin_QuestCustomFloorMod_BigRingNames[1] = il2cpp_utils::newcsstr("BigTrackLaneRingsGroup");
    CustomFloorPlugin_QuestCustomFloorMod_BigRingNames[2] = il2cpp_utils::newcsstr("BigLightsTrackLaneRings");
    CustomFloorPlugin_QuestCustomFloorMod_BigRingNames[3] = il2cpp_utils::newcsstr("BigCenterLightsTrackLaneRings");
    CustomFloorPlugin_QuestCustomFloorMod_BigRingNames[4] = il2cpp_utils::newcsstr("DistantRings");
    CustomFloorPlugin_QuestCustomFloorMod_BigRingNames[5] = il2cpp_utils::newcsstr("TrackLaneRings2");
    CustomFloorPlugin_QuestCustomFloorMod_VisualizerNames = ArrayW<::StringW>(2);
    CustomFloorPlugin_QuestCustomFloorMod_VisualizerNames[0] = il2cpp_utils::newcsstr("Spectrograms");
    CustomFloorPlugin_QuestCustomFloorMod_VisualizerNames[1] = il2cpp_utils::newcsstr("SpectrogramsTheSecond");
    CustomFloorPlugin_QuestCustomFloorMod_TowerNames = ArrayW<::StringW>(7);
    CustomFloorPlugin_QuestCustomFloorMod_TowerNames[0] = il2cpp_utils::newcsstr("Buildings");
    CustomFloorPlugin_QuestCustomFloorMod_TowerNames[1] = il2cpp_utils::newcsstr("NearBuildingLeft");
    CustomFloorPlugin_QuestCustomFloorMod_TowerNames[2] = il2cpp_utils::newcsstr("NearBuildingRight");
    CustomFloorPlugin_QuestCustomFloorMod_TowerNames[3] = il2cpp_utils::newcsstr("FarBuildings");
    CustomFloorPlugin_QuestCustomFloorMod_TowerNames[4] = il2cpp_utils::newcsstr("HallConstruction");
    CustomFloorPlugin_QuestCustomFloorMod_TowerNames[5] = il2cpp_utils::newcsstr("TopCones");
    CustomFloorPlugin_QuestCustomFloorMod_TowerNames[6] = il2cpp_utils::newcsstr("BottomCones");
    CustomFloorPlugin_QuestCustomFloorMod_HighwayNames = ArrayW<::StringW>(12);
    CustomFloorPlugin_QuestCustomFloorMod_HighwayNames[0] = il2cpp_utils::newcsstr("TrackMirror");
    CustomFloorPlugin_QuestCustomFloorMod_HighwayNames[1] = il2cpp_utils::newcsstr("TrackConstruction");
    CustomFloorPlugin_QuestCustomFloorMod_HighwayNames[2] = il2cpp_utils::newcsstr("Construction");
    CustomFloorPlugin_QuestCustomFloorMod_HighwayNames[3] = il2cpp_utils::newcsstr("Floor");
    CustomFloorPlugin_QuestCustomFloorMod_HighwayNames[4] = il2cpp_utils::newcsstr("FloorConstruction");
    CustomFloorPlugin_QuestCustomFloorMod_HighwayNames[5] = il2cpp_utils::newcsstr("VConstruction");
    CustomFloorPlugin_QuestCustomFloorMod_HighwayNames[6] = il2cpp_utils::newcsstr("Underground");
    CustomFloorPlugin_QuestCustomFloorMod_HighwayNames[7] = il2cpp_utils::newcsstr("Cube");
    CustomFloorPlugin_QuestCustomFloorMod_HighwayNames[8] = il2cpp_utils::newcsstr("TrackBL");
    CustomFloorPlugin_QuestCustomFloorMod_HighwayNames[9] = il2cpp_utils::newcsstr("TrackBR");
    CustomFloorPlugin_QuestCustomFloorMod_HighwayNames[10] = il2cpp_utils::newcsstr("TrackTR");
    CustomFloorPlugin_QuestCustomFloorMod_HighwayNames[11] = il2cpp_utils::newcsstr("TrackTL");
    CustomFloorPlugin_QuestCustomFloorMod_BackColumnNames = ArrayW<::StringW>(4);
    CustomFloorPlugin_QuestCustomFloorMod_BackColumnNames[0] = il2cpp_utils::newcsstr("PillarPair");
    CustomFloorPlugin_QuestCustomFloorMod_BackColumnNames[1] = il2cpp_utils::newcsstr("SmallPillarPair");
    CustomFloorPlugin_QuestCustomFloorMod_BackColumnNames[2] = il2cpp_utils::newcsstr("RearPillar");
    CustomFloorPlugin_QuestCustomFloorMod_BackColumnNames[3] = il2cpp_utils::newcsstr("PillarTrackLaneRingsR");
    CustomFloorPlugin_QuestCustomFloorMod_BackLaserNames = ArrayW<::StringW>(8);
    CustomFloorPlugin_QuestCustomFloorMod_BackLaserNames[0] = il2cpp_utils::newcsstr("FrontLights");
    CustomFloorPlugin_QuestCustomFloorMod_BackLaserNames[1] = il2cpp_utils::newcsstr("FrontLasers");
    CustomFloorPlugin_QuestCustomFloorMod_BackLaserNames[2] = il2cpp_utils::newcsstr("Logo");
    CustomFloorPlugin_QuestCustomFloorMod_BackLaserNames[3] = il2cpp_utils::newcsstr("LogoLight");
    CustomFloorPlugin_QuestCustomFloorMod_BackLaserNames[4] = il2cpp_utils::newcsstr("Moon");
    CustomFloorPlugin_QuestCustomFloorMod_BackLaserNames[5] = il2cpp_utils::newcsstr("GateLight");
    CustomFloorPlugin_QuestCustomFloorMod_BackLaserNames[6] = il2cpp_utils::newcsstr("Window");
    CustomFloorPlugin_QuestCustomFloorMod_BackLaserNames[7] = il2cpp_utils::newcsstr("FrontLogo");
    CustomFloorPlugin_QuestCustomFloorMod_DoubleColorLaserNames = ArrayW<::StringW>(7);
    CustomFloorPlugin_QuestCustomFloorMod_DoubleColorLaserNames[0] = il2cpp_utils::newcsstr("DoubleColorLaser");
    CustomFloorPlugin_QuestCustomFloorMod_DoubleColorLaserNames[1] = il2cpp_utils::newcsstr("DoubleColorLaserL");
    CustomFloorPlugin_QuestCustomFloorMod_DoubleColorLaserNames[2] = il2cpp_utils::newcsstr("DoubleColorLaserR");
    CustomFloorPlugin_QuestCustomFloorMod_DoubleColorLaserNames[3] = il2cpp_utils::newcsstr("Laser");
    CustomFloorPlugin_QuestCustomFloorMod_DoubleColorLaserNames[4] = il2cpp_utils::newcsstr("BottomPairLasers");
    CustomFloorPlugin_QuestCustomFloorMod_DoubleColorLaserNames[5] = il2cpp_utils::newcsstr("Main Lasers Top");
    CustomFloorPlugin_QuestCustomFloorMod_DoubleColorLaserNames[6] = il2cpp_utils::newcsstr("Main Lasers Bottom");
    CustomFloorPlugin_QuestCustomFloorMod_RotatingLaserNames = ArrayW<::StringW>(4);
    CustomFloorPlugin_QuestCustomFloorMod_RotatingLaserNames[0] = il2cpp_utils::newcsstr("RotatingLasersPair");
    CustomFloorPlugin_QuestCustomFloorMod_RotatingLaserNames[1] = il2cpp_utils::newcsstr("TunnelRotatingLasersPair");
    CustomFloorPlugin_QuestCustomFloorMod_RotatingLaserNames[2] = il2cpp_utils::newcsstr("SpotlightGroupLeft");
    CustomFloorPlugin_QuestCustomFloorMod_RotatingLaserNames[3] = il2cpp_utils::newcsstr("SpotlightGroupRight");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames = ArrayW<::StringW>(17);
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[0] = il2cpp_utils::newcsstr("GlowLines");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[1] = il2cpp_utils::newcsstr("GlowLineL");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[2] = il2cpp_utils::newcsstr("GlowLineR");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[3] = il2cpp_utils::newcsstr("GlowLineFarL");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[4] = il2cpp_utils::newcsstr("GlowLineFarR");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[5] = il2cpp_utils::newcsstr("GlowLineH");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[6] = il2cpp_utils::newcsstr("GlowLineC");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[7] = il2cpp_utils::newcsstr("NeonTubeL");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[8] = il2cpp_utils::newcsstr("NeonTubeR");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[9] = il2cpp_utils::newcsstr("NeonTube");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[10] = il2cpp_utils::newcsstr("NeonTubeDirectionalL");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[11] = il2cpp_utils::newcsstr("NeonTubeDirectionalR");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[12] = il2cpp_utils::newcsstr("LeftLaser");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[13] = il2cpp_utils::newcsstr("RightLaser");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[14] = il2cpp_utils::newcsstr("RunwayLasers");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[15] = il2cpp_utils::newcsstr("Aurora");
    CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames[16] = il2cpp_utils::newcsstr("TopLaser");
}

static void CustomFloorPlugin_QuestCustomFloorMod_ApplyMenuPlatform_GlobalNamespace_MenuEnvironmentManager(GlobalNamespace::MenuEnvironmentManager* manager);
static void CustomFloorPlugin_QuestCustomFloorMod_ApplyGameplayPlatform();
static ::StringW CustomFloorPlugin_QuestCustomFloorMod_ChooseGameplayPlatformPath();
static UnityEngine::Transform* CustomFloorPlugin_QuestCustomFloorMod_GetGameplayEnvironmentRoot();
static CustomFloorPlugin::CustomPlatform* CustomFloorPlugin_QuestCustomFloorMod_SpawnPlatform_System_String_UnityEngine_Transform_UnityEngine_GameObject_(::StringW configuredPath, UnityEngine::Transform* parent, UnityEngine::GameObject*& activePlatform);
static void CustomFloorPlugin_QuestCustomFloorMod_DestroyPlatform_UnityEngine_GameObject_(UnityEngine::GameObject*& platform);
static ::StringW CustomFloorPlugin_QuestCustomFloorMod_ResolveConfiguredPath_System_String(::StringW configuredPath);
static void CustomFloorPlugin_QuestCustomFloorMod_ApplyMenuVisibility_GlobalNamespace_MenuEnvironmentManager_System_Boolean_System_Boolean(GlobalNamespace::MenuEnvironmentManager* manager, bool hasCustomPlatform, bool hideDefaultPlatform);
static void CustomFloorPlugin_QuestCustomFloorMod_ApplyGameplayVisibility_UnityEngine_Transform_CustomFloorPlugin_CustomPlatform_System_Boolean(UnityEngine::Transform* root, CustomFloorPlugin::CustomPlatform* platform, bool hasCustomPlatform);
static void CustomFloorPlugin_QuestCustomFloorMod_SetPlayersPlaceActive_UnityEngine_Transform_System_Boolean(UnityEngine::Transform* root, bool active);
static void CustomFloorPlugin_QuestCustomFloorMod_SetPathActive_UnityEngine_Transform_System_String_System_Boolean(UnityEngine::Transform* root, ::StringW path, bool active);
static void CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(UnityEngine::Transform* root, ArrayW<::StringW> names, bool active);

static void CustomFloorPlugin_QuestCustomFloorMod_ApplyMenuPlatform_GlobalNamespace_MenuEnvironmentManager(GlobalNamespace::MenuEnvironmentManager* manager) {
    UnityEngine::Transform* managerTransform{};
    CustomFloorPlugin::CustomPlatform* platform{};

    managerTransform = manager->GetComponent<UnityEngine::Transform*>();
    if (managerTransform == nullptr) {
        return;
    }
    platform = CustomFloorPlugin_QuestCustomFloorMod_SpawnPlatform_System_String_UnityEngine_Transform_UnityEngine_GameObject_(MenuPlatformPath, managerTransform, CustomFloorPlugin_QuestCustomFloorMod__activeMenuPlatform);
    if (platform == nullptr) {
        CustomFloorPlugin_QuestCustomFloorMod_ApplyMenuVisibility_GlobalNamespace_MenuEnvironmentManager_System_Boolean_System_Boolean(manager, 0, 0);
    }
    else {
        CustomFloorPlugin_QuestCustomFloorMod_ApplyMenuVisibility_GlobalNamespace_MenuEnvironmentManager_System_Boolean_System_Boolean(manager, 1, platform->hideDefaultPlatform);
    }
}

static void CustomFloorPlugin_QuestCustomFloorMod_ApplyGameplayPlatform() {
    UnityEngine::Transform* root{};
    ::StringW configuredPath{};
    CustomFloorPlugin::CustomPlatform* platform{};

    root = CustomFloorPlugin_QuestCustomFloorMod_GetGameplayEnvironmentRoot();
    if (root == nullptr) {
        return;
    }
    configuredPath = CustomFloorPlugin_QuestCustomFloorMod_ChooseGameplayPlatformPath();
    platform = CustomFloorPlugin_QuestCustomFloorMod_SpawnPlatform_System_String_UnityEngine_Transform_UnityEngine_GameObject_(configuredPath, root, CustomFloorPlugin_QuestCustomFloorMod__activeGameplayPlatform);
    if (platform == nullptr) {
        CustomFloorPlugin_QuestCustomFloorMod_ApplyGameplayVisibility_UnityEngine_Transform_CustomFloorPlugin_CustomPlatform_System_Boolean(root, nullptr, 0);
    }
    else {
        CustomFloorPlugin_QuestCustomFloorMod_ApplyGameplayVisibility_UnityEngine_Transform_CustomFloorPlugin_CustomPlatform_System_Boolean(root, platform, 1);
    }
}

static ::StringW CustomFloorPlugin_QuestCustomFloorMod_ChooseGameplayPlatformPath() {

    if (CustomFloorPlugin_QuestCustomFloorMod__isMultiplayerScenePending) {
        if (!(::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", MultiplayerPlatformPath))) {
            return MultiplayerPlatformPath;
        }
    }

    return GameplayPlatformPath;
}

static UnityEngine::Transform* CustomFloorPlugin_QuestCustomFloorMod_GetGameplayEnvironmentRoot() {
    GlobalNamespace::LightWithIdManager* lightManager{};
    UnityEngine::Transform* lightManagerTransform{};

    auto objects = UnityEngine::Resources::FindObjectsOfTypeAll<GlobalNamespace::LightWithIdManager*>();
    if ((!(objects)) || (((static_cast<int>(objects.size()) == 0)))) {
        return nullptr;
    }
    lightManager = reinterpret_cast<GlobalNamespace::LightWithIdManager*>(objects[0]);
    lightManagerTransform = lightManager->GetComponent<UnityEngine::Transform*>();
    if (lightManagerTransform == nullptr) {
        return nullptr;
    }
    return lightManagerTransform->GetParent();
}

static CustomFloorPlugin::CustomPlatform* CustomFloorPlugin_QuestCustomFloorMod_SpawnPlatform_System_String_UnityEngine_Transform_UnityEngine_GameObject_(::StringW configuredPath, UnityEngine::Transform* parent, UnityEngine::GameObject*& activePlatform) {
    ::StringW fullPath{};
    UnityEngine::AssetBundle* bundle{};
    UnityEngine::Object* asset{};
    UnityEngine::GameObject* prefab{};
    CustomFloorPlugin::CustomPlatform* component{};

    CustomFloorPlugin_QuestCustomFloorMod_DestroyPlatform_UnityEngine_GameObject_(activePlatform);
    fullPath = CustomFloorPlugin_QuestCustomFloorMod_ResolveConfiguredPath_System_String(configuredPath);
    if (::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", fullPath)) {
        return nullptr;
    }
    if (!(::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System.IO", "File"), "Exists", fullPath))) {
        return nullptr;
    }
    bundle = UnityEngine::AssetBundle::LoadFromFile(fullPath);
    if (bundle == nullptr) {
        return nullptr;
    }
    asset = bundle->LoadAsset<UnityEngine::GameObject*>(il2cpp_utils::newcsstr("_CustomPlatform"));
    bundle->Unload(0);
    if (asset == nullptr) {
        return nullptr;
    }
    prefab = reinterpret_cast<UnityEngine::GameObject*>(asset);
    activePlatform = UnityEngine::Object::Instantiate<UnityEngine::GameObject*>(prefab, parent);
    if (activePlatform == nullptr) {
        return nullptr;
    }
    activePlatform->SetActive(1);
    component = activePlatform->GetComponent<CustomFloorPlugin::CustomPlatform*>();
    if (component == nullptr) {
        return nullptr;
    }

    return component;
}

static void CustomFloorPlugin_QuestCustomFloorMod_DestroyPlatform_UnityEngine_GameObject_(UnityEngine::GameObject*& platform) {

    if (platform == nullptr) {
        return;
    }
    UnityEngine::Object::Destroy(platform);
    platform = nullptr;
}

static ::StringW CustomFloorPlugin_QuestCustomFloorMod_ResolveConfiguredPath_System_String(::StringW configuredPath) {
    ::StringW combinedPath{};

    if (::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", configuredPath)) {
        return il2cpp_utils::newcsstr("");
    }
    if (::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System.IO", "File"), "Exists", configuredPath)) {
        return configuredPath;
    }
    if (::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", PlatformDirectory)) {
        return configuredPath;
    }
    combinedPath = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System.IO", "Path"), "Combine", PlatformDirectory, configuredPath);
    if (::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System.IO", "File"), "Exists", combinedPath)) {
        return combinedPath;
    }

    return configuredPath;
}

static void CustomFloorPlugin_QuestCustomFloorMod_ApplyMenuVisibility_GlobalNamespace_MenuEnvironmentManager_System_Boolean_System_Boolean(GlobalNamespace::MenuEnvironmentManager* manager, bool hasCustomPlatform, bool hideDefaultPlatform) {
    ArrayW<GlobalNamespace::MenuEnvironmentManager_MenuEnvironmentObjects*> environments{};
    int32_t i{};

    UnityEngine::GameObject* wrapper{};

    UnityEngine::Transform* root{};

    bool showGround{};

    environments = manager->____data;

    i = 0;
    while (true) {
        if (!(((i < static_cast<int32_t>(static_cast<int>(environments.size())))))) {
            break;
        }
        wrapper = environments[i]->_wrapper;
        if (wrapper != nullptr) {
            root = wrapper->GetComponent<UnityEngine::Transform*>();
            if (root != nullptr) {
                CustomFloorPlugin_QuestCustomFloorMod_SetPathActive_UnityEngine_Transform_System_String_System_Boolean(root, il2cpp_utils::newcsstr("MenuFogRing"), !(hasCustomPlatform));
                CustomFloorPlugin_QuestCustomFloorMod_SetPathActive_UnityEngine_Transform_System_String_System_Boolean(root, il2cpp_utils::newcsstr("Notes"), !(hasCustomPlatform));
                CustomFloorPlugin_QuestCustomFloorMod_SetPathActive_UnityEngine_Transform_System_String_System_Boolean(root, il2cpp_utils::newcsstr("PileOfNotes"), !(hasCustomPlatform));
                showGround = (!(hasCustomPlatform)) || ((!(hideDefaultPlatform)));
                CustomFloorPlugin_QuestCustomFloorMod_SetPathActive_UnityEngine_Transform_System_String_System_Boolean(root, il2cpp_utils::newcsstr("BasicMenuGround"), showGround);
            }
        }
        i = (i + 1);
    }
}

static void CustomFloorPlugin_QuestCustomFloorMod_ApplyGameplayVisibility_UnityEngine_Transform_CustomFloorPlugin_CustomPlatform_System_Boolean(UnityEngine::Transform* root, CustomFloorPlugin::CustomPlatform* platform, bool hasCustomPlatform) {

    if ((!(hasCustomPlatform)) || ((platform == nullptr))) {
        CustomFloorPlugin_QuestCustomFloorMod_SetPlayersPlaceActive_UnityEngine_Transform_System_Boolean(root, 1);
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_SmallRingNames, 1);
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_BigRingNames, 1);
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_VisualizerNames, 1);
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_TowerNames, 1);
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_HighwayNames, 1);
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_BackColumnNames, 1);
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_BackLaserNames, 1);
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_DoubleColorLaserNames, 1);
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_RotatingLaserNames, 1);
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames, 1);
    }
    else {
        CustomFloorPlugin_QuestCustomFloorMod_SetPlayersPlaceActive_UnityEngine_Transform_System_Boolean(root, !(platform->hideDefaultPlatform));
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_SmallRingNames, !(platform->hideSmallRings));
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_BigRingNames, !(platform->hideBigRings));
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_VisualizerNames, !(platform->hideEQVisualizer));
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_TowerNames, !(platform->hideTowers));
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_HighwayNames, !(platform->hideHighway));
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_BackColumnNames, !(platform->hideBackColumns));
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_BackLaserNames, !(platform->hideBackLasers));
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_DoubleColorLaserNames, !(platform->hideDoubleColorLasers));
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_RotatingLaserNames, !(platform->hideRotatingLasers));
        CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(root, CustomFloorPlugin_QuestCustomFloorMod_TrackLightNames, !(platform->hideTrackLights));
    }
}

static void CustomFloorPlugin_QuestCustomFloorMod_SetPlayersPlaceActive_UnityEngine_Transform_System_Boolean(UnityEngine::Transform* root, bool active) {
    CustomFloorPlugin_QuestCustomFloorMod_SetPathActive_UnityEngine_Transform_System_String_System_Boolean(root, il2cpp_utils::newcsstr("PlayersPlace"), active);
    CustomFloorPlugin_QuestCustomFloorMod_SetPathActive_UnityEngine_Transform_System_String_System_Boolean(root, il2cpp_utils::newcsstr("IsActiveObjects/Construction/PlayersPlace"), active);
}

static void CustomFloorPlugin_QuestCustomFloorMod_SetPathActive_UnityEngine_Transform_System_String_System_Boolean(UnityEngine::Transform* root, ::StringW path, bool active) {
    UnityEngine::Transform* child{};

    child = root->Find(path);
    if ((child != nullptr) && (((child->get_gameObject() != nullptr)))) {
        child->get_gameObject()->SetActive(active);
    }
}

static void CustomFloorPlugin_QuestCustomFloorMod_SetNamedObjectsActive_UnityEngine_Transform_System_String___System_Boolean(UnityEngine::Transform* root, ArrayW<::StringW> names, bool active) {
    int32_t i{};
    UnityEngine::Transform* child{};

    i = 0;
    while (true) {
        if (!(((i < static_cast<int32_t>(static_cast<int>(names.size())))))) {
            break;
        }
        child = GlobalNamespace::TransformExtensions::FindChildRecursively(root, names[i]);
        if ((child != nullptr) && (((child->get_gameObject() != nullptr)))) {
            child->get_gameObject()->SetActive(active);
        }
        i = (i + 1);
    }
}

static void OnMenuEnvironmentStart(GlobalNamespace::MenuEnvironmentManager* self) {
    CustomFloorPlugin_QuestCustomFloorMod_ApplyMenuPlatform_GlobalNamespace_MenuEnvironmentManager(self);
}

MAKE_HOOK_MATCH(
    GlobalNamespace_MenuEnvironmentManager_Start_GlobalNamespace_MenuEnvironmentManager_Hook,
    &GlobalNamespace::MenuEnvironmentManager::Start,
    void,
    GlobalNamespace::MenuEnvironmentManager* self) {
    GlobalNamespace_MenuEnvironmentManager_Start_GlobalNamespace_MenuEnvironmentManager_Hook(self);
    OnMenuEnvironmentStart(self);
    return;
}

static void OnMenuEnvironmentChanged(GlobalNamespace::MenuEnvironmentManager* self, GlobalNamespace::MenuEnvironmentManager_MenuEnvironmentType menuEnvironmentType) {
    CustomFloorPlugin_QuestCustomFloorMod_ApplyMenuPlatform_GlobalNamespace_MenuEnvironmentManager(self);
}

MAKE_HOOK_MATCH(
    GlobalNamespace_MenuEnvironmentManager_ShowEnvironmentType_GlobalNamespace_MenuEnvironmentManager_GlobalNamespace_MenuEnvironmentManager_MenuEnvironmentType_Hook,
    &GlobalNamespace::MenuEnvironmentManager::ShowEnvironmentType,
    void,
    GlobalNamespace::MenuEnvironmentManager* self, GlobalNamespace::MenuEnvironmentManager_MenuEnvironmentType menuEnvironmentType) {
    GlobalNamespace_MenuEnvironmentManager_ShowEnvironmentType_GlobalNamespace_MenuEnvironmentManager_GlobalNamespace_MenuEnvironmentManager_MenuEnvironmentType_Hook(self, menuEnvironmentType);
    OnMenuEnvironmentChanged(self, menuEnvironmentType);
    return;
}

static void OnGameCoreSceneSetupStart(GlobalNamespace::GameCoreSceneSetup* self) {
    CustomFloorPlugin_QuestCustomFloorMod_ApplyGameplayPlatform();
}

MAKE_HOOK_MATCH(
    GlobalNamespace_GameCoreSceneSetup_Start_GlobalNamespace_GameCoreSceneSetup_Hook,
    &Zenject::MonoInstallerBase::Start,
    void,
    Zenject::MonoInstallerBase* selfRaw) {
    auto self = reinterpret_cast<GlobalNamespace::GameCoreSceneSetup*>(selfRaw);
    GlobalNamespace_GameCoreSceneSetup_Start_GlobalNamespace_GameCoreSceneSetup_Hook(selfRaw);
    OnGameCoreSceneSetupStart(self);
    return;
}

static void OnStandardLevelInit(GlobalNamespace::StandardLevelScenesTransitionSetupDataSO* self, GlobalNamespace::PlayerSpecificSettings* playerSpecificSettings, ::StringW backButtonText, bool startPaused) {
    CustomFloorPlugin_QuestCustomFloorMod__isMultiplayerScenePending = 0;
}

MAKE_HOOK_MATCH(
    GlobalNamespace_StandardLevelScenesTransitionSetupDataSO_InitAndSetupScenes_GlobalNamespace_StandardLevelScenesTransitionSetupDataSO_GlobalNamespace_PlayerSpecificSettings_System_String_System_Boolean_Hook,
    &GlobalNamespace::StandardLevelScenesTransitionSetupDataSO::InitAndSetupScenes,
    void,
    GlobalNamespace::StandardLevelScenesTransitionSetupDataSO* self, GlobalNamespace::PlayerSpecificSettings* playerSpecificSettings, ::StringW backButtonText, bool startPaused) {
    GlobalNamespace_StandardLevelScenesTransitionSetupDataSO_InitAndSetupScenes_GlobalNamespace_StandardLevelScenesTransitionSetupDataSO_GlobalNamespace_PlayerSpecificSettings_System_String_System_Boolean_Hook(self, playerSpecificSettings, backButtonText, startPaused);
    OnStandardLevelInit(self, playerSpecificSettings, backButtonText, startPaused);
    return;
}

static void OnMultiplayerLevelInit(GlobalNamespace::MultiplayerLevelScenesTransitionSetupDataSO* self) {
    CustomFloorPlugin_QuestCustomFloorMod__isMultiplayerScenePending = 1;
}

MAKE_HOOK_MATCH(
    GlobalNamespace_MultiplayerLevelScenesTransitionSetupDataSO_InitAndSetupScenes_GlobalNamespace_MultiplayerLevelScenesTransitionSetupDataSO_Hook,
    &GlobalNamespace::MultiplayerLevelScenesTransitionSetupDataSO::InitAndSetupScenes,
    void,
    GlobalNamespace::MultiplayerLevelScenesTransitionSetupDataSO* self) {
    GlobalNamespace_MultiplayerLevelScenesTransitionSetupDataSO_InitAndSetupScenes_GlobalNamespace_MultiplayerLevelScenesTransitionSetupDataSO_Hook(self);
    OnMultiplayerLevelInit(self);
    return;
}

static void OnStopStandardLevel(GlobalNamespace::MenuTransitionsHelper* self) {
    CustomFloorPlugin_QuestCustomFloorMod__isMultiplayerScenePending = 0;
    CustomFloorPlugin_QuestCustomFloorMod_DestroyPlatform_UnityEngine_GameObject_(CustomFloorPlugin_QuestCustomFloorMod__activeGameplayPlatform);
}

MAKE_HOOK_MATCH(
    GlobalNamespace_MenuTransitionsHelper_StopStandardLevel_GlobalNamespace_MenuTransitionsHelper_Hook,
    &GlobalNamespace::MenuTransitionsHelper::StopStandardLevel,
    void,
    GlobalNamespace::MenuTransitionsHelper* self) {
    GlobalNamespace_MenuTransitionsHelper_StopStandardLevel_GlobalNamespace_MenuTransitionsHelper_Hook(self);
    OnStopStandardLevel(self);
    return;
}

MOD_EXTERN_FUNC void late_load() noexcept {
    il2cpp_functions::Init();
    InitializeLocalStaticFields();
    custom_types::Register::AutoRegister();
    PaperLogger.info("Installing hooks...");

    INSTALL_HOOK(PaperLogger, GlobalNamespace_MenuEnvironmentManager_Start_GlobalNamespace_MenuEnvironmentManager_Hook);
    INSTALL_HOOK(PaperLogger, GlobalNamespace_MenuEnvironmentManager_ShowEnvironmentType_GlobalNamespace_MenuEnvironmentManager_GlobalNamespace_MenuEnvironmentManager_MenuEnvironmentType_Hook);
    INSTALL_HOOK(PaperLogger, GlobalNamespace_GameCoreSceneSetup_Start_GlobalNamespace_GameCoreSceneSetup_Hook);
    INSTALL_HOOK(PaperLogger, GlobalNamespace_StandardLevelScenesTransitionSetupDataSO_InitAndSetupScenes_GlobalNamespace_StandardLevelScenesTransitionSetupDataSO_GlobalNamespace_PlayerSpecificSettings_System_String_System_Boolean_Hook);
    INSTALL_HOOK(PaperLogger, GlobalNamespace_MultiplayerLevelScenesTransitionSetupDataSO_InitAndSetupScenes_GlobalNamespace_MultiplayerLevelScenesTransitionSetupDataSO_Hook);
    INSTALL_HOOK(PaperLogger, GlobalNamespace_MenuTransitionsHelper_StopStandardLevel_GlobalNamespace_MenuTransitionsHelper_Hook);

    PaperLogger.info("Installed all hooks!");
}
