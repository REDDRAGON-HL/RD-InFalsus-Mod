using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe class LevelLabel
    {
        private const int CardLevels = 0xA8;
        private const int CardTitles = 0x90;
        private const int CardArtists = 0xA0;
        private const int ButtonLevels = 0x48;
        private const int HudLevels = 0x68;
        private const int HudTitle = 0x30;
        private const int TransitionLevels = 0x90;
        private const int SmallCardLevel = 0x98;
        private const int SmallCardTitle = 0x50;
        private const int ResultsNode = 0x58;
        private const int ResultsTexts = 0x68;
        private static int _viewModelSong = Offsets.Unresolved;
        private static int _viewModelDifficulty = Offsets.Unresolved;
        private static bool _viewModelResolved;

        private static bool ResolveViewModel()
        {
            if (_viewModelResolved) return true;
            _viewModelSong = FieldResolver.Field("_SH", "_WEb");
            _viewModelDifficulty = FieldResolver.Field("_SH", "_xEb");

            if (_viewModelSong < 0 || _viewModelDifficulty < 0)
            {
                ModLog.WarnRelease("level labels: the song card's view model could not be read by " +
                                   "name; the card and the strip keep their numbers");
                return false;
            }

            _viewModelResolved = true;
            return true;
        }

        private const int TextFont = 0x20;
        private const int TextLocalization = 0x120;

        private static readonly Dictionary<IntPtr, (IntPtr Family, int Localization)> Borrowed =
            new Dictionary<IntPtr, (IntPtr, int)>();
        private static IntPtr _levelFamily;
        private static readonly List<IntPtr> Donors = new List<IntPtr>();
        private static readonly Dictionary<IntPtr, int> DonorLocalization = new Dictionary<IntPtr, int>();
        private static int _diagnosed;
        private static int _errors;
        private static string _said;

        private static void Say(string place, string id, int index, string label)
        {
            string line = $"{place}: {id} [{Name(index)}] -> '{label}'";
            if (line == _said) return;
            _said = line;
            ModLog.Load(line);
        }

        private static string Name(int index) => index switch
        {
            0 => "MIN",
            1 => "EVO",
            2 => "ULT",
            3 => "FBD",
            _ => "?",
        };
        private const int SelectedId = 0x19C;
        private const int SelectedDifficulty = 0x19E;

        internal static void CurrentSelection(out string baseName, out byte difficulty)
        {
            baseName = null;
            difficulty = 0;
            IntPtr scene = Hooks.SongSelect;
            if (!Memory.LooksLikeObject(scene)) return;
            ushort id = Memory.U16(scene + SelectedId);
            byte flag = Memory.U8(scene + SelectedDifficulty);
            if (id == 0 || SongCatalog.DifficultyIndexOf(flag) < 0) return;

            foreach (KeyValuePair<ushort, ChartInfo> entry in SongCatalog.ById)
            {
                if (entry.Key != id) continue;
                baseName = entry.Value.BaseName;
                difficulty = flag;
                return;
            }
        }

        internal static int IndexOf(byte flag) => SongCatalog.DifficultyIndexOf(flag);

        private static string LabelOf(string baseName, int index, out int rating)
        {
            rating = 0;
            if (index < 0 || string.IsNullOrEmpty(baseName)) return null;
            if (!SongCatalog.TryByName(baseName, out ChartInfo info)) return null;
            rating = info.Level?[index] ?? 0;
            if (rating <= 0) return null;
            string label = info.Special?[index];
            return string.IsNullOrEmpty(label) ? null : label;
        }

        internal static void Card(IntPtr card, IntPtr current)
        {
            if (!Memory.LooksLikeObject(card) || !Memory.LooksLikeObject(current)) return;
            if (!ResolveViewModel()) return;
            string id = Memory.Text(Memory.Ptr(current + _viewModelSong + Offsets.Song.BaseName), 128);
            int index = IndexOf(Memory.U8(current + _viewModelDifficulty));
            string label = LabelOf(id, index, out int rating);
            IntPtr numbers = Memory.Ptr(card + CardLevels);
            NoteLevelFamily(FirstText(numbers, true));
            LearnDonors(card);

            if (label == null)
            {
                WidgetText.RestoreConstrained(numbers, rating);
                return;
            }

            if (WidgetText.WriteConstrained(numbers, label) > 0) Say("song card", id, index, label);
        }

        internal static void Button(IntPtr button, int rating)
        {
            if (!Memory.LooksLikeObject(button)) return;
            CurrentSelection(out string baseName, out byte difficulty);
            int index = IndexOf(difficulty);
            string label = LabelOf(baseName, index, out int known);
            if (label != null && (known <= 0 || known != rating)) return;
            IntPtr texts = Memory.Ptr(button + ButtonLevels);
            NoteLevelFamily(FirstText(texts));

            if (label == null)
            {
                WidgetText.RestoreTexts(texts, rating);
                return;
            }

            if (WidgetText.WriteTexts(texts, label) > 0) Say("difficulty button", baseName, index, label);
        }

        internal static void Hud(IntPtr hud, IntPtr songInfo, byte difficulty)
        {
            if (!Memory.LooksLikeObject(hud) || !Memory.LooksLikeObject(songInfo)) return;
            string id = Memory.Text(Memory.Ptr(songInfo + Offsets.Song.BaseName), 128);
            int index = IndexOf(difficulty);
            string label = LabelOf(id, index, out int rating);
            IntPtr texts = Memory.Ptr(hud + HudLevels);
            NoteLevelFamily(FirstText(texts));

            if (label == null)
            {
                WidgetText.RestoreTexts(texts, rating);
                return;
            }

            ConsiderDonor(Memory.Ptr(hud + HudTitle));
            if (WidgetText.WriteTexts(texts, label) > 0) Say("in-game hud", id, index, label);
        }

        internal static void Transition(IntPtr layer, IntPtr songInfo, byte difficulty, string place)
        {
            if (!Memory.LooksLikeObject(layer) || !Memory.LooksLikeObject(songInfo)) return;
            string id = Memory.Text(Memory.Ptr(songInfo + Offsets.Song.BaseName), 128);
            int index = IndexOf(difficulty);
            string label = LabelOf(id, index, out int rating);
            IntPtr numbers = Memory.Ptr(layer + TransitionLevels);
            NoteLevelFamily(FirstText(numbers, true));

            if (label == null)
            {
                WidgetText.RestoreConstrained(numbers, rating);
                return;
            }

            if (WidgetText.WriteConstrained(numbers, label) > 0) Say(place, id, index, label);
        }

        internal static void SmallCard(IntPtr card, IntPtr current)
        {
            if (!Memory.LooksLikeObject(card) || !Memory.LooksLikeObject(current)) return;
            if (!ResolveViewModel()) return;
            string id = Memory.Text(Memory.Ptr(current + _viewModelSong + Offsets.Song.BaseName), 128);
            int index = IndexOf(Memory.U8(current + _viewModelDifficulty));
            string label = LabelOf(id, index, out int rating);
            IntPtr node = Memory.Ptr(card + SmallCardLevel);
            if (!Memory.LooksLikeObject(node)) return;
            IntPtr text = Memory.Ptr(node + WidgetText.ConstrainedText);
            if (!Memory.LooksLikeObject(text)) return;
            NoteLevelFamily(text);

            if (label == null)
            {
                WidgetText.RestoreNode(node, rating);
                return;
            }

            ConsiderDonor(Memory.Ptr(card + SmallCardTitle));
            Dress(text, label);

            if (WidgetText.Write(text, label) != IntPtr.Zero)
            {
                WidgetText.Relayout(node);
                Say("song strip", id, index, label);
            }
        }

        internal static void Results(IntPtr panel, IntPtr songInfo, byte difficulty)
        {
            if (!Memory.LooksLikeObject(panel) || !Memory.LooksLikeObject(songInfo)) return;
            string id = Memory.Text(Memory.Ptr(songInfo + Offsets.Song.BaseName), 128);
            int index = IndexOf(difficulty);
            string label = LabelOf(id, index, out int rating);
            IntPtr node = Memory.Ptr(panel + ResultsNode);
            IntPtr texts = Memory.Ptr(panel + ResultsTexts);
            NoteLevelFamily(FirstText(texts));

            if (label == null)
            {
                WidgetText.RestoreNode(node, rating);
                WidgetText.RestoreTexts(texts, rating);
                return;
            }

            int written = 0;

            if (Memory.LooksLikeObject(node))
            {
                IntPtr text = Memory.Ptr(node + WidgetText.ConstrainedText);
                if (Memory.LooksLikeObject(text) && WidgetText.Write(text, label) != IntPtr.Zero)
                {
                    WidgetText.Relayout(node);
                    written++;
                }
            }

            written += WidgetText.WriteTexts(texts, label);
            if (written > 0) Say("results", id, index, label);
        }

        private static void LearnDonors(IntPtr card)
        {
            ConsiderDonor(FirstText(Memory.Ptr(card + CardArtists)));
            ConsiderDonor(FirstText(Memory.Ptr(card + CardTitles)));
        }

        private static IntPtr FirstText(IntPtr array)
        {
            int length = WidgetText.ArrayLength(array);
            for (int i = 0; i < length; i++)
            {
                IntPtr text = WidgetText.Element(array, i);
                if (Memory.LooksLikeObject(text)) return text;
            }

            return IntPtr.Zero;
        }

        private static IntPtr FirstText(IntPtr array, bool throughConstrained)
        {
            if (!throughConstrained) return FirstText(array);
            int length = WidgetText.ArrayLength(array);
            for (int i = 0; i < length; i++)
            {
                IntPtr node = WidgetText.Element(array, i);
                if (!Memory.LooksLikeObject(node)) continue;
                IntPtr text = Memory.Ptr(node + WidgetText.ConstrainedText);
                if (Memory.LooksLikeObject(text)) return text;
            }

            return IntPtr.Zero;
        }

        private static void NoteLevelFamily(IntPtr levelText)
        {
            if (_levelFamily != IntPtr.Zero || !Memory.LooksLikeObject(levelText)) return;
            IntPtr family = Memory.Ptr(levelText + TextFont);
            if (Memory.LooksLikeObject(family)) _levelFamily = family;
        }

        private static void ConsiderDonor(IntPtr fastText)
        {
            if (!Memory.LooksLikeObject(fastText)) return;

            try
            {
                IntPtr family = Memory.Ptr(fastText + TextFont);
                if (!Memory.LooksLikeObject(family)) return;
                if (family == _levelFamily) return;
                if (Donors.Contains(family)) return;
                int localization = Memory.I32(fastText + TextLocalization);
                if (localization < 1 || localization > 5) return;
                int assets = WidgetText.ArrayLength(Memory.Ptr(family + 0x20));
                if (assets <= 0) return;
                if (Donors.Count >= 8) return;
                Donors.Add(family);
                DonorLocalization[family] = localization;

                if (_diagnosed++ == 0)
                    ModLog.Load($"level labels: the label font is family 0x{family.ToInt64():X} " +
                                $"(localization {localization}, {assets} asset(s)), taken from " +
                                $"'{Shorten(WidgetText.Read(fastText))}'");
            }
            catch (Exception e)
            {
                Guard("the font lookup", e);
            }
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= 24 ? text : text.Substring(0, 24) + "…";
        }

        private static IntPtr DonorFor(string label)
        {
            if (NeedsMoreThanAscii(label) && Donors.Count > 0) return Donors[0];
            return IntPtr.Zero;
        }

        internal static void Dress(IntPtr fastText, string label)
        {
            if (!Memory.LooksLikeObject(fastText)) return;

            try
            {
                IntPtr donor = DonorFor(label);

                if (donor == IntPtr.Zero)
                {
                    ReleaseFont(fastText);
                    return;
                }

                if (Memory.Ptr(fastText + TextFont) == donor) return;
                if (Borrowed.ContainsKey(fastText)) return;
                Borrowed[fastText] = (Memory.Ptr(fastText + TextFont), Memory.I32(fastText + TextLocalization));
                Memory.WritePtr(fastText + TextFont, donor);
                if (DonorLocalization.TryGetValue(donor, out int localization))
                    Memory.WriteI32(fastText + TextLocalization, localization);
            }
            catch (Exception e)
            {
                Guard("the font swap", e);
            }
        }

        internal static bool ReleaseFont(IntPtr fastText)
        {
            if (!Memory.LooksLikeObject(fastText)) return false;
            if (!Borrowed.TryGetValue(fastText, out (IntPtr Family, int Localization) original)) return false;
            Borrowed.Remove(fastText);

            try
            {
                Memory.WritePtr(fastText + TextFont, original.Family);
                Memory.WriteI32(fastText + TextLocalization, original.Localization);
                return true;
            }
            catch (Exception e)
            {
                Guard("the font restore", e);
                return false;
            }
        }

        private static bool NeedsMoreThanAscii(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;

            foreach (char c in label)
                if (c > 0x7E) return true;
            return false;
        }

        private static void Guard(string what, Exception e)
        {
            if (_errors++ > 4) return;
            ModLog.WarnRelease($"level labels: {what} threw {ModLog.Describe(e)}");
        }
    }
}
