#include "main.hpp"
#include "scotland2/shared/modloader.h"

#include "bsml/shared/BSML.hpp"
#include "GlobalNamespace/StandardLevelDetailView.hpp"
#include "GlobalNamespace/StandardLevelDetailViewController.hpp"
#include "System/String.hpp"
#include "TMPro/TextMeshProUGUI.hpp"
#include "TMPro/TMP_Text.hpp"
#include "UnityEngine/GameObject.hpp"
#include "UnityEngine/UI/Button.hpp"
#include "UnityEngine/UI/Selectable.hpp"

static modloader::ModInfo modInfo{"com.example.testmod", "1.0.0", 0};

Configuration &getConfig() {
    static Configuration config(modInfo);
    return config;
}

static void SampleMod_TestMod_OpenSampleMenu();
static void SampleMod_TestMod_OnGameplaySetupTabActivate_UnityEngine_GameObject_System_Boolean(UnityEngine::GameObject* root, bool firstActivation);

static void SampleMod_TestMod_OpenSampleMenu() {
    return;
}

static void SampleMod_TestMod_OnGameplaySetupTabActivate_UnityEngine_GameObject_System_Boolean(UnityEngine::GameObject* root, bool firstActivation) {
    return;
}

static void OnLevelScreenActivatePrefix(GlobalNamespace::StandardLevelDetailViewController* self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) {
    GlobalNamespace::StandardLevelDetailView* detailView{};
    bool local1{};
    bool local2{};
    bool local3{};
    bool local4{};
    bool local5{};
    bool local6{};
    
    local1 = (Enabled == 0);
    if (!(local1)) goto label_8;
    goto label_67;
    label_8:;
    detailView = self->____standardLevelDetailView;
    local2 = (detailView == nullptr);
    if (!(local2)) goto label_18;
    goto label_67;
    label_18:;
    local3 = (detailView->____buttonsWrapper != nullptr);
    if (!(local3)) goto label_30;
    detailView->____buttonsWrapper->SetActive(1);
    label_30:;
    local4 = (detailView->____actionButtonText != nullptr);
    if (!(local4)) goto label_44;
    detailView->____actionButtonText->set_text(::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System", "String"), "Concat", ButtonText, PrefixSuffix));
    label_44:;
    local5 = (detailView->____beatmapLevelVersionText != nullptr);
    if (!(local5)) goto label_56;
    detailView->____beatmapLevelVersionText->set_text(VersionStatusText);
    label_56:;
    local6 = (detailView->____actionButton != nullptr);
    if (!(local6)) goto label_67;
    detailView->____actionButton->___m_Interactable = 1;
    label_67:;
    return;
}

static void OnLevelScreenActivatePostfix(GlobalNamespace::StandardLevelDetailViewController* self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) {
    GlobalNamespace::StandardLevelDetailView* detailView{};
    bool local1{};
    bool local2{};
    bool local3{};
    bool local4{};
    
    local1 = (Enabled == 0);
    if (!(local1)) goto label_8;
    goto label_43;
    label_8:;
    detailView = self->____standardLevelDetailView;
    local2 = (detailView == nullptr);
    if (!(local2)) goto label_18;
    goto label_43;
    label_18:;
    local3 = (detailView->____actionButtonText != nullptr);
    if (!(local3)) goto label_32;
    detailView->____actionButtonText->set_text(::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System", "String"), "Concat", ButtonText, PostfixSuffix));
    label_32:;
    local4 = (detailView->____practiceButton != nullptr);
    if (!(local4)) goto label_43;
    detailView->____practiceButton->___m_Interactable = 1;
    label_43:;
    return;
}

MAKE_HOOK_MATCH(
    GlobalNamespace_StandardLevelDetailViewController_DidActivate_GlobalNamespace_StandardLevelDetailViewController_System_Boolean_System_Boolean_System_Boolean_Hook,
    &GlobalNamespace::StandardLevelDetailViewController::DidActivate,
    void,
    GlobalNamespace::StandardLevelDetailViewController* self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) {
    OnLevelScreenActivatePrefix(self, firstActivation, addedToHierarchy, screenSystemEnabling);
    GlobalNamespace_StandardLevelDetailViewController_DidActivate_GlobalNamespace_StandardLevelDetailViewController_System_Boolean_System_Boolean_System_Boolean_Hook(self, firstActivation, addedToHierarchy, screenSystemEnabling);
    OnLevelScreenActivatePostfix(self, firstActivation, addedToHierarchy, screenSystemEnabling);
    return;
}

MOD_EXTERN_FUNC void late_load() noexcept {
    il2cpp_functions::Init();
    BSML::Init();
    BSML::Register::RegisterMenuButton("SampleMod", "Open the SampleMod menu", SampleMod_TestMod_OpenSampleMenu);
    BSML::Register::RegisterGameplaySetupTab("SampleMod", SampleMod_TestMod_OnGameplaySetupTabActivate_UnityEngine_GameObject_System_Boolean, BSML::MenuType::All);

    PaperLogger.info("Installing hooks...");

    INSTALL_HOOK(PaperLogger, GlobalNamespace_StandardLevelDetailViewController_DidActivate_GlobalNamespace_StandardLevelDetailViewController_System_Boolean_System_Boolean_System_Boolean_Hook);

    PaperLogger.info("Installed all hooks!");
}
