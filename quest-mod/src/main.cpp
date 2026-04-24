#include "main.hpp"
#include "scotland2/shared/modloader.h"

#include "GlobalNamespace/UnityXRController.hpp"
#include "GlobalNamespace/UnityXRHelper.hpp"
#include "UnityEngine/Vector2.hpp"
#include "UnityEngine/XR/XRNode.hpp"

static modloader::ModInfo modInfo{"com.csharp.quest.pausekey", "0.1.0", 0};

static bool PauseKey_PauseKeyMod__pressedLastFrame = false;

Configuration &getConfig() {
    static Configuration config(modInfo);
    return config;
}

MAKE_HOOK_MATCH(
    GlobalNamespace_UnityXRHelper_GetMenuButton_GlobalNamespace_UnityXRHelper_Hook,
    &GlobalNamespace::UnityXRHelper::GetMenuButton,
    bool,
    GlobalNamespace::UnityXRHelper* self) {
    bool local0{};
    bool local1{};
    bool local2{};
    bool local3{};
    bool local4{};
    bool local5{};
    bool local6{};
    bool local7{};
    bool local9{};
    bool local10{};
    bool local12{};
    
    local0 = (Enabled == 0);
    if (local0) {
        local1 = GlobalNamespace_UnityXRHelper_GetMenuButton_GlobalNamespace_UnityXRHelper_Hook(self);
    }
    else {
        local2 = (PauseBinding == 0);
        if (local2) {
            local1 = GlobalNamespace_UnityXRHelper_GetMenuButton_GlobalNamespace_UnityXRHelper_Hook(self);
        }
        else {
            local3 = (PauseBinding == 1);
            if (local3) {
                local4 = (self->____leftController == nullptr);
                if (local4) {
                    local1 = 0;
                    return local1;
                }
                local1 = ((self->GetTriggerValue(self->____leftController->___node) < TriggerThreshold) == 0);
            }
            else {
                local5 = (PauseBinding == 2);
                if (local5) {
                    local6 = (self->____rightController == nullptr);
                    if (local6) {
                        local1 = 0;
                        return local1;
                    }
                    local1 = ((self->GetTriggerValue(self->____rightController->___node) < TriggerThreshold) == 0);
                }
                else {
                    local7 = (PauseBinding == 3);
                    if (local7) {
                        local9 = (self->____leftController == nullptr);
                        if (local9) {
                            local1 = 0;
                            return local1;
                        }
                        auto thumbstick = self->GetThumbstickValue(self->____leftController->___node);
                        local1 = ((UnityEngine::Vector2::SqrMagnitude(thumbstick) < (ThumbstickThreshold * ThumbstickThreshold)) == 0);
                    }
                    else {
                        local10 = (PauseBinding == 4);
                        if (local10) {
                            local12 = (self->____rightController == nullptr);
                            if (local12) {
                                local1 = 0;
                                return local1;
                            }
                            auto thumbstick_1 = self->GetThumbstickValue(self->____rightController->___node);
                            local1 = ((UnityEngine::Vector2::SqrMagnitude(thumbstick_1) < (ThumbstickThreshold * ThumbstickThreshold)) == 0);
                        }
                        else {
                            local1 = 0;
                            return local1;
                        }
                    }
                }
            }
        }
    }
    return local1;
}

MAKE_HOOK_MATCH(
    GlobalNamespace_UnityXRHelper_GetMenuButtonDown_GlobalNamespace_UnityXRHelper_Hook,
    &GlobalNamespace::UnityXRHelper::GetMenuButtonDown,
    bool,
    GlobalNamespace::UnityXRHelper* self) {
    bool isPressed{};
    bool pressedThisFrame{};
    bool local2{};
    bool local3{};
    bool local4{};
    bool local5{};
    bool local6{};
    bool local7{};
    bool local8{};
    bool local9{};
    bool local10{};
    bool local12{};
    bool local13{};
    bool local15{};
    
    local2 = (Enabled == 0);
    if (local2) {
        local3 = GlobalNamespace_UnityXRHelper_GetMenuButtonDown_GlobalNamespace_UnityXRHelper_Hook(self);
    }
    else {
        local4 = (PauseBinding == 0);
        if (local4) {
            local3 = GlobalNamespace_UnityXRHelper_GetMenuButtonDown_GlobalNamespace_UnityXRHelper_Hook(self);
        }
        else {
            isPressed = 0;
            local5 = (PauseBinding == 1);
            if (local5) {
                local6 = (self->____leftController != nullptr);
                if (local6) {
                    isPressed = ((self->GetTriggerValue(self->____leftController->___node) < TriggerThreshold) == 0);
                }
            }
            local7 = (PauseBinding == 2);
            if (local7) {
                local8 = (self->____rightController != nullptr);
                if (local8) {
                    isPressed = ((self->GetTriggerValue(self->____rightController->___node) < TriggerThreshold) == 0);
                }
            }
            local9 = (PauseBinding == 3);
            if (local9) {
                local10 = (self->____leftController != nullptr);
                if (local10) {
                    auto thumbstick = self->GetThumbstickValue(self->____leftController->___node);
                    isPressed = ((UnityEngine::Vector2::SqrMagnitude(thumbstick) < (ThumbstickThreshold * ThumbstickThreshold)) == 0);
                }
            }
            local12 = (PauseBinding == 4);
            if (local12) {
                local13 = (self->____rightController != nullptr);
                if (local13) {
                    auto thumbstick_1 = self->GetThumbstickValue(self->____rightController->___node);
                    isPressed = ((UnityEngine::Vector2::SqrMagnitude(thumbstick_1) < (ThumbstickThreshold * ThumbstickThreshold)) == 0);
                }
            }
            pressedThisFrame = 0;
            local15 = isPressed;
            if (local15) {
                pressedThisFrame = (PauseKey_PauseKeyMod__pressedLastFrame == 0);
            }
            PauseKey_PauseKeyMod__pressedLastFrame = isPressed;
            local3 = pressedThisFrame;
            return local3;
        }
    }
    return local3;
}

static void OnApplicationPausePostfix(GlobalNamespace::UnityXRHelper* self, bool pauseStatus) {
    bool local0{};
    
    local0 = (pauseStatus == 0);
    if (local0) {
        return;
    }
    PauseKey_PauseKeyMod__pressedLastFrame = 0;
}

MAKE_HOOK_MATCH(
    GlobalNamespace_UnityXRHelper_OnApplicationPause_GlobalNamespace_UnityXRHelper_System_Boolean_Hook,
    &GlobalNamespace::UnityXRHelper::OnApplicationPause,
    void,
    GlobalNamespace::UnityXRHelper* self, bool pauseStatus) {
    GlobalNamespace_UnityXRHelper_OnApplicationPause_GlobalNamespace_UnityXRHelper_System_Boolean_Hook(self, pauseStatus);
    OnApplicationPausePostfix(self, pauseStatus);
    return;
}

MOD_EXTERN_FUNC void late_load() noexcept {
    il2cpp_functions::Init();
    PaperLogger.info("Installing hooks...");

    INSTALL_HOOK(PaperLogger, GlobalNamespace_UnityXRHelper_GetMenuButton_GlobalNamespace_UnityXRHelper_Hook);
    INSTALL_HOOK(PaperLogger, GlobalNamespace_UnityXRHelper_GetMenuButtonDown_GlobalNamespace_UnityXRHelper_Hook);
    INSTALL_HOOK(PaperLogger, GlobalNamespace_UnityXRHelper_OnApplicationPause_GlobalNamespace_UnityXRHelper_System_Boolean_Hook);

    PaperLogger.info("Installed all hooks!");
}
