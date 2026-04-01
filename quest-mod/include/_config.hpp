#pragma once

#define MOD_EXPORT __attribute__((visibility("default")))
#define MOD_EXTERN_FUNC extern "C" MOD_EXPORT

#include "beatsaber-hook/shared/utils/il2cpp-utils.hpp"

static bool Enabled = true;
static Il2CppString* ButtonText = il2cpp_utils::newcsstr("Skill Issue");
static Il2CppString* PrefixSuffix = il2cpp_utils::newcsstr(" [pre]");
static Il2CppString* PostfixSuffix = il2cpp_utils::newcsstr(" [post]");
static Il2CppString* VersionStatusText = il2cpp_utils::newcsstr("Mod Active");
