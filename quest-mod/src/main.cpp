#include "main.hpp"
#include "scotland2/shared/modloader.h"

#include "GlobalNamespace/StandardLevelDetailView.hpp"
#include "GlobalNamespace/StandardLevelDetailViewController.hpp"
#include "HMUI/CurvedTextMeshPro.hpp"
#include "UnityEngine/GameObject.hpp"
#include "UnityEngine/UI/Button.hpp"

static modloader::ModInfo modInfo{"com.example.testmod", "1.0.0", 0};

Configuration &getConfig() {
    static Configuration config(modInfo);
    return config;
}

MAKE_HOOK_MATCH(
    OnLevelScreenActivateHook,
    &GlobalNamespace::StandardLevelDetailViewController::DidActivate,
    void,
    GlobalNamespace::StandardLevelDetailViewController* self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) {
    OnLevelScreenActivateHook(self, firstActivation, addedToHierarchy, screenSystemEnabling);
    if (Enabled) {
        auto detailView = self->_standardLevelDetailView;
        auto actionButton = detailView->_actionButton;
        auto gameObject = actionButton->get_gameObject();
        auto buttonText = gameObject->GetComponentInChildren<HMUI::CurvedTextMeshPro*>();
        buttonText->set_text(ButtonText);
    }
}

MOD_EXTERN_FUNC void late_load() noexcept {
    il2cpp_functions::Init();
    PaperLogger.info("Installing hooks...");

    INSTALL_HOOK(PaperLogger, OnLevelScreenActivateHook);

    PaperLogger.info("Installed all hooks!");
}
