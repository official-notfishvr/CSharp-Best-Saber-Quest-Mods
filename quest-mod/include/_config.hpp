#pragma once

#define MOD_EXPORT __attribute__((visibility("default")))
#define MOD_EXTERN_FUNC extern "C" MOD_EXPORT

#include "beatsaber-hook/shared/utils/il2cpp-utils.hpp"

static bool Enabled = true;
static ::StringW ButtonText = il2cpp_utils::newcsstr("Skill Issue");
static ::StringW PrefixSuffix = il2cpp_utils::newcsstr(" [pre]");
static ::StringW PostfixSuffix = il2cpp_utils::newcsstr(" [post]");
static ::StringW VersionStatusText = il2cpp_utils::newcsstr("Mod Active");
