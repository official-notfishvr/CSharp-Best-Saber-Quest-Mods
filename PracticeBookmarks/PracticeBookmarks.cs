using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CoreMod;
using GlobalNamespace;

namespace PracticeBookmarks;

[Mod("com.csharp.quest.practicebookmarks", "0.1.0")]
public static class PracticeBookmarksMod
{
    private const string CustomLevelPrefix = "custom_level_";

    private static List<string>? ActiveBookmarkNames;
    private static List<float>? ActiveBookmarkTimes;

    private static string? _activeLevelId;
    private static bool _isApplyingSnap;

    [Config(Description = "Enable practice bookmarks on Quest", DefaultValue = true)]
    public static bool Enabled { get; set; } = true;

    [Config(Description = "Snap the practice slider to nearby bookmarks", DefaultValue = true)]
    public static bool SnapToBookmarks { get; set; } = true;

    [Config(Description = "Maximum distance in seconds before snapping to a bookmark", DefaultValue = 0.75f)]
    public static float SnapWindowSeconds { get; set; } = 0.75f;

    [Config(Description = "Prefix shown when the slider is near a bookmark", DefaultValue = "Bookmark: ")]
    public static string BookmarkPrefix { get; set; } = "Bookmark: ";

    [Hook(typeof(PracticeViewController), nameof(PracticeViewController.DidActivate), Phase = HookPhase.Postfix)]
    public static void OnPracticeDidActivatePostfix(PracticeViewController self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (!Enabled)
            return;

        LoadBookmarks(self);
        UpdateBookmarkLabel(self, FindNearestBookmarkIndex(GetCurrentPracticeTime(self), SnapWindowSeconds));
    }

    [Hook(typeof(PracticeViewController), nameof(PracticeViewController.HandleSongStartSliderValueDidChange), Phase = HookPhase.Postfix)]
    public static void OnSongStartSliderChangedPostfix(PracticeViewController self, HMUI.RangeValuesTextSlider slider, float value)
    {
        if (!Enabled)
            return;

        if (GetBookmarkCount() == 0)
            return;

        if (_isApplyingSnap)
            return;

        var bookmarkIndex = FindNearestBookmarkIndex(value, SnapWindowSeconds);
        if (bookmarkIndex < 0)
            return;

        var snappedTime = GetBookmarkTime(bookmarkIndex);
        if (!SnapToBookmarks)
        {
            UpdateBookmarkLabel(self, bookmarkIndex);
            return;
        }

        if (value != snappedTime)
        {
            _isApplyingSnap = true;
            self._practiceSettings._startSongTime = snappedTime;
            if (self._songStartSlider != null)
                self._songStartSlider.SetNormalizedValue(self._songStartSlider.NormalizeValue(snappedTime), true);
            _isApplyingSnap = false;
        }

        UpdateBookmarkLabel(self, bookmarkIndex);
    }

    [Hook(typeof(PracticeViewController), nameof(PracticeViewController.HandlePlayButtonPressed), Phase = HookPhase.Postfix)]
    public static void OnPlayButtonPressedPostfix(PracticeViewController self)
    {
        if (!Enabled)
            return;

        if (!SnapToBookmarks)
            return;

        if (GetBookmarkCount() == 0)
            return;

        var bookmarkIndex = FindNearestBookmarkIndex(self._practiceSettings._startSongTime, SnapWindowSeconds);
        if (bookmarkIndex >= 0)
            self._practiceSettings._startSongTime = GetBookmarkTime(bookmarkIndex);
    }

    [Hook(typeof(PracticeViewController), nameof(PracticeViewController.DidDeactivate), Phase = HookPhase.Postfix)]
    public static void OnPracticeDidDeactivatePostfix(PracticeViewController self, bool removedFromHierarchy, bool screenSystemDisabling)
    {
        ClearBookmarks();
        _activeLevelId = null;
        _isApplyingSnap = false;
    }

    private static void LoadBookmarks(PracticeViewController self)
    {
        ClearBookmarks();

        var level = self._beatmapLevel;
        if (level == null || string.IsNullOrEmpty(level.levelID))
            return;

        _activeLevelId = level.levelID;

        var customLevelFolder = TryResolveCustomLevelFolder(level.levelID);
        if (string.IsNullOrEmpty(customLevelFolder))
            return;

        var infoPath = FindInfoFile(customLevelFolder);
        if (string.IsNullOrEmpty(infoPath))
            return;

        LoadBookmarksFromInfoFile(infoPath, level.beatsPerMinute);
    }

