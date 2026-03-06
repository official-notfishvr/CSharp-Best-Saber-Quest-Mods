#pragma once

#define MOD_EXPORT __attribute__((visibility("default")))
#define MOD_EXTERN_FUNC extern "C" MOD_EXPORT

#include "beatsaber-hook/shared/utils/il2cpp-utils.hpp"

#define MOD_ID "com.example.testmod"
#define VERSION "1.0.0"

static bool Enabled = true;
static Il2CppString* ButtonText = il2cpp_utils::newcsstr("Skill Issue");