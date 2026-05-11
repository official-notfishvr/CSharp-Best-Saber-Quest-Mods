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

static bool PauseKey_PauseKeyMod_GetBindingState_GlobalNamespace_UnityXRHelper_System_Int32(GlobalNamespace::UnityXRHelper* self, int32_t binding);

static bool PauseKey_PauseKeyMod_GetBindingState_GlobalNamespace_UnityXRHelper_System_Int32(GlobalNamespace::UnityXRHelper* self, int32_t binding) {
    bool local0{};
    bool local1{};
    bool local2{};
    bool local3{};
    bool local4{};
    bool local5{};
    UnityEngine::Vector2 thumbstick;
    bool local7{};
    bool local8{};
    UnityEngine::Vector2 thumbstick_1;
    bool local10{};
    
    local0 = (binding == 1);
    if ((local0)) {
        local1 = (self->____leftController == nullptr);
        if ((local1)) {
            local2 = 0;
            return local2;
        }
        local2 = !((self->GetTriggerValue(self->____leftController->___node) < TriggerThreshold));
    }
    else {
        local3 = (binding == 2);
        if ((local3)) {
            local4 = (self->____rightController == nullptr);
            if ((local4)) {
                local2 = 0;
                return local2;
            }
            local2 = !((self->GetTriggerValue(self->____rightController->___node) < TriggerThreshold));
        }
        else {
            local5 = (binding == 9);
            if ((local5)) {
                local7 = (self->____leftController == nullptr);
                if ((local7)) {
                    local2 = 0;
                    return local2;
                }
                thumbstick = self->GetThumbstickValue(self->____leftController->___node);
                local2 = !((UnityEngine::Vector2::SqrMagnitude(thumbstick) < (ThumbstickThreshold * ThumbstickThreshold)));
            }
            else {
                local8 = (binding == 10);
                if ((local8)) {
                    local10 = (self->____rightController == nullptr);
                    if ((local10)) {
                        local2 = 0;
                        return local2;
                    }
                    thumbstick_1 = self->GetThumbstickValue(self->____rightController->___node);
                    local2 = !((UnityEngine::Vector2::SqrMagnitude(thumbstick_1) < (ThumbstickThreshold * ThumbstickThreshold)));
                }
                else {
                    local2 = 0;
                    return local2;
                }
            }
        }
    }
    return local2;
}

MAKE_HOOK_MATCH(
    GlobalNamespace_UnityXRHelper_GetMenuButton_GlobalNamespace_UnityXRHelper_Hook,
    &GlobalNamespace::UnityXRHelper::GetMenuButton,
    bool,
    GlobalNamespace::UnityXRHelper* self) {
    bool local0{};
    bool local1{};
    bool local2{};
    
    local0 = !(Enabled);
    if ((local0)) {
        local1 = GlobalNamespace_UnityXRHelper_GetMenuButton_GlobalNamespace_UnityXRHelper_Hook(self);
    }
    else {
        local2 = (PauseBinding == 0);
        if ((local2)) {
            local1 = GlobalNamespace_UnityXRHelper_GetMenuButton_GlobalNamespace_UnityXRHelper_Hook(self);
        }
        else {
            local1 = PauseKey_PauseKeyMod_GetBindingState_GlobalNamespace_UnityXRHelper_System_Int32(self, PauseBinding);
            return local1;
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
    
    local2 = !(Enabled);
    if ((local2)) {
        local3 = GlobalNamespace_UnityXRHelper_GetMenuButtonDown_GlobalNamespace_UnityXRHelper_Hook(self);
    }
    else {
        local4 = (PauseBinding == 0);
        if ((local4)) {
            local3 = GlobalNamespace_UnityXRHelper_GetMenuButtonDown_GlobalNamespace_UnityXRHelper_Hook(self);
        }
        else {
            isPressed = PauseKey_PauseKeyMod_GetBindingState_GlobalNamespace_UnityXRHelper_System_Int32(self, PauseBinding);
            pressedThisFrame = 0;
            local5 = isPressed;
            if ((local5)) {
                pressedThisFrame = !(PauseKey_PauseKeyMod__pressedLastFrame);
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
    
    local0 = !(pauseStatus);
    if ((local0)) {
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
