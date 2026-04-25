#include "main.hpp"
#include "scotland2/shared/modloader.h"

#include "GlobalNamespace/BeatmapLevel.hpp"
#include "GlobalNamespace/FileUtility.hpp"
#include "GlobalNamespace/PracticeSettings.hpp"
#include "GlobalNamespace/PracticeViewController.hpp"
#include "HMUI/RangeValuesTextSlider.hpp"
#include "HMUI/TextSlider.hpp"
#include "HMUI/TimeSlider.hpp"
#include "System/Collections/Generic/List_1.hpp"
#include "System/Globalization/CultureInfo.hpp"
#include "System/IFormatProvider.hpp"
#include "System/IO/Directory.hpp"
#include "System/IO/File.hpp"
#include "System/IO/Path.hpp"
#include "System/Nullable_1.hpp"
#include "System/String.hpp"
#include "System/StringComparison.hpp"
#include "TMPro/TextMeshProUGUI.hpp"
#include "TMPro/TMP_Text.hpp"

static modloader::ModInfo modInfo{"com.csharp.quest.practicebookmarks", "0.1.0", 0};

static System::Collections::Generic::List_1<::StringW>* PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkNames = nullptr;
static System::Collections::Generic::List_1<float>* PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkTimes = nullptr;
static ::StringW PracticeBookmarks_PracticeBookmarksMod__activeLevelId = nullptr;
static bool PracticeBookmarks_PracticeBookmarksMod__isApplyingSnap = false;

Configuration &getConfig() {
    static Configuration config(modInfo);
    return config;
}

static void PracticeBookmarks_PracticeBookmarksMod_LoadBookmarks_GlobalNamespace_PracticeViewController(GlobalNamespace::PracticeViewController* self);
static void PracticeBookmarks_PracticeBookmarksMod_LoadBookmarksFromInfoFile_System_String_System_Single(::StringW infoPath, float beatsPerMinute);
static void PracticeBookmarks_PracticeBookmarksMod_LoadBookmarksFromDifficultyFile_System_String_System_Single(::StringW difficultyPath, float beatsPerMinute);
static ::StringW PracticeBookmarks_PracticeBookmarksMod_ExtractBookmarkName_System_String(::StringW json);
static System::Nullable_1<float> PracticeBookmarks_PracticeBookmarksMod_ExtractBookmarkSeconds_System_String_System_Single(::StringW json, float beatsPerMinute);
static ::StringW PracticeBookmarks_PracticeBookmarksMod_TryResolveCustomLevelFolder_System_String(::StringW levelId);
static ::StringW PracticeBookmarks_PracticeBookmarksMod_TryFindDirectoryContaining_System_String_System_String(::StringW root, ::StringW token);
static ::StringW PracticeBookmarks_PracticeBookmarksMod_FindInfoFile_System_String(::StringW levelFolder);
static void PracticeBookmarks_PracticeBookmarksMod_UpdateBookmarkLabel_GlobalNamespace_PracticeViewController_System_Int32(GlobalNamespace::PracticeViewController* self, int32_t bookmarkIndex);
static int32_t PracticeBookmarks_PracticeBookmarksMod_FindNearestBookmarkIndex_System_Single_System_Single(float time, float windowSeconds);
static float PracticeBookmarks_PracticeBookmarksMod_GetCurrentPracticeTime_GlobalNamespace_PracticeViewController(GlobalNamespace::PracticeViewController* self);
static float PracticeBookmarks_PracticeBookmarksMod_BeatsToSeconds_System_Single_System_Single(float beatsPerMinute, float beat);
static float PracticeBookmarks_PracticeBookmarksMod_Abs_System_Single(float value);
static ::StringW PracticeBookmarks_PracticeBookmarksMod_ExtractCustomLevelToken_System_String(::StringW levelId);
static ::StringW PracticeBookmarks_PracticeBookmarksMod_TryExtractArray_System_String_System_String(::StringW json, ::StringW key);
static System::Collections::Generic::List_1<::StringW>* PracticeBookmarks_PracticeBookmarksMod_SplitTopLevelObjects_System_String(::StringW arrayContent);
static ::StringW PracticeBookmarks_PracticeBookmarksMod_ExtractStringValue_System_String_System_String(::StringW json, ::StringW key);
static System::Nullable_1<float> PracticeBookmarks_PracticeBookmarksMod_ExtractFloatValue_System_String_System_String(::StringW json, ::StringW key);
static int32_t PracticeBookmarks_PracticeBookmarksMod_FindKeyIndex_System_String_System_String(::StringW json, ::StringW key);
static int32_t PracticeBookmarks_PracticeBookmarksMod_FindMatchingToken_System_String_System_Int32_System_Char_System_Char(::StringW text, int32_t startIndex, Il2CppChar openToken, Il2CppChar closeToken);
static void PracticeBookmarks_PracticeBookmarksMod_EnsureBookmarkStorage();
static void PracticeBookmarks_PracticeBookmarksMod_ClearBookmarks();
static void PracticeBookmarks_PracticeBookmarksMod_AddBookmark_System_String_System_Single(::StringW name, float time);
static int32_t PracticeBookmarks_PracticeBookmarksMod_GetBookmarkCount();
static ::StringW PracticeBookmarks_PracticeBookmarksMod_GetBookmarkName_System_Int32(int32_t index);
static float PracticeBookmarks_PracticeBookmarksMod_GetBookmarkTime_System_Int32(int32_t index);