    private static void LoadBookmarksFromInfoFile(string infoPath, float beatsPerMinute)
    {
        var infoJson = File.ReadAllText(infoPath);
        var difficultySets = TryExtractArray(infoJson, "_difficultyBeatmapSets") ?? TryExtractArray(infoJson, "difficultyBeatmapSets");
        if (string.IsNullOrEmpty(difficultySets))
            return;

        var setObjects = SplitTopLevelObjects(difficultySets);
        for (var setIndex = 0; setIndex < setObjects.Count; setIndex++)
        {
            var setJson = setObjects[setIndex];
            var difficultyArray = TryExtractArray(setJson, "_difficultyBeatmaps") ?? TryExtractArray(setJson, "difficultyBeatmaps");
            if (string.IsNullOrEmpty(difficultyArray))
                continue;

            var difficultyObjects = SplitTopLevelObjects(difficultyArray);
            for (var difficultyIndex = 0; difficultyIndex < difficultyObjects.Count; difficultyIndex++)
            {
                var filename = ExtractStringValue(difficultyObjects[difficultyIndex], "_beatmapFilename") ?? ExtractStringValue(difficultyObjects[difficultyIndex], "beatmapFilename");
                if (string.IsNullOrEmpty(filename))
                    continue;

                var levelFolder = Path.GetDirectoryName(infoPath);
                if (string.IsNullOrEmpty(levelFolder))
                    continue;

                var difficultyPath = Path.Combine(levelFolder, filename);
                if (File.Exists(difficultyPath))
                    LoadBookmarksFromDifficultyFile(difficultyPath, beatsPerMinute);
            }
        }
    }

    private static void LoadBookmarksFromDifficultyFile(string difficultyPath, float beatsPerMinute)
    {
        var json = File.ReadAllText(difficultyPath);
        var bookmarkArray = TryExtractArray(json, "_bookmarks") ?? TryExtractArray(json, "bookmarks");
        if (string.IsNullOrEmpty(bookmarkArray))
            return;

        var objects = SplitTopLevelObjects(bookmarkArray);
        for (var i = 0; i < objects.Count; i++)
        {
            var name = ExtractBookmarkName(objects[i]);
            if (string.IsNullOrEmpty(name))
                continue;

            var seconds = ExtractBookmarkSeconds(objects[i], beatsPerMinute);
            if (seconds == null)
                continue;

            AddBookmark(name, seconds.Value);
        }
    }

    private static string? ExtractBookmarkName(string json)
    {
        return ExtractStringValue(json, "_name") ?? ExtractStringValue(json, "n");
    }

    private static float? ExtractBookmarkSeconds(string json, float beatsPerMinute)
    {
        var beatTime = ExtractFloatValue(json, "_time");
        if (beatTime == null)
            beatTime = ExtractFloatValue(json, "b");

        if (beatTime == null)
            return null;

        return BeatsToSeconds(beatsPerMinute, beatTime.Value);
    }

    private static string? TryResolveCustomLevelFolder(string levelId)
    {
        var token = ExtractCustomLevelToken(levelId);
        if (string.IsNullOrEmpty(token))
            return null;

        var candidateRoots = new[]
        {
            Path.Combine(FileUtility.GetPlatformPersistentDataPath(false), "CustomLevels"),
            Path.Combine(FileUtility.GetPlatformPersistentDataPath(true), "CustomLevels"),
            "/sdcard/ModData/com.beatgames.beatsaber/Mods/SongCore/CustomLevels",
            "/sdcard/ModData/com.beatgames.beatsaber/Mods/SongLoader/CustomLevels",
        };

        for (var i = 0; i < candidateRoots.Length; i++)
        {
            var root = candidateRoots[i];
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                continue;

            var directHit = TryFindDirectoryContaining(root, token);
            if (!string.IsNullOrEmpty(directHit))
                return directHit;
        }

        return null;
    }

