using BGLib.Polyglot;
using HarmonyLib;
using HMUI;
using IPA.Utilities;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace BookmarkViewer.Patches
{
    internal static class BookmarkPatches
    {
        private sealed class Bookmark
        {
            public string Name = string.Empty;
            public float TimeInSeconds;
            public Color Color;
            public Graphic? Graphic;
        }

        private static readonly List<Graphic> GraphicsPool = new List<Graphic>();
        private static readonly List<Bookmark> Bookmarks = new List<Bookmark>();
        private static Graphic? _templateGraphic;
        private static CurvedTextMeshPro? _currentBookmarkText;
        private static Bookmark? _currentBookmark;
        private static Vector3 _bookmarkGraphicScale = Vector3.one;
        private static float _minX;
        private static float _maxX;
        private static int _requestVersion;
        private static bool _updatingSliderRange;

        private static float FindClosestTime(float target)
        {
            float closest = Bookmarks[0].TimeInSeconds;
            float minDifference = Math.Abs(target - closest);
            for (int i = 1; i < Bookmarks.Count; i++)
            {
                float time = Bookmarks[i].TimeInSeconds;
                float difference = Math.Abs(target - time);
                if (difference < minDifference)
                {
                    minDifference = difference;
                    closest = time;
                }
            }
            return closest;
        }

        private static void SelectBookmark(float value)
        {
            Bookmark? selected = Bookmarks.FindLast(bookmark => bookmark.TimeInSeconds <= value);
            if (ReferenceEquals(selected, _currentBookmark)) return;
            if (_currentBookmark?.Graphic != null)
            {
                _currentBookmark.Graphic.color = WithAlpha(_currentBookmark.Color, 0.7f);
                _currentBookmark.Graphic.transform.localScale = _bookmarkGraphicScale;
            }
            _currentBookmark = selected;
            if (selected?.Graphic != null)
            {
                selected.Graphic.color = WithAlpha(selected.Color, 0.9f);
                selected.Graphic.transform.localScale = Vector3.Scale(_bookmarkGraphicScale, new Vector3(1f, 1.15f, 1f));
            }
            if (_currentBookmarkText != null)
                _currentBookmarkText.text = selected?.Name ?? string.Empty;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        [HarmonyPatch(typeof(PracticeViewController), "HandleSongStartSliderValueDidChange")]
        private static class SliderChangedPatch
        {
            private static void Prefix(RangeValuesTextSlider slider, ref float value, BeatmapLevel ____beatmapLevel)
            {
                if (_updatingSliderRange || Config.Instance?.Enabled != true || Bookmarks.Count == 0 || ____beatmapLevel == null) return;
                if (Config.Instance.SnapToBookmark)
                {
                    float closest = FindClosestTime(value);
                    if (Math.Abs(value - closest) < ____beatmapLevel.songDuration / 100f && Math.Abs(value - closest) > 0.001f)
                    {
                        value = closest;
                        slider.value = closest;
                    }
                }
                SelectBookmark(value);
            }
        }

        [HarmonyPatch(typeof(PracticeViewController), "DidActivate")]
        private static class ActivatePatch
        {
            private static async void Postfix(PracticeViewController __instance, BeatmapLevel ____beatmapLevel, BeatmapKey ____beatmapKey,
                BeatmapLevelsModel ____beatmapLevelsModel, BeatmapLevelsEntitlementModel ____beatmapLevelsEntitlementModel)
            {
                int requestVersion = ++_requestVersion;
                GraphicsPool.RemoveAll(graphic => graphic == null);
                foreach (Graphic graphic in GraphicsPool)
                    graphic.gameObject.SetActive(false);
                Bookmarks.Clear();
                _currentBookmark = null;
                if (_currentBookmarkText != null) _currentBookmarkText.text = string.Empty;
                if (Config.Instance?.Enabled != true || ____beatmapLevel == null ||
                    string.IsNullOrEmpty(____beatmapKey.levelId) ||
                    !____beatmapKey.levelId.StartsWith("custom_level_", StringComparison.Ordinal)) return;

                try
                {
                    BeatmapLevelDataVersion version = await ____beatmapLevelsEntitlementModel.GetLevelDataVersionAsync(
                        ____beatmapKey.levelId, CancellationToken.None);
                    LoadBeatmapLevelDataResult result = await ____beatmapLevelsModel.LoadBeatmapLevelDataAsync(
                        ____beatmapKey.levelId, version, CancellationToken.None);
                    if (result.isError || result.beatmapLevelData == null)
                    {
                        Debug.LogWarning($"BookmarkViewer could not load level data: {result.errorMessage}");
                        return;
                    }
                    string? json = await result.beatmapLevelData.GetBeatmapStringAsync(____beatmapKey);
                    if (json == null || json.Length == 0) return;
                    if (!__instance || !__instance.isActiveAndEnabled || requestVersion != _requestVersion || Config.Instance?.Enabled != true) return;
                    ReadBookmarks(____beatmapLevel, json);
                    if (Bookmarks.Count == 0) return;

                    TimeSlider slider = __instance.GetField<TimeSlider, PracticeViewController>("_songStartSlider");
                    Graphic sliderGraphic = slider.GetField<Graphic, TextSlider>("_handleGraphic");
                    if (sliderGraphic == null) return;
                    if (_templateGraphic == null || _templateGraphic.transform.parent != sliderGraphic.transform.parent)
                    {
                        if (_templateGraphic != null) UnityEngine.Object.Destroy(_templateGraphic.gameObject);
                        foreach (Graphic graphic in GraphicsPool)
                            if (graphic != null) UnityEngine.Object.Destroy(graphic.gameObject);
                        GraphicsPool.Clear();
                        _templateGraphic = UnityEngine.Object.Instantiate(sliderGraphic, sliderGraphic.transform.parent);
                        _templateGraphic.gameObject.SetActive(false);
                    }
                    _templateGraphic.transform.rotation = Quaternion.Euler(0f, 0f, Config.Instance.UnskewBookmarks ? 5f : 0f);
                    SetupNameText(slider);
                    GetSliderRange(slider, sliderGraphic);
                    ShowBookmarks(sliderGraphic.transform, ____beatmapLevel);
                    SelectBookmark(slider.value - ____beatmapLevel.songDuration / 100f);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"BookmarkViewer could not load bookmarks: {exception}");
                }
            }

            private static void ReadBookmarks(BeatmapLevel level, string json)
            {
                if (level.beatsPerMinute <= 0f) return;
                JObject root = JObject.Parse(json);
                JToken? customData = root["customData"] ?? root["_customData"];
                JArray? bookmarkList = (customData?["bookmarks"] ?? customData?["_bookmarks"]) as JArray;
                if (bookmarkList == null) return;
                foreach (JObject item in bookmarkList.OfType<JObject>())
                {
                    float? beat = (item["b"] ?? item["_time"])?.Value<float>();
                    if (!beat.HasValue) continue;
                    JArray? colorArray = (item["c"] ?? item["_color"]) as JArray;
                    Color color = colorArray != null && colorArray.Count >= 3
                        ? new Color(colorArray[0].Value<float>(), colorArray[1].Value<float>(), colorArray[2].Value<float>())
                        : Color.red;
                    Bookmarks.Add(new Bookmark
                    {
                        Name = (item["n"] ?? item["_name"])?.Value<string>() ?? string.Empty,
                        TimeInSeconds = beat.Value,
                        Color = color
                    });
                }
                Bookmarks.Sort((left, right) => left.TimeInSeconds.CompareTo(right.TimeInSeconds));
                bool useBpmEvents = (customData?["bookmarksUseOfficialBpmEvents"] ??
                    customData?["_bookmarksUseOfficialBpmEvents"])?.Value<bool>() == true;
                if (!useBpmEvents)
                {
                    foreach (Bookmark bookmark in Bookmarks)
                        bookmark.TimeInSeconds = bookmark.TimeInSeconds * 60f / level.beatsPerMinute;
                    return;
                }

                List<(float Beat, float Bpm)> events = ((root["bpmEvents"] ?? root["_BPMChanges"]) as JArray)?
                    .OfType<JObject>()
                    .Select(item => (Beat: (item["b"] ?? item["_time"])?.Value<float>() ?? 0f,
                        Bpm: (item["m"] ?? item["_BPM"])?.Value<float>() ?? 0f))
                    .Where(item => item.Beat >= 0f && item.Bpm > 0f)
                    .OrderBy(item => item.Beat)
                    .ToList() ?? new List<(float Beat, float Bpm)>();
                float segmentBeat = 0f;
                float segmentSeconds = 0f;
                float bpm = level.beatsPerMinute;
                int eventIndex = 0;
                foreach (Bookmark bookmark in Bookmarks)
                {
                    while (eventIndex < events.Count && events[eventIndex].Beat <= bookmark.TimeInSeconds)
                    {
                        (float eventBeat, float eventBpm) = events[eventIndex++];
                        segmentSeconds += (eventBeat - segmentBeat) * 60f / bpm;
                        segmentBeat = eventBeat;
                        bpm = eventBpm;
                    }
                    bookmark.TimeInSeconds = segmentSeconds + (bookmark.TimeInSeconds - segmentBeat) * 60f / bpm;
                }
            }

            private static void ShowBookmarks(Transform sliderGraphicTransform, BeatmapLevel level)
            {
                for (int index = 0; index < Bookmarks.Count; index++)
                {
                    Bookmark bookmark = Bookmarks[index];
                    Graphic graphic;
                    if (index < GraphicsPool.Count)
                    {
                        graphic = GraphicsPool[index];
                        graphic.gameObject.SetActive(true);
                    }
                    else
                    {
                        graphic = UnityEngine.Object.Instantiate(_templateGraphic!, sliderGraphicTransform.parent);
                        graphic.gameObject.SetActive(true);
                        GraphicsPool.Add(graphic);
                    }
                    bookmark.Graphic = graphic;
                    graphic.transform.localScale = _bookmarkGraphicScale;
                    graphic.transform.position = new Vector3(
                        Mathf.Lerp(_minX, _maxX, Mathf.InverseLerp(0f, level.songDuration, bookmark.TimeInSeconds)),
                        sliderGraphicTransform.position.y, sliderGraphicTransform.position.z);
                    graphic.color = WithAlpha(bookmark.Color, 0.7f);
                }
            }

            private static void SetupNameText(TimeSlider slider)
            {
                if (_currentBookmarkText != null) return;
                Transform label = slider.transform.parent.Find("SongStartLabel");
                if (label == null) return;
                GameObject nameText = UnityEngine.Object.Instantiate(label.gameObject, slider.transform.parent);
                UnityEngine.Object.Destroy(nameText.GetComponent<LocalizedTextMeshProUGUI>());
                _currentBookmarkText = nameText.GetComponent<CurvedTextMeshPro>();
                if (_currentBookmarkText == null) return;
                _currentBookmarkText.transform.position = new Vector3(-0.025f, 2.04f, 4.35f);
                _currentBookmarkText.alignment = TMPro.TextAlignmentOptions.Right;
                _currentBookmarkText.text = string.Empty;
            }

            private static void GetSliderRange(TimeSlider slider, Graphic sliderGraphic)
            {
                float value = slider.value;
                _updatingSliderRange = true;
                try
                {
                    slider.value = slider.maxValue;
                    _maxX = sliderGraphic.transform.position.x + Math.Abs(1f - Config.Instance!.BookmarkWidthSize) * 0.05f;
                    slider.value = slider.minValue;
                    _minX = sliderGraphic.transform.position.x;
                    slider.value = value;
                    _bookmarkGraphicScale = Vector3.Scale(sliderGraphic.transform.localScale,
                        new Vector3(Config.Instance.BookmarkWidthSize, 1f, 1f));
                }
                finally
                {
                    _updatingSliderRange = false;
                }
            }
        }
    }
}