static void PracticeBookmarks_PracticeBookmarksMod_LoadBookmarks_GlobalNamespace_PracticeViewController(GlobalNamespace::PracticeViewController* self) {
    GlobalNamespace::BeatmapLevel* level{};
    ::StringW customLevelFolder{};
    ::StringW infoPath{};
    bool local3{};
    bool local4{};
    bool local5{};
    
    PracticeBookmarks_PracticeBookmarksMod_ClearBookmarks();
    level = self->____beatmapLevel;
    if (!(level)) goto label_12;
    goto label_13;
    label_12:;
    label_13:;
    local3 = 1;
    if (!(local3)) goto label_17;
    goto label_44;
    label_17:;
    PracticeBookmarks_PracticeBookmarksMod__activeLevelId = level->___levelID;
    customLevelFolder = PracticeBookmarks_PracticeBookmarksMod_TryResolveCustomLevelFolder_System_String(level->___levelID);
    local4 = ::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", customLevelFolder);
    if (!(local4)) goto label_30;
    goto label_44;
    label_30:;
    infoPath = PracticeBookmarks_PracticeBookmarksMod_FindInfoFile_System_String(customLevelFolder);
    local5 = ::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", infoPath);
    if (!(local5)) goto label_39;
    goto label_44;
    label_39:;
    PracticeBookmarks_PracticeBookmarksMod_LoadBookmarksFromInfoFile_System_String_System_Single(infoPath, level->___beatsPerMinute);
    label_44:;
    return;
}

static void PracticeBookmarks_PracticeBookmarksMod_LoadBookmarksFromInfoFile_System_String_System_Single(::StringW infoPath, float beatsPerMinute) {
    ::StringW infoJson{};
    ::StringW difficultySets{};
    System::Collections::Generic::List_1<::StringW>* setObjects{};
    bool local3{};
    int32_t setIndex{};
    ::StringW setJson{};
    ::StringW difficultyArray{};
    System::Collections::Generic::List_1<::StringW>* difficultyObjects{};
    bool local8{};
    int32_t difficultyIndex{};
    ::StringW filename{};
    ::StringW levelFolder{};
    ::StringW difficultyPath{};
    bool local13{};
    bool local14{};
    bool local15{};
    bool local16{};
    bool local17{};
    ::StringW dupTemp0{};
    ::StringW dupTemp1{};
    ::StringW dupTemp2{};
    
    infoJson = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System.IO", "File"), "ReadAllText", infoPath);
    dupTemp0 = PracticeBookmarks_PracticeBookmarksMod_TryExtractArray_System_String_System_String(infoJson, il2cpp_utils::newcsstr("_difficultyBeatmapSets"));
    if (dupTemp0) goto label_13;
    label_13:;
    difficultySets = PracticeBookmarks_PracticeBookmarksMod_TryExtractArray_System_String_System_String(infoJson, il2cpp_utils::newcsstr("difficultyBeatmapSets"));
    local3 = ::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", difficultySets);
    if (!(local3)) goto label_20;
    goto label_120;
    label_20:;
    setObjects = PracticeBookmarks_PracticeBookmarksMod_SplitTopLevelObjects_System_String(difficultySets);
    setIndex = 0;
    goto label_113;
    label_26:;
    setJson = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(setObjects, "get_Item", setIndex);
    dupTemp1 = PracticeBookmarks_PracticeBookmarksMod_TryExtractArray_System_String_System_String(setJson, il2cpp_utils::newcsstr("_difficultyBeatmaps"));
    if (dupTemp1) goto label_40;
    label_40:;
    difficultyArray = PracticeBookmarks_PracticeBookmarksMod_TryExtractArray_System_String_System_String(setJson, il2cpp_utils::newcsstr("difficultyBeatmaps"));
    local8 = ::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", difficultyArray);
    if (!(local8)) goto label_47;
    goto label_109;
    label_47:;
    difficultyObjects = PracticeBookmarks_PracticeBookmarksMod_SplitTopLevelObjects_System_String(difficultyArray);
    difficultyIndex = 0;
    goto label_101;
    label_53:;
    dupTemp2 = PracticeBookmarks_PracticeBookmarksMod_ExtractStringValue_System_String_System_String(::il2cpp_utils::RunMethodRethrow<::StringW, false>(difficultyObjects, "get_Item", difficultyIndex), il2cpp_utils::newcsstr("_beatmapFilename"));
    if (dupTemp2) goto label_67;
    label_67:;
    filename = PracticeBookmarks_PracticeBookmarksMod_ExtractStringValue_System_String_System_String(::il2cpp_utils::RunMethodRethrow<::StringW, false>(difficultyObjects, "get_Item", difficultyIndex), il2cpp_utils::newcsstr("beatmapFilename"));
    local13 = ::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", filename);
    if (!(local13)) goto label_74;
    goto label_97;
    label_74:;
    levelFolder = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System.IO", "Path"), "GetDirectoryName", infoPath);
    local14 = ::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", levelFolder);
    if (!(local14)) goto label_83;
    goto label_97;
    label_83:;
    difficultyPath = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System.IO", "Path"), "Combine", levelFolder, filename);
    local15 = ::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System.IO", "File"), "Exists", difficultyPath);
    if (!(local15)) goto label_96;
    PracticeBookmarks_PracticeBookmarksMod_LoadBookmarksFromDifficultyFile_System_String_System_Single(difficultyPath, beatsPerMinute);
    label_96:;
    label_97:;
    difficultyIndex = (difficultyIndex + 1);
    label_101:;
    local16 = (difficultyIndex < ::il2cpp_utils::RunMethodRethrow<int32_t, false>(difficultyObjects, "get_Count"));
    if (local16) goto label_53;
    label_109:;
    setIndex = (setIndex + 1);
    label_113:;
    local17 = (setIndex < ::il2cpp_utils::RunMethodRethrow<int32_t, false>(setObjects, "get_Count"));
    if (local17) goto label_26;
    label_120:;
    return;
}

