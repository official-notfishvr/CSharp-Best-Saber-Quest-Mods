#pragma once

#define MOD_EXPORT __attribute__((visibility("default")))
#define MOD_EXTERN_FUNC extern "C" MOD_EXPORT

#include "beatsaber-hook/shared/utils/il2cpp-utils.hpp"

static bool Enabled = true;
static int32_t PauseBinding = 0;
static float TriggerThreshold = 0.75;
static float ThumbstickThreshold = 0.85;
