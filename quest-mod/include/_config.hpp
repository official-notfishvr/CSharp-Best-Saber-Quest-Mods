#pragma once

#define MOD_EXPORT __attribute__((visibility("default")))
#define MOD_EXTERN_FUNC extern "C" MOD_EXPORT

#include "beatsaber-hook/shared/utils/il2cpp-utils.hpp"

static ::StringW PlatformDirectory = nullptr;
static ::StringW MenuPlatformPath = nullptr;
static ::StringW GameplayPlatformPath = nullptr;
static ::StringW MultiplayerPlatformPath = nullptr;