static void PracticeBookmarks_PracticeBookmarksMod_LoadBookmarksFromDifficultyFile_System_String_System_Single(::StringW difficultyPath, float beatsPerMinute) {
    ::StringW json{};
    ::StringW bookmarkArray{};
    System::Collections::Generic::List_1<::StringW>* objects{};
    bool local3{};
    int32_t i{};
    ::StringW name{};
    System::Nullable_1<float> seconds;
    bool local7{};
    bool local8{};
    bool local9{};
    ::StringW dupTemp0{};
    
    json = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System.IO", "File"), "ReadAllText", difficultyPath);
    dupTemp0 = PracticeBookmarks_PracticeBookmarksMod_TryExtractArray_System_String_System_String(json, il2cpp_utils::newcsstr("_bookmarks"));
    if (dupTemp0) goto label_13;
    label_13:;
    bookmarkArray = PracticeBookmarks_PracticeBookmarksMod_TryExtractArray_System_String_System_String(json, il2cpp_utils::newcsstr("bookmarks"));
    local3 = ::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", bookmarkArray);
    if (!(local3)) goto label_20;
    goto label_69;
    label_20:;
    objects = PracticeBookmarks_PracticeBookmarksMod_SplitTopLevelObjects_System_String(bookmarkArray);
    i = 0;
    goto label_62;
    label_26:;
    name = PracticeBookmarks_PracticeBookmarksMod_ExtractBookmarkName_System_String(::il2cpp_utils::RunMethodRethrow<::StringW, false>(objects, "get_Item", i));
    local7 = ::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", name);
    if (!(local7)) goto label_38;
    goto label_58;
    label_38:;
    seconds = PracticeBookmarks_PracticeBookmarksMod_ExtractBookmarkSeconds_System_String_System_Single(::il2cpp_utils::RunMethodRethrow<::StringW, false>(objects, "get_Item", i), beatsPerMinute);
    local8 = (seconds.get_HasValue() == 0);
    if (!(local8)) goto label_52;
    goto label_58;
    label_52:;
    PracticeBookmarks_PracticeBookmarksMod_AddBookmark_System_String_System_Single(name, seconds.get_Value());
    label_58:;
    i = (i + 1);
    label_62:;
    local9 = (i < ::il2cpp_utils::RunMethodRethrow<int32_t, false>(objects, "get_Count"));
    if (local9) goto label_26;
    label_69:;
    return;
}

static ::StringW PracticeBookmarks_PracticeBookmarksMod_ExtractBookmarkName_System_String(::StringW json) {
    ::StringW local0{};
    ::StringW dupTemp0{};
    
    dupTemp0 = PracticeBookmarks_PracticeBookmarksMod_ExtractStringValue_System_String_System_String(json, il2cpp_utils::newcsstr("_name"));
    if (dupTemp0) goto label_10;
    label_10:;
    local0 = PracticeBookmarks_PracticeBookmarksMod_ExtractStringValue_System_String_System_String(json, il2cpp_utils::newcsstr("n"));
    goto label_12;
    label_12:;
    return local0;
}

static System::Nullable_1<float> PracticeBookmarks_PracticeBookmarksMod_ExtractBookmarkSeconds_System_String_System_Single(::StringW json, float beatsPerMinute) {
    System::Nullable_1<float> beatTime;
    bool local1{};
    bool local2{};
    System::Nullable_1<float> local3;
    System::Nullable_1<float> local4;
    
    beatTime = PracticeBookmarks_PracticeBookmarksMod_ExtractFloatValue_System_String_System_String(json, il2cpp_utils::newcsstr("_time"));
    local1 = (beatTime.get_HasValue() == 0);
    if (!(local1)) goto label_16;
    beatTime = PracticeBookmarks_PracticeBookmarksMod_ExtractFloatValue_System_String_System_String(json, il2cpp_utils::newcsstr("b"));
    label_16:;
    local2 = (beatTime.get_HasValue() == 0);
    if (!(local2)) goto label_28;
    local3 = {};
    local4 = local3;
    goto label_35;
    label_28:;
    local4 = [&]() { System::Nullable_1<float> tmpValue{}; tmpValue._ctor(PracticeBookmarks_PracticeBookmarksMod_BeatsToSeconds_System_Single_System_Single(beatsPerMinute, beatTime.get_Value())); return tmpValue; }();
    goto label_35;
    label_35:;
    return local4;
}

