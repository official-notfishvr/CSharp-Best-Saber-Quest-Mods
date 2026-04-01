#include "main.hpp"
#include "scotland2/shared/modloader.h"

#include "GlobalNamespace/StandardLevelDetailView.hpp"
#include "GlobalNamespace/StandardLevelDetailViewController.hpp"
#include "TMPro/TextMeshProUGUI.hpp"
#include "UnityEngine/GameObject.hpp"
#include "UnityEngine/UI/Button.hpp"

static modloader::ModInfo modInfo{"com.example.testmod", "1.0.0", 0};

Configuration &getConfig() {
    static Configuration config(modInfo);
    return config;
}

static void OnLevelScreenActivatePrefix(GlobalNamespace::StandardLevelDetailViewController* self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) {
    if (!((Enabled == 0))) {
        auto detailView = self->_standardLevelDetailView;
        if (!((detailView == nullptr))) {
            if ((detailView->_buttonsWrapper != nullptr)) {
                detailView->_buttonsWrapper->SetActive(1);
            }
            if ((detailView->_actionButtonText != nullptr)) {
                detailView->_actionButtonText->set_text(System::String::Concat(ButtonText, PrefixSuffix));
            }
            if ((detailView->_beatmapLevelVersionText != nullptr)) {
                detailView->_beatmapLevelVersionText->set_text(VersionStatusText);
            }
            if ((detailView->_actionButton != nullptr)) {
                detailView->_actionButton->m_Interactable = 1;
            }
        }
    }
}

static void OnLevelScreenActivatePostfix(GlobalNamespace::StandardLevelDetailViewController* self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) {
    if (!((Enabled == 0))) {
        auto detailView = self->_standardLevelDetailView;
        if (!((detailView == nullptr))) {
            if ((detailView->_actionButtonText != nullptr)) {
                detailView->_actionButtonText->set_text(System::String::Concat(ButtonText, PostfixSuffix));
            }
            if ((detailView->_practiceButton != nullptr)) {
                detailView->_practiceButton->m_Interactable = 1;
            }
        }
    }
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
    PaperLogger.info("Installing hooks...");

    INSTALL_HOOK(PaperLogger, GlobalNamespace_StandardLevelDetailViewController_DidActivate_GlobalNamespace_StandardLevelDetailViewController_System_Boolean_System_Boolean_System_Boolean_Hook);

    PaperLogger.info("Installed all hooks!");
}
