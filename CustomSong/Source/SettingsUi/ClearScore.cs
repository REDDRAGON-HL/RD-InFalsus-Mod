using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes;

namespace CustomSong
{
    internal static unsafe class ClearScore
    {
        private const int RvaAddRow = 0x6F4EB0;
        private const int TextSize = 0x50;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void> _addRow;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr> _createText;

        private static IntPtr TextSetter()
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass("FastText.dll", "FastText", "FastText");
            if (klass == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);
                IntPtr iter = IntPtr.Zero;
                IntPtr method;
                while ((method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
                {
                    if (Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name_(method) != "SetText") continue;
                    if ((int)Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(method) != 2) continue;
                    IntPtr type = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param(method, 0);
                    if (type == IntPtr.Zero) continue;
                    IntPtr typeClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_from_type(type);
                    if (typeClass == IntPtr.Zero) continue;
                    if (Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(typeClass) != "TextParameters") continue;
                    return method;
                }

                ModLog.Warn("clear score: no SetText(TextParameters) among FastText's methods");
            }
            catch (Exception e)
            {
                ModLog.Warn("clear score: the text setter could not be walked: " + ModLog.Describe(e));
            }

            return IntPtr.Zero;
        }

        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, void> _setText;
        private static bool _asked;
        private static bool _failed;
        private static bool _said;

        internal static void AfterMenuBuilt(Il2CppObjectBase __instance)
        {
            if (_failed) return;
            if (!Resolve()) return;

            try
            {
                IntPtr container = HarmonyHooks.Pointer(__instance);
                if (!Memory.LooksLikeObject(container)) return;
                Add(container);

                if (!_said)
                {
                    _said = true;
                }
            }
            catch (Exception e)
            {
                Fail(ModLog.Describe(e));
            }
        }

        private static void Add(IntPtr container)
        {
            byte* title = stackalloc byte[TextSize];
            byte* body = stackalloc byte[TextSize];
            Fill(title, Caption);
            Fill(body, Slot());
            _addRow(container, (IntPtr)title, (IntPtr)body, IntPtr.Zero);
        }

        private const string Caption = "清除成绩";