static ::StringW PracticeBookmarks_PracticeBookmarksMod_TryResolveCustomLevelFolder_System_String(::StringW levelId) {
    ::StringW token{};
    ArrayW<::StringW> candidateRoots{};
    bool local2{};
    ::StringW local3{};
    int32_t i{};
    ::StringW root{};
    ::StringW directHit{};
    bool local7{};
    bool local8{};
    bool local9{};
    ArrayW<::StringW> dupTemp0{};
    
    token = PracticeBookmarks_PracticeBookmarksMod_ExtractCustomLevelToken_System_String(levelId);
    local2 = ::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", token);
    if (!(local2)) goto label_12;
    local3 = nullptr;
    goto label_88;
    label_12:;
    dupTemp0 = ArrayW<::StringW>(4);
    dupTemp0[0] = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System.IO", "Path"), "Combine", GlobalNamespace::FileUtility::GetPlatformPersistentDataPath(0), il2cpp_utils::newcsstr("CustomLevels"));
    dupTemp0[1] = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System.IO", "Path"), "Combine", GlobalNamespace::FileUtility::GetPlatformPersistentDataPath(1), il2cpp_utils::newcsstr("CustomLevels"));
    dupTemp0[2] = il2cpp_utils::newcsstr("/sdcard/ModData/com.beatgames.beatsaber/Mods/SongCore/CustomLevels");
    dupTemp0[3] = il2cpp_utils::newcsstr("/sdcard/ModData/com.beatgames.beatsaber/Mods/SongLoader/CustomLevels");
    candidateRoots = dupTemp0;
    i = 0;
    goto label_77;
    label_40:;
    root = candidateRoots[i];
    if (::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", root)) goto label_53;
    goto label_54;
    label_53:;
    label_54:;
    local7 = 1;
    if (!(local7)) goto label_58;
    goto label_73;
    label_58:;
    directHit = PracticeBookmarks_PracticeBookmarksMod_TryFindDirectoryContaining_System_String_System_String(root, token);
    local8 = (::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", directHit) == 0);
    if (!(local8)) goto label_72;
    local3 = directHit;
    goto label_88;
    label_72:;
    label_73:;
    i = (i + 1);
    label_77:;
    local9 = (i < static_cast<int32_t>(static_cast<int>(candidateRoots.size())));
    if (local9) goto label_40;
    local3 = nullptr;
    goto label_88;
    label_88:;
    return local3;
}

static ::StringW PracticeBookmarks_PracticeBookmarksMod_TryFindDirectoryContaining_System_String_System_String(::StringW root, ::StringW token) {
    ArrayW<::StringW> directories{};
    int32_t i{};
    ::StringW directory{};
    ::StringW name{};
    bool local4{};
    ::StringW local5{};
    bool local6{};
    
    directories = ::il2cpp_utils::RunMethodRethrow<ArrayW<::StringW>, false>(::il2cpp_utils::GetClassFromName("System.IO", "Directory"), "GetDirectories", root);
    i = 0;
    goto label_39;
    label_7:;
    directory = directories[i];
    name = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System.IO", "Path"), "GetFileName", directory);
    if (::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System", "String"), "IsNullOrEmpty", name)) goto label_27;
    goto label_28;
    label_27:;
    label_28:;
    local4 = 0;
    if (!(local4)) goto label_34;
    local5 = directory;
    goto label_50;
    label_34:;
    i = (i + 1);
    label_39:;
    local6 = (i < static_cast<int32_t>(static_cast<int>(directories.size())));
    if (local6) goto label_7;
    local5 = nullptr;
    goto label_50;
    label_50:;
    return local5;
}

static ::StringW PracticeBookmarks_PracticeBookmarksMod_FindInfoFile_System_String(::StringW levelFolder) {
    ::StringW infoDat{};
    ::StringW lowerInfoDat{};
    bool local2{};
    ::StringW local3{};
    bool local4{};
    
    infoDat = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System.IO", "Path"), "Combine", levelFolder, il2cpp_utils::newcsstr("Info.dat"));
    local2 = ::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System.IO", "File"), "Exists", infoDat);
    if (!(local2)) goto label_13;
    local3 = infoDat;
    goto label_28;
    label_13:;
    lowerInfoDat = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System.IO", "Path"), "Combine", levelFolder, il2cpp_utils::newcsstr("info.dat"));
    local4 = ::il2cpp_utils::RunMethodRethrow<bool, false>(::il2cpp_utils::GetClassFromName("System.IO", "File"), "Exists", lowerInfoDat);
    if (!(local4)) goto label_25;
    local3 = lowerInfoDat;
    goto label_28;
    label_25:;
    local3 = nullptr;
    goto label_28;
    label_28:;
    return local3;
}

static void PracticeBookmarks_PracticeBookmarksMod_UpdateBookmarkLabel_GlobalNamespace_PracticeViewController_System_Int32(GlobalNamespace::PracticeViewController* self, int32_t bookmarkIndex) {
    bool local0{};
    
    if ((bookmarkIndex < 0)) goto label_9;
    goto label_10;
    label_9:;
    label_10:;
    local0 = 1;
    if (!(local0)) goto label_14;
    goto label_22;
    label_14:;
    self->____value->set_text(::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System", "String"), "Concat", BookmarkPrefix, PracticeBookmarks_PracticeBookmarksMod_GetBookmarkName_System_Int32(bookmarkIndex)));
    label_22:;
    return;
}

static int32_t PracticeBookmarks_PracticeBookmarksMod_FindNearestBookmarkIndex_System_Single_System_Single(float time, float windowSeconds) {
    int32_t bookmarkCount{};
    int32_t nearestIndex{};
    float nearestDistance{};
    bool local3{};
    int32_t local4{};
    int32_t i{};
    float distance{};
    bool local7{};
    bool local8{};
    
    bookmarkCount = PracticeBookmarks_PracticeBookmarksMod_GetBookmarkCount();
    local3 = (bookmarkCount == 0);
    if (!(local3)) goto label_12;
    local4 = -1;
    goto label_51;
    label_12:;
    nearestIndex = -1;
    nearestDistance = windowSeconds;
    i = 0;
    goto label_42;
    label_19:;
    distance = PracticeBookmarks_PracticeBookmarksMod_Abs_System_Single((PracticeBookmarks_PracticeBookmarksMod_GetBookmarkTime_System_Int32(i) - time));
    local7 = (distance > nearestDistance);
    if (!(local7)) goto label_33;
    goto label_38;
    label_33:;
    nearestIndex = i;
    nearestDistance = distance;
    label_38:;
    i = (i + 1);
    label_42:;
    local8 = (i < bookmarkCount);
    if (local8) goto label_19;
    local4 = nearestIndex;
    goto label_51;
    label_51:;
    return local4;
}

static float PracticeBookmarks_PracticeBookmarksMod_GetCurrentPracticeTime_GlobalNamespace_PracticeViewController(GlobalNamespace::PracticeViewController* self) {
    bool local0{};
    float local1{};
    
    local0 = (self->____practiceSettings != nullptr);
    if (!(local0)) goto label_13;
    local1 = self->____practiceSettings->____startSongTime;
    goto label_16;
    label_13:;
    local1 = 0;
    goto label_16;
    label_16:;
    return local1;
}

static float PracticeBookmarks_PracticeBookmarksMod_BeatsToSeconds_System_Single_System_Single(float beatsPerMinute, float beat) {
    bool local0{};
    float local1{};
    
    local0 = ((beatsPerMinute > 0) == 0);
    if (!(local0)) goto label_12;
    local1 = beat;
    goto label_19;
    label_12:;
    local1 = ((60 / beatsPerMinute) * beat);
    goto label_19;
    label_19:;
    return local1;
}

static float PracticeBookmarks_PracticeBookmarksMod_Abs_System_Single(float value) {
    float local0{};
    
    if ((value < 0)) goto label_6;
    goto label_8;
    label_6:;
    label_8:;
    local0 = (-value);
    goto label_10;
    label_10:;
    return local0;
}

static ::StringW PracticeBookmarks_PracticeBookmarksMod_ExtractCustomLevelToken_System_String(::StringW levelId) {
    int32_t index{};
    bool local1{};
    ::StringW local2{};
    ::StringW local3{};
    int32_t local4{};
    
    index = ::il2cpp_utils::RunMethodRethrow<int32_t, false>(levelId, "IndexOf", il2cpp_utils::newcsstr("custom_level_"), 5);
    local1 = (index < 0);
    if (!(local1)) goto label_15;
    local2 = nullptr;
    goto label_31;
    label_15:;
    local3 = levelId;
    local4 = (index + ::il2cpp_utils::RunMethodRethrow<int32_t, false>(il2cpp_utils::newcsstr("custom_level_"), "get_Length"));
    local2 = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(local3, "Substring", local4, (::il2cpp_utils::RunMethodRethrow<int32_t, false>(local3, "get_Length") - local4));
    goto label_31;
    label_31:;
    return local2;
}

static ::StringW PracticeBookmarks_PracticeBookmarksMod_TryExtractArray_System_String_System_String(::StringW json, ::StringW key) {
    int32_t keyIndex{};
    int32_t arrayStart{};
    int32_t arrayEnd{};
    bool local3{};
    ::StringW local4{};
    bool local5{};
    bool local6{};
    
    keyIndex = PracticeBookmarks_PracticeBookmarksMod_FindKeyIndex_System_String_System_String(json, key);
    local3 = (keyIndex < 0);
    if (!(local3)) goto label_14;
    local4 = nullptr;
    goto label_55;
    label_14:;
    arrayStart = ::il2cpp_utils::RunMethodRethrow<int32_t, false>(json, "IndexOf", 91, keyIndex);
    local5 = (arrayStart < 0);
    if (!(local5)) goto label_28;
    local4 = nullptr;
    goto label_55;
    label_28:;
    arrayEnd = PracticeBookmarks_PracticeBookmarksMod_FindMatchingToken_System_String_System_Int32_System_Char_System_Char(json, arrayStart, 91, 93);
    local6 = (arrayEnd < 0);
    if (!(local6)) goto label_43;
    local4 = nullptr;
    goto label_55;
    label_43:;
    local4 = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(json, "Substring", (arrayStart + 1), ((arrayEnd - arrayStart) - 1));
    goto label_55;
    label_55:;
    return local4;
}

static System::Collections::Generic::List_1<::StringW>* PracticeBookmarks_PracticeBookmarksMod_SplitTopLevelObjects_System_String(::StringW arrayContent) {
    System::Collections::Generic::List_1<::StringW>* result{};
    int32_t depth{};
    int32_t objectStart{};
    bool inString{};
    bool escaped{};
    int32_t i{};
    Il2CppChar current{};
    bool local7{};
    bool local8{};
    bool local9{};
    bool local10{};
    bool local11{};
    bool local12{};
    bool local13{};
    bool local14{};
    bool local15{};
    bool local16{};
    System::Collections::Generic::List_1<::StringW>* local17{};
    
    result = ::il2cpp_utils::NewSpecific<decltype(result)>();
    depth = 0;
    objectStart = -1;
    inString = 0;
    escaped = 0;
    i = 0;
    goto label_130;
    label_14:;
    current = ::il2cpp_utils::RunMethodRethrow<Il2CppChar, false>(arrayContent, "get_Chars", i);
    local7 = inString;
    if (!(local7)) goto label_55;
    local8 = escaped;
    if (!(local8)) goto label_33;
    escaped = 0;
    goto label_54;
    label_33:;
    local9 = (current == 92);
    if (!(local9)) goto label_44;
    escaped = 1;
    goto label_54;
    label_44:;
    local10 = (current == 34);
    if (!(local10)) goto label_54;
    inString = 0;
    label_54:;
    goto label_126;
    label_55:;
    local11 = (current == 34);
    if (!(local11)) goto label_65;
    inString = 1;
    goto label_126;
    label_65:;
    local12 = (current == 123);
    if (!(local12)) goto label_85;
    local13 = (depth == 0);
    if (!(local13)) goto label_80;
    objectStart = i;
    label_80:;
    depth = (depth + 1);
    goto label_126;
    label_85:;
    local14 = ((current == 125) == 0);
    if (!(local14)) goto label_94;
    goto label_126;
    label_94:;
    depth = (depth - 1);
    if (depth) goto label_106;
    goto label_107;
    label_106:;
    label_107:;
    local15 = 0;
    if (!(local15)) goto label_125;
    ::il2cpp_utils::RunMethodRethrow<void, false>(result, "Add", ::il2cpp_utils::RunMethodRethrow<::StringW, false>(arrayContent, "Substring", objectStart, ((i - objectStart) + 1)));
    objectStart = -1;
    label_125:;
    label_126:;
    i = (i + 1);
    label_130:;
    local16 = (i < ::il2cpp_utils::RunMethodRethrow<int32_t, false>(arrayContent, "get_Length"));
    if (local16) goto label_14;
    local17 = result;
    goto label_140;
    label_140:;
    return local17;
}

static ::StringW PracticeBookmarks_PracticeBookmarksMod_ExtractStringValue_System_String_System_String(::StringW json, ::StringW key) {
    int32_t keyIndex{};
    int32_t colonIndex{};
    int32_t valueStart{};
    bool escaped{};
    bool local4{};
    ::StringW local5{};
    bool local6{};
    bool local7{};
    int32_t i{};
    Il2CppChar current{};
    bool local10{};
    bool local11{};
    bool local12{};
    bool local13{};
    
    keyIndex = PracticeBookmarks_PracticeBookmarksMod_FindKeyIndex_System_String_System_String(json, key);
    local4 = (keyIndex < 0);
    if (!(local4)) goto label_14;
    local5 = nullptr;
    goto label_109;
    label_14:;
    colonIndex = ::il2cpp_utils::RunMethodRethrow<int32_t, false>(json, "IndexOf", 58, keyIndex);
    local6 = (colonIndex < 0);
    if (!(local6)) goto label_28;
    local5 = nullptr;
    goto label_109;
    label_28:;
    valueStart = ::il2cpp_utils::RunMethodRethrow<int32_t, false>(json, "IndexOf", 34, (colonIndex + 1));
    local7 = (valueStart < 0);
    if (!(local7)) goto label_44;
    local5 = nullptr;
    goto label_109;
    label_44:;
    escaped = 0;
    i = (valueStart + 1);
    goto label_99;
    label_51:;
    current = ::il2cpp_utils::RunMethodRethrow<Il2CppChar, false>(json, "get_Chars", i);
    local10 = escaped;
    if (!(local10)) goto label_64;
    escaped = 0;
    goto label_95;
    label_64:;
    local11 = (current == 92);
    if (!(local11)) goto label_74;
    escaped = 1;
    goto label_95;
    label_74:;
    local12 = ((current == 34) == 0);
    if (!(local12)) goto label_83;
    goto label_95;
    label_83:;
    local5 = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(json, "Substring", (valueStart + 1), ((i - valueStart) - 1));
    goto label_109;
    label_95:;
    i = (i + 1);
    label_99:;
    local13 = (i < ::il2cpp_utils::RunMethodRethrow<int32_t, false>(json, "get_Length"));
    if (local13) goto label_51;
    local5 = nullptr;
    goto label_109;
    label_109:;
    return local5;
}

static System::Nullable_1<float> PracticeBookmarks_PracticeBookmarksMod_ExtractFloatValue_System_String_System_String(::StringW json, ::StringW key) {
    int32_t keyIndex{};
    int32_t colonIndex{};
    int32_t start{};
    int32_t end{};
    ::StringW valueText{};
    bool local5{};
    System::Nullable_1<float> local6;
    System::Nullable_1<float> local7;
    bool local8{};
    bool local9{};
    Il2CppChar current{};
    bool isNumeric{};
    bool local12{};
    bool local13{};
    bool local14{};
    
    keyIndex = PracticeBookmarks_PracticeBookmarksMod_FindKeyIndex_System_String_System_String(json, key);
    local5 = (keyIndex < 0);
    if (!(local5)) goto label_16;
    local6 = {};
    local7 = local6;
    goto label_125;
    label_16:;
    colonIndex = ::il2cpp_utils::RunMethodRethrow<int32_t, false>(json, "IndexOf", 58, keyIndex);
    local8 = (colonIndex < 0);
    if (!(local8)) goto label_32;
    local6 = {};
    local7 = local6;
    goto label_125;
    label_32:;
    start = (colonIndex + 1);
    goto label_41;
    label_37:;
    start = (start + 1);
    label_41:;
    if ((start >= ::il2cpp_utils::RunMethodRethrow<int32_t, false>(json, "get_Length"))) goto label_50;
    goto label_51;
    label_50:;
    label_51:;
    local9 = 0;
    if (local9) goto label_37;
    end = start;
    goto label_92;
    label_57:;
    current = ::il2cpp_utils::RunMethodRethrow<Il2CppChar, false>(json, "get_Chars", end);
    if ((current < 48)) goto label_68;
    if ((current <= 57)) goto label_78;
    label_68:;
    if ((current == 45)) goto label_78;
    if ((current == 43)) goto label_78;
    goto label_79;
    label_78:;
    label_79:;
    isNumeric = 1;
    local12 = (isNumeric == 0);
    if (!(local12)) goto label_87;
    goto label_99;
    label_87:;
    end = (end + 1);
    label_92:;
    local13 = (end < ::il2cpp_utils::RunMethodRethrow<int32_t, false>(json, "get_Length"));
    if (local13) goto label_57;
    label_99:;
    local14 = ((end > start) == 0);
    if (!(local14)) goto label_112;
    local6 = {};
    local7 = local6;
    goto label_125;
    label_112:;
    valueText = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(json, "Substring", start, (end - start));
    local7 = [&]() { System::Nullable_1<float> tmpValue{}; tmpValue._ctor(::il2cpp_utils::RunMethodRethrow<float, false>(::il2cpp_utils::GetClassFromName("System", "Single"), "Parse", valueText, ::il2cpp_utils::RunMethodRethrow<System::Globalization::CultureInfo*, false>(::il2cpp_utils::GetClassFromName("System.Globalization", "CultureInfo"), "get_InvariantCulture"))); return tmpValue; }();
    goto label_125;
    label_125:;
    return local7;
}

static int32_t PracticeBookmarks_PracticeBookmarksMod_FindKeyIndex_System_String_System_String(::StringW json, ::StringW key) {
    int32_t local0{};
    
    local0 = ::il2cpp_utils::RunMethodRethrow<int32_t, false>(json, "IndexOf", ::il2cpp_utils::RunMethodRethrow<::StringW, false>(::il2cpp_utils::GetClassFromName("System", "String"), "Concat", il2cpp_utils::newcsstr("\""), key, il2cpp_utils::newcsstr("\"")), 4);
    goto label_10;
    label_10:;
    return local0;
}

static int32_t PracticeBookmarks_PracticeBookmarksMod_FindMatchingToken_System_String_System_Int32_System_Char_System_Char(::StringW text, int32_t startIndex, Il2CppChar openToken, Il2CppChar closeToken) {
    int32_t depth{};
    bool inString{};
    bool escaped{};
    int32_t i{};
    Il2CppChar current{};
    bool local5{};
    bool local6{};
    bool local7{};
    bool local8{};
    bool local9{};
    bool local10{};
    bool local11{};
    bool local12{};
    int32_t local13{};
    bool local14{};
    
    depth = 0;
    inString = 0;
    escaped = 0;
    i = startIndex;
    goto label_96;
    label_10:;
    current = ::il2cpp_utils::RunMethodRethrow<Il2CppChar, false>(text, "get_Chars", i);
    local5 = inString;
    if (!(local5)) goto label_51;
    local6 = escaped;
    if (!(local6)) goto label_29;
    escaped = 0;
    goto label_50;
    label_29:;
    local7 = (current == 92);
    if (!(local7)) goto label_40;
    escaped = 1;
    goto label_50;
    label_40:;
    local8 = (current == 34);
    if (!(local8)) goto label_50;
    inString = 0;
    label_50:;
    goto label_92;
    label_51:;
    local9 = (current == 34);
    if (!(local9)) goto label_61;
    inString = 1;
    goto label_92;
    label_61:;
    local10 = (current == openToken);
    if (!(local10)) goto label_72;
    depth = (depth + 1);
    goto label_82;
    label_72:;
    local11 = (current == closeToken);
    if (!(local11)) goto label_82;
    depth = (depth - 1);
    label_82:;
    local12 = (depth == 0);
    if (!(local12)) goto label_91;
    local13 = i;
    goto label_106;
    label_91:;
    label_92:;
    i = (i + 1);
    label_96:;
    local14 = (i < ::il2cpp_utils::RunMethodRethrow<int32_t, false>(text, "get_Length"));
    if (local14) goto label_10;
    local13 = -1;
    goto label_106;
    label_106:;
    return local13;
}

static void PracticeBookmarks_PracticeBookmarksMod_EnsureBookmarkStorage() {
    if (PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkNames) goto label_5;
    PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkNames = ::il2cpp_utils::NewSpecific<decltype(PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkNames)>();
    label_5:;
    if (PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkTimes) goto label_9;
    PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkTimes = ::il2cpp_utils::NewSpecific<decltype(PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkTimes)>();
    label_9:;
    return;
}

static void PracticeBookmarks_PracticeBookmarksMod_ClearBookmarks() {
    PracticeBookmarks_PracticeBookmarksMod_EnsureBookmarkStorage();
    ::il2cpp_utils::RunMethodRethrow<void, false>(PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkNames, "Clear");
    ::il2cpp_utils::RunMethodRethrow<void, false>(PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkTimes, "Clear");
    return;
}

static void PracticeBookmarks_PracticeBookmarksMod_AddBookmark_System_String_System_Single(::StringW name, float time) {
    PracticeBookmarks_PracticeBookmarksMod_EnsureBookmarkStorage();
    ::il2cpp_utils::RunMethodRethrow<void, false>(PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkNames, "Add", name);
    ::il2cpp_utils::RunMethodRethrow<void, false>(PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkTimes, "Add", time);
    return;
}

static int32_t PracticeBookmarks_PracticeBookmarksMod_GetBookmarkCount() {
    int32_t local0{};
    
    PracticeBookmarks_PracticeBookmarksMod_EnsureBookmarkStorage();
    local0 = ::il2cpp_utils::RunMethodRethrow<int32_t, false>(PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkTimes, "get_Count");
    goto label_7;
    label_7:;
    return local0;
}

static ::StringW PracticeBookmarks_PracticeBookmarksMod_GetBookmarkName_System_Int32(int32_t index) {
    ::StringW local0{};
    
    PracticeBookmarks_PracticeBookmarksMod_EnsureBookmarkStorage();
    local0 = ::il2cpp_utils::RunMethodRethrow<::StringW, false>(PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkNames, "get_Item", index);
    goto label_8;
    label_8:;
    return local0;
}

static float PracticeBookmarks_PracticeBookmarksMod_GetBookmarkTime_System_Int32(int32_t index) {
    float local0{};
    
    PracticeBookmarks_PracticeBookmarksMod_EnsureBookmarkStorage();
    local0 = ::il2cpp_utils::RunMethodRethrow<float, false>(PracticeBookmarks_PracticeBookmarksMod_ActiveBookmarkTimes, "get_Item", index);
    goto label_8;
    label_8:;
    return local0;
}

static void OnPracticeDidActivatePostfix(GlobalNamespace::PracticeViewController* self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) {
    bool local0{};
    
    if ((Enabled == 0)) {
        return;
    }
    PracticeBookmarks_PracticeBookmarksMod_LoadBookmarks_GlobalNamespace_PracticeViewController(self);
    PracticeBookmarks_PracticeBookmarksMod_UpdateBookmarkLabel_GlobalNamespace_PracticeViewController_System_Int32(self, PracticeBookmarks_PracticeBookmarksMod_FindNearestBookmarkIndex_System_Single_System_Single(PracticeBookmarks_PracticeBookmarksMod_GetCurrentPracticeTime_GlobalNamespace_PracticeViewController(self), SnapWindowSeconds));
}

MAKE_HOOK_MATCH(
    GlobalNamespace_PracticeViewController_DidActivate_GlobalNamespace_PracticeViewController_System_Boolean_System_Boolean_System_Boolean_Hook,
    &GlobalNamespace::PracticeViewController::DidActivate,
    void,
    GlobalNamespace::PracticeViewController* self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) {
    GlobalNamespace_PracticeViewController_DidActivate_GlobalNamespace_PracticeViewController_System_Boolean_System_Boolean_System_Boolean_Hook(self, firstActivation, addedToHierarchy, screenSystemEnabling);
    OnPracticeDidActivatePostfix(self, firstActivation, addedToHierarchy, screenSystemEnabling);
    return;
}

static void OnSongStartSliderChangedPostfix(GlobalNamespace::PracticeViewController* self, HMUI::RangeValuesTextSlider* slider, float value) {
    int32_t bookmarkIndex{};
    float snappedTime{};
    bool local2{};
    bool local3{};
    bool local4{};
    bool local5{};
    bool local6{};
    bool local7{};
    bool local8{};
    
    if ((Enabled == 0)) {
        return;
    }
    if ((PracticeBookmarks_PracticeBookmarksMod_GetBookmarkCount() == 0)) {
        return;
    }
    if (PracticeBookmarks_PracticeBookmarksMod__isApplyingSnap) {
        return;
    }
    bookmarkIndex = PracticeBookmarks_PracticeBookmarksMod_FindNearestBookmarkIndex_System_Single_System_Single(value, SnapWindowSeconds);
    if ((bookmarkIndex < 0)) {
        return;
    }
    snappedTime = PracticeBookmarks_PracticeBookmarksMod_GetBookmarkTime_System_Int32(bookmarkIndex);
    if ((SnapToBookmarks == 0)) {
        PracticeBookmarks_PracticeBookmarksMod_UpdateBookmarkLabel_GlobalNamespace_PracticeViewController_System_Int32(self, bookmarkIndex);
    }
    else {
        if (((value == snappedTime) == 0)) {
            PracticeBookmarks_PracticeBookmarksMod__isApplyingSnap = 1;
            self->____practiceSettings->____startSongTime = snappedTime;
            if ((self->____songStartSlider != nullptr)) {
                self->____songStartSlider->SetNormalizedValue(self->____songStartSlider->NormalizeValue(snappedTime), 1);
            }
            PracticeBookmarks_PracticeBookmarksMod__isApplyingSnap = 0;
        }
        PracticeBookmarks_PracticeBookmarksMod_UpdateBookmarkLabel_GlobalNamespace_PracticeViewController_System_Int32(self, bookmarkIndex);
    }
}

MAKE_HOOK_MATCH(
    GlobalNamespace_PracticeViewController_HandleSongStartSliderValueDidChange_GlobalNamespace_PracticeViewController_HMUI_RangeValuesTextSlider_System_Single_Hook,
    &GlobalNamespace::PracticeViewController::HandleSongStartSliderValueDidChange,
    void,
    GlobalNamespace::PracticeViewController* self, HMUI::RangeValuesTextSlider* slider, float value) {
    GlobalNamespace_PracticeViewController_HandleSongStartSliderValueDidChange_GlobalNamespace_PracticeViewController_HMUI_RangeValuesTextSlider_System_Single_Hook(self, slider, value);
    OnSongStartSliderChangedPostfix(self, slider, value);
    return;
}

static void OnPlayButtonPressedPostfix(GlobalNamespace::PracticeViewController* self) {
    int32_t bookmarkIndex{};
    bool local1{};
    bool local2{};
    bool local3{};
    bool local4{};
    
    if ((Enabled == 0)) {
        return;
    }
    if ((SnapToBookmarks == 0)) {
        return;
    }
    if ((PracticeBookmarks_PracticeBookmarksMod_GetBookmarkCount() == 0)) {
        return;
    }
    bookmarkIndex = PracticeBookmarks_PracticeBookmarksMod_FindNearestBookmarkIndex_System_Single_System_Single(self->____practiceSettings->____startSongTime, SnapWindowSeconds);
    if (((bookmarkIndex < 0) == 0)) {
        self->____practiceSettings->____startSongTime = PracticeBookmarks_PracticeBookmarksMod_GetBookmarkTime_System_Int32(bookmarkIndex);
    }
}

MAKE_HOOK_MATCH(
    GlobalNamespace_PracticeViewController_HandlePlayButtonPressed_GlobalNamespace_PracticeViewController_Hook,
    &GlobalNamespace::PracticeViewController::HandlePlayButtonPressed,
    void,
    GlobalNamespace::PracticeViewController* self) {
    GlobalNamespace_PracticeViewController_HandlePlayButtonPressed_GlobalNamespace_PracticeViewController_Hook(self);
    OnPlayButtonPressedPostfix(self);
    return;
}

static void OnPracticeDidDeactivatePostfix(GlobalNamespace::PracticeViewController* self, bool removedFromHierarchy, bool screenSystemDisabling) {
    PracticeBookmarks_PracticeBookmarksMod_ClearBookmarks();
    PracticeBookmarks_PracticeBookmarksMod__activeLevelId = nullptr;
    PracticeBookmarks_PracticeBookmarksMod__isApplyingSnap = 0;
}

MAKE_HOOK_MATCH(
    GlobalNamespace_PracticeViewController_DidDeactivate_GlobalNamespace_PracticeViewController_System_Boolean_System_Boolean_Hook,
    &GlobalNamespace::PracticeViewController::DidDeactivate,
    void,
    GlobalNamespace::PracticeViewController* self, bool removedFromHierarchy, bool screenSystemDisabling) {
    GlobalNamespace_PracticeViewController_DidDeactivate_GlobalNamespace_PracticeViewController_System_Boolean_System_Boolean_Hook(self, removedFromHierarchy, screenSystemDisabling);
    OnPracticeDidDeactivatePostfix(self, removedFromHierarchy, screenSystemDisabling);
    return;
}

MOD_EXTERN_FUNC void late_load() noexcept {
    il2cpp_functions::Init();
    PaperLogger.info("Installing hooks...");

    INSTALL_HOOK(PaperLogger, GlobalNamespace_PracticeViewController_DidActivate_GlobalNamespace_PracticeViewController_System_Boolean_System_Boolean_System_Boolean_Hook);
    INSTALL_HOOK(PaperLogger, GlobalNamespace_PracticeViewController_HandleSongStartSliderValueDidChange_GlobalNamespace_PracticeViewController_HMUI_RangeValuesTextSlider_System_Single_Hook);
    INSTALL_HOOK(PaperLogger, GlobalNamespace_PracticeViewController_HandlePlayButtonPressed_GlobalNamespace_PracticeViewController_Hook);
    INSTALL_HOOK(PaperLogger, GlobalNamespace_PracticeViewController_DidDeactivate_GlobalNamespace_PracticeViewController_System_Boolean_System_Boolean_Hook);

    PaperLogger.info("Installed all hooks!");
}