    private static string? TryFindDirectoryContaining(string root, string token)
    {
        var directories = Directory.GetDirectories(root);
        for (var i = 0; i < directories.Length; i++)
        {
            var directory = directories[i];
            var name = Path.GetFileName(directory);
            if (!string.IsNullOrEmpty(name) && name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return directory;
        }

        return null;
    }

    private static string? FindInfoFile(string levelFolder)
    {
        var infoDat = Path.Combine(levelFolder, "Info.dat");
        if (File.Exists(infoDat))
            return infoDat;

        var lowerInfoDat = Path.Combine(levelFolder, "info.dat");
        if (File.Exists(lowerInfoDat))
            return lowerInfoDat;

        return null;
    }

    private static void UpdateBookmarkLabel(PracticeViewController self, int bookmarkIndex)
    {
        if (bookmarkIndex < 0 || self._value == null)
            return;

        self._value.set_Text(BookmarkPrefix + GetBookmarkName(bookmarkIndex));
    }

    private static int FindNearestBookmarkIndex(float time, float windowSeconds)
    {
        var bookmarkCount = GetBookmarkCount();
        if (bookmarkCount == 0)
            return -1;

        var nearestIndex = -1;
        var nearestDistance = windowSeconds;

        for (var i = 0; i < bookmarkCount; i++)
        {
            var distance = Abs(GetBookmarkTime(i) - time);
            if (distance > nearestDistance)
                continue;

            nearestIndex = i;
            nearestDistance = distance;
        }

        return nearestIndex;
    }

    private static float GetCurrentPracticeTime(PracticeViewController self)
    {
        if (self._practiceSettings != null)
            return self._practiceSettings._startSongTime;

        return 0f;
    }

    private static float BeatsToSeconds(float beatsPerMinute, float beat)
    {
        if (beatsPerMinute <= 0f)
            return beat;

        return (60f / beatsPerMinute) * beat;
    }

    private static float Abs(float value)
    {
        return value < 0f ? -value : value;
    }

    private static string? ExtractCustomLevelToken(string levelId)
    {
        var index = levelId.IndexOf(CustomLevelPrefix, System.StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        return levelId[(index + CustomLevelPrefix.Length)..];
    }

    private static string? TryExtractArray(string json, string key)
    {
        var keyIndex = FindKeyIndex(json, key);
        if (keyIndex < 0)
            return null;

        var arrayStart = json.IndexOf('[', keyIndex);
        if (arrayStart < 0)
            return null;

        var arrayEnd = FindMatchingToken(json, arrayStart, '[', ']');
        if (arrayEnd < 0)
            return null;

        return json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
    }

    private static List<string> SplitTopLevelObjects(string arrayContent)
    {
        var result = new List<string>();
        var depth = 0;
        var objectStart = -1;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < arrayContent.Length; i++)
        {
            var current = arrayContent[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '{')
            {
                if (depth == 0)
                    objectStart = i;
                depth++;
                continue;
            }

            if (current != '}')
                continue;

            depth--;
            if (depth == 0 && objectStart >= 0)
            {
                result.Add(arrayContent.Substring(objectStart, i - objectStart + 1));
                objectStart = -1;
            }
        }

        return result;
    }

    private static string? ExtractStringValue(string json, string key)
    {
        var keyIndex = FindKeyIndex(json, key);
        if (keyIndex < 0)
            return null;

        var colonIndex = json.IndexOf(':', keyIndex);
        if (colonIndex < 0)
            return null;

        var valueStart = json.IndexOf('"', colonIndex + 1);
        if (valueStart < 0)
            return null;

        var escaped = false;
        for (var i = valueStart + 1; i < json.Length; i++)
        {
            var current = json[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (current == '\\')
            {
                escaped = true;
                continue;
            }

            if (current != '"')
                continue;

            return json.Substring(valueStart + 1, i - valueStart - 1);
        }

        return null;
    }

    private static float? ExtractFloatValue(string json, string key)
    {
        var keyIndex = FindKeyIndex(json, key);
        if (keyIndex < 0)
            return null;

        var colonIndex = json.IndexOf(':', keyIndex);
        if (colonIndex < 0)
            return null;

        var start = colonIndex + 1;
        while (start < json.Length && char.IsWhiteSpace(json[start]))
            start++;

        var end = start;
        while (end < json.Length)
        {
            var current = json[end];
            var isNumeric = (current >= '0' && current <= '9') || current == '-' || current == '+' || current == '.';
            if (!isNumeric)
                break;
            end++;
        }

        if (end <= start)
            return null;

        var valueText = json.Substring(start, end - start);
        return float.Parse(valueText, CultureInfo.InvariantCulture);
    }

    private static int FindKeyIndex(string json, string key)
    {
        return json.IndexOf("\"" + key + "\"", System.StringComparison.Ordinal);
    }

    private static int FindMatchingToken(string text, int startIndex, char openToken, char closeToken)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var current = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == openToken)
                depth++;
            else if (current == closeToken)
                depth--;

            if (depth == 0)
                return i;
        }

        return -1;
    }

    private static void EnsureBookmarkStorage()
    {
        ActiveBookmarkNames ??= new List<string>();
        ActiveBookmarkTimes ??= new List<float>();
    }

    private static void ClearBookmarks()
    {
        EnsureBookmarkStorage();
        ActiveBookmarkNames!.Clear();
        ActiveBookmarkTimes!.Clear();
    }

    private static void AddBookmark(string name, float time)
    {
        EnsureBookmarkStorage();
        ActiveBookmarkNames!.Add(name);
        ActiveBookmarkTimes!.Add(time);
    }

    private static int GetBookmarkCount()
    {
        EnsureBookmarkStorage();
        return ActiveBookmarkTimes!.Count;
    }

    private static string GetBookmarkName(int index)
    {
        EnsureBookmarkStorage();
        return ActiveBookmarkNames![index];
    }

    private static float GetBookmarkTime(int index)
    {
        EnsureBookmarkStorage();
        return ActiveBookmarkTimes![index];
    }
}
