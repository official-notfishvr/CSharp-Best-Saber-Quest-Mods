#pragma once

#define MOD_EXPORT __attribute__((visibility("default")))
#define MOD_EXTERN_FUNC extern "C" MOD_EXPORT

#include "beatsaber-hook/shared/utils/il2cpp-utils.hpp"

static ::StringW PlatformDirectory = il2cpp_utils::newcsstr("/sdcard/ModData/com.beatgames.beatsaber/Mods/CustomPlatforms");
static ::StringW MenuPlatformPath = il2cpp_utils::newcsstr("");
static ::StringW GameplayPlatformPath = il2cpp_utils::newcsstr("");
static ::StringW MultiplayerPlatformPath = il2cpp_utils::newcsstr("");
