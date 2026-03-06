#include "main.hpp"
#include "scotland2/shared/modloader.h"

#include "GlobalNamespace/StandardLevelDetailViewController.hpp"
#include "GlobalNamespace/StandardLevelDetailView.hpp"
#include "UnityEngine/UI/Button.hpp"
#include "UnityEngine/GameObject.hpp"
#include "HMUI/CurvedTextMeshPro.hpp"

static modloader::ModInfo modInfo{"com.example.testmod", "1.0.0", 0};

Configuration &getConfig() {
    static Configuration config(modInfo);
    return config;
}

MOD_EXTERN_FUNC void setup(CModInfo *info) noexcept {
    *info = modInfo.to_c();
    getConfig().Load();
    Paper::Logger::RegisterFileContextId(PaperLogger.tag);
    PaperLogger.info("Completed setup!");
}

MAKE_HOOK_MATCH(
    OnLevelScreenActivateHook,
    &GlobalNamespace::StandardLevelDetailViewController::DidActivate,
    void,
    GlobalNamespace::StandardLevelDetailViewController* self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) {

    OnLevelScreenActivateHook(self, firstActivation, addedToHierarchy, screenSystemEnabling);
    if (Enabled) {
        GlobalNamespace::StandardLevelDetailView* detailView = self->_standardLevelDetailView;
        UnityEngine::UI::Button* actionButton = detailView->actionButton;
        UnityEngine::GameObject* gameObject = actionButton->get_gameObject();
        HMUI::CurvedTextMeshPro* buttonText = gameObject->GetComponentInChildren<HMUI::CurvedTextMeshPro*>();
        buttonText->set_text(ButtonText);
    }
}

MOD_EXTERN_FUNC void late_load() noexcept {
    il2cpp_functions::Init();
    PaperLogger.info("Installing hooks...");

    INSTALL_HOOK(PaperLogger, OnLevelScreenActivateHook);

    PaperLogger.info("Installed all hooks!");
}