        private static string Slot()
        {
            try
            {
                if (!CurrentSong.TryRead(out IntPtr song, out int difficulty)) return "";
                string name = Memory.Text(Memory.Ptr(song + Offsets.Song.BaseName), 128);
                if (string.IsNullOrEmpty(name)) return "";
                string[] flags = { "4-KEY", "NO-SPEED", "6-KEY", "FBD" };
                return $"{name}  [{flags[difficulty]}]";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static void Fill(byte* block, string words)
        {
            new Span<byte>(block, TextSize).Clear();
            if (words == null || _createText == null) return;
            _createText((IntPtr)block, Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(words));
        }

        internal static bool Install()
        {
            MethodInfo built = HarmonyHooks.Target("TutorialReplayContainer", "_vJ", "ifapp.Game.Scenes");
            if (built == null)
            {
                ModLog.WarnRelease("clear score: the help menu was not found; the row cannot be added");
                return false;
            }

            bool patched = HarmonyHooks.Patch(built, null,
                HarmonyHooks.Handler(typeof(ClearScore), nameof(AfterMenuBuilt)));
            return patched && InstallAnswer();
        }

        private static bool InstallAnswer()
        {
            MethodInfo press = HarmonyHooks.Target("TutorialReplayContainer._LB", "_dk",
                                                   "ifapp.Game.Scenes");
            if (press == null)
            {
                ModLog.WarnRelease("clear score: the menu's press handler was not found; the row " +
                                   "cannot be told apart");
                return false;
            }

            bool patched = HarmonyHooks.Patch(press, null,
                HarmonyHooks.Handler(typeof(ClearScore), nameof(AfterPress)));
            return patched && InstallConfirm();
        }

        private static bool InstallConfirm()
        {
            MethodInfo confirm = HarmonyHooks.Target("TutorialReplayContainer._LB", "_Dk",
                                                     "ifapp.Game.Scenes");
            if (confirm == null)
            {
                ModLog.WarnRelease("clear score: the box's confirm handler was not found; a yes will " +
                                   "clear nothing");
                return false;
            }

            bool patched = HarmonyHooks.Patch(confirm, null,
                HarmonyHooks.Handler(typeof(ClearScore), nameof(AfterConfirm)));
            return patched;
        }

        private static void AfterConfirm(Il2CppObjectBase __instance)
        {
            try
            {
                IntPtr closure = HarmonyHooks.Pointer(__instance);
                if (closure == IntPtr.Zero) return;
                if (Memory.Ptr(closure + ClosureAction) != IntPtr.Zero) return;
                Forget();
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("clear score: the score could not be cleared: " + ModLog.Describe(e));
            }
        }

        private const int ClosureAction = 0x60;
        private static ushort _songId;
        private static byte _flag;
        private static bool _aimed;
        private static byte[] _key;

        private static void Remember()
        {
            _aimed = false;

            try
            {
                IntPtr scene = Hooks.SongSelect;
                if (!Memory.LooksLikeObject(scene))
                {
                    ModLog.WarnRelease("clear score: the song-select page is not up; the row will do nothing");
                    return;
                }

                ushort id = Memory.U16(scene + SelectedId);
                byte flag = Memory.U8(scene + SelectedDifficulty);

                if ((flag & 0x0F) == 0 || (flag & 0xF0) != 0)
                {
                    ModLog.WarnRelease($"clear score: the page's difficulty byte is {flag:X2}; " +
                                       "the row will do nothing");
                    return;
                }

                if (id == 0)
                {
                    ModLog.WarnRelease("clear score: the page has no song selected; the row will do nothing");
                    return;
                }

                IntPtr record = Record(scene, id);
                if (record == IntPtr.Zero)
                {
                    ModLog.WarnRelease($"clear score: song {id} is not in the page's list; " +
                                       "the row will do nothing");
                    return;
                }

                _songId = id;
                _flag = flag;
                var key = new byte[0x10];
                BitConverter.GetBytes(Memory.Ptr(record + Offsets.Song.BaseName).ToInt64()).CopyTo(key, 0);
                BitConverter.GetBytes(id).CopyTo(key, 8);
                key[0xA] = flag;
                _key = key;
                _aimed = true;
                ModLog.Load($"clear score: aimed at song {id}, difficulty flag {flag}");
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("clear score: the selection could not be read: " + ModLog.Describe(e));
            }
        }

        private static IntPtr Record(IntPtr scene, ushort id)
        {
            int size = Offsets.Song.Size;
            if (size <= 0) return IntPtr.Zero;
            IntPtr list = Memory.Ptr(scene + SongList);
            if (!Memory.TryList(list, out IntPtr items, out int count)) return IntPtr.Zero;

            for (int i = 0; i < count; i++)
            {
                IntPtr at = items + i * size;
                if (Memory.U16(at + Offsets.Song.Id) == id) return at;
            }

            return IntPtr.Zero;
        }

        private const int SongList = 0x188;
        private const int SelectedId = 0x19C;
        private const int SelectedDifficulty = 0x19E;

        private static void AfterPress(Il2CppObjectBase __instance)
        {
            try
            {
                IntPtr closure = HarmonyHooks.Pointer(__instance);
                if (closure == IntPtr.Zero) return;
                if (Memory.Ptr(closure + ClosureAction) != IntPtr.Zero) return;
                Remember();
                Reword();
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("clear score: the box could not be reworded: " +
                                   ModLog.Describe(e));
            }
        }

        private static void Reword()
        {
            IntPtr modal = Modal();
            if (!Memory.LooksLikeObject(modal)) return;
            byte* words = stackalloc byte[TextSize];
            Fill(words, Question);
            Buffer.MemoryCopy(words, (void*)(modal + TitleBlock), TextSize, TextSize);
            Buffer.MemoryCopy(words, (void*)(modal + BodyBlock), TextSize, TextSize);
            if (_setText == null) return;
            IntPtr heading = Memory.Ptr(modal + HeadingText);
            if (Memory.LooksLikeObject(heading)) _setText(heading, (IntPtr)words, 0);
            IntPtr second = Memory.Ptr(modal + SecondText);
            if (Memory.LooksLikeObject(second)) _setText(second, (IntPtr)words, 0);
        }

        private const int TitleBlock = 0x128;
        private const int BodyBlock = 0x178;
        private const int HeadingText = 0xB8;
        private const int SecondText = 0xC0;
        private const string Question = "确定删除该谱成绩？";
        private const int ResultsHistory = 0x18;
        private const int ResultsHighscores = 0x10;
        private const int ResultSong = 0x0;
        private const int ResultDifficulty = 0x2;
        private const int RvaLength = 0x4FF8F0;
        private const int RvaItem = 0xF9BB40;
        private const int RvaRemoveAt = 0xF9B520;

        private static void Forget()
        {
            if (!ScoreStore.Ready)
            {
                ModLog.WarnRelease("clear score: the custom results are not open; nothing was removed");
                return;
            }

            if (!_aimed)
            {
                ModLog.WarnRelease("clear score: the row was not aimed at anything; nothing was removed");
                return;
            }

            int history = ForgetHistory(ScoreStore.Table, _songId, _flag);
            bool highscore = ForgetHighscore(ScoreStore.Table, _key);
            ScoreStore.Save();
            ModLog.Load($"clear score: {history} run(s) and {(highscore ? "the best" : "no best")} " +
                        $"cleared for song {_songId} at difficulty flag {_flag}");
            _aimed = false;
        }

        private static int ForgetHistory(IntPtr table, ushort songId, byte flag)
        {
            IntPtr list = Memory.Ptr(table + ResultsHistory);
            if (!Memory.LooksLikeObject(list)) return 0;
            if (!HistoryMethods()) return 0;
            int removed = 0;
            for (int i = _length(list) - 1; i >= 0; i--)
            {
                IntPtr result = _item(list, i);
                if (result == IntPtr.Zero) continue;
                if (Memory.U16(result + ResultSong) != songId) continue;
                if (Memory.U8(result + ResultDifficulty) != flag) continue;
                _removeAt(list, i);
                removed++;
            }

            return removed;
        }

        private static bool HistoryMethods()
        {
            if (_length != null && _item != null && _removeAt != null) return true;
            IntPtr length = Generic("get_Length", 0);
            IntPtr item = Generic("get_Item", 1);
            IntPtr removeAt = Generic("RemoveAt", 1);

            if (length == IntPtr.Zero || item == IntPtr.Zero || removeAt == IntPtr.Zero)
            {
                ModLog.WarnRelease("clear score: the history list's methods were not found; the runs " +
                                   "stay in it");
                return false;
            }

            _length = (delegate* unmanaged[Cdecl]<IntPtr, int>)*(IntPtr*)length;
            _item = (delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr>)*(IntPtr*)item;
            _removeAt = (delegate* unmanaged[Cdecl]<IntPtr, int, void>)*(IntPtr*)removeAt;
            return true;
        }

        private static bool ForgetHighscore(IntPtr table, byte[] key)
        {
            IntPtr scores = Memory.Ptr(table + ResultsHighscores);
            if (!Memory.LooksLikeObject(scores) || key == null) return false;

            try
            {
                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(scores);
                IntPtr remove = klass == IntPtr.Zero ? IntPtr.Zero : ScoreStore.MethodOn(klass, "Remove", 1);
                if (remove == IntPtr.Zero)
                {
                    ModLog.WarnRelease("clear score: the best-result table has no Remove in this build; " +
                                       "the score stays on the song cards");
                    return false;
                }

                fixed (byte* keyPointer = key)
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)keyPointer;
                    IntPtr raised = IntPtr.Zero;
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(remove, scores, (void**)args, ref raised);
                    if (raised != IntPtr.Zero)
                    {
                        ModLog.WarnRelease("clear score: the best-result table raised on the removal: " +
                                           Raised.Text(raised));
                        return false;
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("clear score: the best result could not be removed: " + ModLog.Describe(e));
                return false;
            }
        }

        private static delegate* unmanaged[Cdecl]<IntPtr, int> _length;
        private static delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr> _item;
        private static delegate* unmanaged[Cdecl]<IntPtr, int, void> _removeAt;

        private static IntPtr Generic(string methodName, int parameters)
        {
            if (!ScoreStore.Ready) return IntPtr.Zero;
            IntPtr list = Memory.Ptr(ScoreStore.Table + ResultsHistory);
            if (!Memory.LooksLikeObject(list)) return IntPtr.Zero;
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(list);
            return klass == IntPtr.Zero ? IntPtr.Zero : ScoreStore.MethodOn(klass, methodName, parameters);
        }

        private static IntPtr Modal()
        {
            IntPtr klass = FieldResolver.ClassPointer("UiModal");
            if (klass == IntPtr.Zero) return IntPtr.Zero;
            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return IntPtr.Zero;
            return Memory.Ptr(statics);
        }

        private static bool Resolve()
        {
            if (_asked) return _createText != null && _addRow != null;
            _asked = true;
            IntPtr add = MethodResolver.ByName("TutorialReplayContainer", "_VJ");
            if (add == IntPtr.Zero)
                add = MethodResolver.ByRuntime("TutorialReplayContainer", "_VJ", "Game.dll",
                                               "ifapp.Game.Scenes");
            if (add == IntPtr.Zero && GameAssembly.Base != IntPtr.Zero)
                add = GameAssembly.Base + RvaAddRow;
            IntPtr create = MethodResolver.ByRuntime("TextParameters", "CreateDirectString", "Str", "Str");
            IntPtr text = TextSetter();

            if (add == IntPtr.Zero)
            {
                Fail("the menu's row entry was not found");
                return false;
            }

            if (create == IntPtr.Zero)
            {
                Fail("the game's own string-to-parameters builder was not found");
                return false;
            }

            _addRow = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)add;
            _createText = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr>)create;

            if (text != IntPtr.Zero)
                _setText = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, void>)*(IntPtr*)text;
            else
                ModLog.WarnRelease("clear score: the box keeps the game's own words");
            _length = null;
            _item = null;
            _removeAt = null;
            return true;
        }

        private static void Fail(string why)
        {
            if (_failed) return;
            _failed = true;
            ModLog.WarnRelease("clear score: the menu row was not added: " + why);
        }
    }
}
