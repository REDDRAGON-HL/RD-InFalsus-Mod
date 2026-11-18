using System;
using System.Collections.Generic;

namespace CustomSong
{
    internal static class SceneDirectives
    {
        internal enum Kind
        {
            SetColor,

            HideGroup,

            TrackDisplay,

            TrackMove,

            ShowText,

            AudioOffset,

            GroupSpeed,
        }

        internal struct Entry
        {
            internal Kind Kind;
            internal int Time;
            internal long Group;
            internal int Value;
            internal int Size;
            internal int Duration;
            internal uint Rgba;
            internal bool EdgeGlow;
            internal int Mode;
            internal float X, Y, Z, Xoz, Yoz, Xoy;
        }

        internal struct SkyVisual
        {
            internal long Id;
            internal long Group;
            internal int TimingGroup;
            internal int Start;
            internal int End;
            internal float StartCenter, EndCenter;
            internal float StartWidth, EndWidth;
            internal int LeftEase, RightEase;
            internal bool NoClip;
            internal float PrevLo, PrevHi, NextLo, NextHi;
            internal bool IsFirst, IsLast;
            internal bool IsSkyNote;
        }

        internal struct Text
        {
            internal string Value;
            internal int Start;
            internal int End;
            internal float Size;
            internal int Side;
            internal float X;
            internal float Y;
            internal float Amp;
            internal float Hz;
            internal float Spacing;
            internal float FadeIn;
            internal float FadeOut;
            internal uint Fill;
            internal uint Shadow;
            internal float ShadowPx;
        }

        private static readonly Dictionary<string, Entry[]> BySeed =
            new Dictionary<string, Entry[]>(StringComparer.Ordinal);

        private static readonly Dictionary<string, SkyVisual[]> VisualsBySeed =
            new Dictionary<string, SkyVisual[]>(StringComparer.Ordinal);

        private static readonly Dictionary<string, Text[]> TextsBySeed =
            new Dictionary<string, Text[]>(StringComparer.Ordinal);
        internal static Entry[] Current = Array.Empty<Entry>();
        internal static SkyVisual[] CurrentVisuals = Array.Empty<SkyVisual>();
        internal static Text[] CurrentTexts = Array.Empty<Text>();
        internal static int Generation;
        internal static Dictionary<long, int> CurrentGroups = new Dictionary<long, int>();
        internal static Dictionary<long, long> CurrentSkyGroups = new Dictionary<long, long>();

        private static readonly Dictionary<string, Dictionary<long, int>> GroupsBySeed =
            new Dictionary<string, Dictionary<long, int>>(StringComparer.Ordinal);

        private static readonly Dictionary<string, Dictionary<long, long>> SkyBySeed =
            new Dictionary<string, Dictionary<long, long>>(StringComparer.Ordinal);

        internal static void Publish(string seed, List<Entry> entries, Dictionary<long, int> groups,
                                     Dictionary<long, long> skyGroups, List<SkyVisual> visuals,
                                     List<Text> texts)
        {
            if (string.IsNullOrEmpty(seed)) return;

            if (entries == null || entries.Count == 0)
            {
                BySeed.Remove(seed);
                GroupsBySeed.Remove(seed);
                SkyBySeed.Remove(seed);
            }
            else
            {
                entries.Sort((a, b) => a.Time.CompareTo(b.Time));
                BySeed[seed] = entries.ToArray();
                GroupsBySeed[seed] = groups ?? new Dictionary<long, int>();
                SkyBySeed[seed] = skyGroups ?? new Dictionary<long, long>();
            }

            if (visuals == null || visuals.Count == 0) VisualsBySeed.Remove(seed);
            else VisualsBySeed[seed] = visuals.ToArray();
            if (texts == null || texts.Count == 0) TextsBySeed.Remove(seed);
            else
            {
                texts.Sort((a, b) => a.Start.CompareTo(b.Start));
                TextsBySeed[seed] = texts.ToArray();
            }
        }

        internal static void Activate(string seed)
        {
            Generation++;
            Entry[] entries = Array.Empty<Entry>();
            bool found = seed != null && BySeed.TryGetValue(seed, out entries);
            Current = entries ?? Array.Empty<Entry>();
            Dictionary<long, int> groups = null;
            CurrentGroups = found && GroupsBySeed.TryGetValue(seed, out groups) && groups != null
                ? groups
                : new Dictionary<long, int>();
            Dictionary<long, long> sky = null;
            CurrentSkyGroups = found && SkyBySeed.TryGetValue(seed, out sky) && sky != null
                ? sky
                : new Dictionary<long, long>();
            SkyVisual[] visuals = Array.Empty<SkyVisual>();
            CurrentVisuals = seed != null && VisualsBySeed.TryGetValue(seed, out visuals) && visuals != null
                ? visuals
                : Array.Empty<SkyVisual>();
            Text[] texts = Array.Empty<Text>();
            CurrentTexts = seed != null && TextsBySeed.TryGetValue(seed, out texts) && texts != null
                ? texts
                : Array.Empty<Text>();
        }

        internal static void AddTexts(List<Text> rows)
        {
            if (rows == null || rows.Count == 0) return;
            var merged = new List<Text>(CurrentTexts.Length + rows.Count);
            merged.AddRange(CurrentTexts);
            merged.AddRange(rows);
            merged.Sort((a, b) => a.Start.CompareTo(b.Start));
            CurrentTexts = merged.ToArray();
        }

        internal static int TimingGroupOf(long noteId) =>            CurrentGroups.TryGetValue(noteId, out int group) ? group : 0;

        internal static long SkyGroupOf(long noteId) =>
            CurrentSkyGroups.TryGetValue(noteId, out long group) ? group : 0;

        internal static void Clear()
        {
            Current = Array.Empty<Entry>();
            CurrentVisuals = Array.Empty<SkyVisual>();
            CurrentTexts = Array.Empty<Text>();
            CurrentGroups = new Dictionary<long, int>();
            CurrentSkyGroups = new Dictionary<long, long>();
        }

        internal static bool Any
        {
            get
            {
                Entry[] entries = Current;
                SkyVisual[] visuals = CurrentVisuals;
                Text[] texts = CurrentTexts;
                return (entries != null && entries.Length > 0) ||
                       (visuals != null && visuals.Length > 0) ||
                       (texts != null && texts.Length > 0);
            }
        }
    }
}
