#pragma once

#define MOD_EXPORT __attribute__((visibility("default")))
#define MOD_EXTERN_FUNC extern "C" MOD_EXPORT

#include "beatsaber-hook/shared/utils/il2cpp-utils.hpp"

static bool Enabled = true;
static bool SnapToBookmarks = true;
static float SnapWindowSeconds = 0.75;
static ::StringW BookmarkPrefix = il2cpp_utils::newcsstr("Bookmark: ");
