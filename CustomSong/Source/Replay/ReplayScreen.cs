extern alias UnityEngineCore;
using System;
using System.Collections.Generic;
using CustomSong.Auto;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngineCore::UnityEngine;

namespace CustomSong.Replay
{
    internal static unsafe class ReplayScreen
    {
        internal const int TabKey = 3;
        private const int LeftShiftKey = 51;
        private const int RightShiftKey = 52;
        private const int RetryFallback = 0x100;
        private const int TextMappingFallback = 0xB0;
        private const int RetryTextsFallback = 0x100;
        private const int InstanceFallback = 0x10;
        private const int KeyboardStateFallback = 0x50;
        private static bool _asked;
        private static IntPtr _scene;
        private static IntPtr _retry;
        private static IntPtr[] _texts;
        private static string[] _originals;
        private static bool _swapped;
        private static bool _lastShift;
        private static bool _labelProbed;
        private static bool _labelWarned;
        private static bool _updateSeen;
        private static bool _returnRequested;
        private static bool _finishing;
        private static int _finishFrames;
        private static bool _seeked;
        private const int FinishFrames = 600;
        private static bool _skipping;
        private static int _tabFrames;
        private static bool _tabLogged;
        private static bool _tabReported;
        private static bool _encounterLogged;
        private static byte[] _resultBlock;
        private const int ResultBlockSize = 0x60;
        private static byte[] _encounterBlock;
        private const int EncounterBlockSize = 0x48;
        private static byte[] _scalars;
        private const int ScalarFrom = 0xA8;
        private const int ScalarTo = 0x100;

        private sealed class ArrayBlock
        {
            internal int Offset;
            internal byte[] Bytes;
            internal int Length;
        }

        private static ArrayBlock[] _arrays;
        private static IntPtr _getText;
        private static IntPtr _inputClass;
        private static int _instanceOffset = -1;
        private static int _keyboardStateOffset = -1;

        internal static void Install()
        {
            if (_asked) return;
            _asked = true;

            bool update = HarmonyHooks.Patch(
                HarmonyHooks.Target("ResultsScene", "Update", "ifapp.Game.Scenes"),
                null,
                HarmonyHooks.Handler(typeof(ReplayScreen), nameof(UpdatePostfix)));

            bool click = HarmonyHooks.Patch(
                HarmonyHooks.Target("UIButton", "_lSA", "ifapp.Game.UI.Common"),
                HarmonyHooks.Handler(typeof(ReplayScreen), nameof(ClickedPrefix)));

            bool splash = HarmonyHooks.Patch(
                HarmonyHooks.Target("ResultsSplashContainer", "_xM", "ifapp.Game.Scenes"),
                HarmonyHooks.Handler(typeof(ReplayScreen), nameof(SplashDonePrefix)));

            bool reveal = HarmonyHooks.Patch(
                HarmonyHooks.Target("ResultsSplashContainer", "_yM", "ifapp.Game.Scenes"),
                HarmonyHooks.Handler(typeof(ReplayScreen), nameof(SplashStartPrefix)));

            bool wait = HarmonyHooks.Patch(
                HarmonyHooks.Target("_KG", "get_keepWaiting", "_k"),
                HarmonyHooks.Handler(typeof(ReplayScreen), nameof(KeepWaitingPrefix)));

            bool encounter = HarmonyHooks.Patch(
                HarmonyHooks.Target("StoryIdentifier", "_ePA", "ifapp.Game.Data.Story"),
                null,
                HarmonyHooks.Handler(typeof(ReplayScreen), nameof(EncounterComputedPostfix)));
        }

        private static void EncounterComputedPostfix()
        {
            if (!ReplayCore.Ran || _encounterBlock == null) return;

            try
            {
                IntPtr statics = ResultsStatics();
                if (statics == IntPtr.Zero) return;

                System.Runtime.InteropServices.Marshal.Copy(_encounterBlock, 0, statics + 0x60,
                                                            _encounterBlock.Length);

                if (_resultBlock != null)
                    System.Runtime.InteropServices.Marshal.Copy(_resultBlock, 0, statics, _resultBlock.Length);

                if (!_encounterLogged)
                {
                    _encounterLogged = true;
                }
            }
            catch (Exception e)
            {
                ModLog.Warn("replay: the encounter result could not be put back: " +
                                   ModLog.Describe(e));
            }
        }

        private static bool SplashStartPrefix() => !_skipping;

        private static bool SplashDonePrefix(ref bool __result)
        {
            if (!ReplayCore.Ran) return true;
            __result = true;
            return false;
        }

        private static bool KeepWaitingPrefix(ref bool __result)
        {
            if (!_skipping) return true;
            __result = false;
            return false;
        }

        internal static void Uninstall()
        {
            _asked = false;
            Forget();
        }

        internal static void ResultsStarted(IntPtr scene)
        {
            ReplayCore.ResultsScreenUp();

            if (_tabFrames > 0 && !_tabReported)
            {
                _tabReported = true;
            }

            if (!Memory.LooksLikeObject(scene)) return;
            if (scene == _scene) return;
            _scene = scene;
            _retry = IntPtr.Zero;
            _texts = null;
            _originals = null;
            _swapped = false;
            _lastShift = false;
            _labelProbed = false;

            if (_tabFrames > 0)
            {
                _tabFrames = 0;
            }

            int offset = FieldResolver.Lookup("ResultsScene", "retryButton");
            if (offset < 0) offset = RetryFallback;
            _retry = Memory.Ptr(scene + offset);

            if (!ReplayCore.Ran) SaveResultBlock();
            LogResult();
        }

        internal static void RestoreResultBlock()
        {
            if (_resultBlock == null) return;

            try
            {
                IntPtr statics = ResultsStatics();
                if (statics == IntPtr.Zero) return;
                System.Runtime.InteropServices.Marshal.Copy(_resultBlock, 0, statics, _resultBlock.Length);

                if (_encounterBlock != null)
                    System.Runtime.InteropServices.Marshal.Copy(_encounterBlock, 0, statics + 0x60, _encounterBlock.Length);

                if (_scalars != null)
                    System.Runtime.InteropServices.Marshal.Copy(_scalars, 0, statics + ScalarFrom, _scalars.Length);
                string phases = RestoreArrays(statics);
            }
            catch (Exception e)
            {
                ModLog.Warn("replay: the result block could not be restored: " + ModLog.Describe(e));
            }
        }

        private static void SaveResultBlock()
        {
            try
            {
                IntPtr statics = ResultsStatics();
                if (statics == IntPtr.Zero) return;
                var block = new byte[ResultBlockSize];
                System.Runtime.InteropServices.Marshal.Copy(statics, block, 0, block.Length);
                _resultBlock = block;
                var encounter = new byte[EncounterBlockSize];
                System.Runtime.InteropServices.Marshal.Copy(statics + 0x60, encounter, 0, encounter.Length);
                _encounterBlock = encounter;
                var scalars = new byte[ScalarTo - ScalarFrom];
                System.Runtime.InteropServices.Marshal.Copy(statics + ScalarFrom, scalars, 0, scalars.Length);
                _scalars = scalars;

                _arrays = new[]
                {
                    SnapshotArray(statics, 0xD0),
                    SnapshotArray(statics, 0xD8),
                    SnapshotArray(statics, 0xE0),
                };
            }
            catch (Exception e)
            {
                ModLog.Warn("replay: the result block could not be saved: " + ModLog.Describe(e));
            }
        }

        private static ArrayBlock SnapshotArray(IntPtr statics, int offset)
        {
            var block = new ArrayBlock { Offset = offset };

            try
            {
                IntPtr array = Memory.Ptr(statics + offset);
                if (!Memory.LooksLikeObject(array)) return block;
                int length = Memory.I32(array + Offsets.Runtime.ArrayLength);
                if (length <= 0 || length > 100000) return block;
                int bytes = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(array);
                if (bytes <= 0) return block;
                var copy = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(array + Offsets.Runtime.ArrayDataOffset, copy, 0, bytes);
                block.Bytes = copy;
                block.Length = length;
            }
            catch (Exception e)
            {
                ModLog.WarnRelease($"replay: the results array at 0x{offset:X} could not be read: " +
                                   ModLog.Describe(e));
            }

            return block;
        }

        private static string RestoreArrays(IntPtr statics)
        {
            if (_arrays == null) return "none";
            var parts = new List<string>(_arrays.Length);

            foreach (ArrayBlock block in _arrays)
            {
                if (block.Bytes == null)
                {
                    parts.Add("none");
                    continue;
                }

                try
                {
                    IntPtr array = Memory.Ptr(statics + block.Offset);
                    if (!Memory.LooksLikeObject(array))
                    {
                        parts.Add("gone");
                        continue;
                    }

                    int length = Memory.I32(array + Offsets.Runtime.ArrayLength);
                    int bytes = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(array);
                    if (length != block.Length || bytes != block.Bytes.Length)
                    {
                        parts.Add($"{length}/{block.Length}");
                        continue;
                    }

                    System.Runtime.InteropServices.Marshal.Copy(
                        block.Bytes, 0, array + Offsets.Runtime.ArrayDataOffset, bytes);
                    parts.Add(length.ToString());
                }
                catch (Exception e)
                {
                    ModLog.Warn($"replay: the results array at 0x{block.Offset:X} could not be " +
                                       "restored: " + ModLog.Describe(e));
                    parts.Add("error");
                }
            }

            return string.Join("/", parts);
        }

        private static string Describe(ArrayBlock[] blocks)
        {
            if (blocks == null) return "none";
            var parts = new List<string>(blocks.Length);
            foreach (ArrayBlock block in blocks) parts.Add(block.Bytes == null ? "none" : block.Length.ToString());
            return string.Join("/", parts);
        }

        private static IntPtr ResultsStatics()
        {
            IntPtr klass = MethodResolver.ClassByFullName("ifapp.Game.Scenes", "ResultsScene");
            return klass == IntPtr.Zero ? IntPtr.Zero : FieldResolver.Statics(klass);
        }

        private static void LogResult()
        {
            try
            {
                IntPtr klass = MethodResolver.ClassByFullName("ifapp.Game.Scenes", "ResultsScene");
                if (klass == IntPtr.Zero) return;
                IntPtr statics = FieldResolver.Statics(klass);
                if (statics == IntPtr.Zero) return;

                Counts("tap", statics + 0x0A);
                Counts("hold", statics + 0x1A);
                Counts("sky", statics + 0x2A);
                Counts("flick", statics + 0x3A);
            }
            catch (Exception)
            {
            }
        }

        private static void Counts(string name, IntPtr at)
        {
        }

        internal static void RequestReturn() => _returnRequested = true;

        internal static void Finished()
        {
            if (_tabFrames > 0 && !_tabLogged)
            {
                _tabLogged = true;
            }

            _finishing = false;
            _skipping = false;
        }

        internal static void Forget()
        {
            _scene = IntPtr.Zero;
            _retry = IntPtr.Zero;
            _texts = null;
            _originals = null;
            _swapped = false;
            _labelProbed = false;
            _returnRequested = false;
            _finishing = false;
            _skipping = false;
            _tabFrames = 0;
            _tabLogged = false;
            _tabReported = false;
            _encounterLogged = false;
        }

        internal static void ReturnToResults()
        {
            if (!ReplayCore.Playing) return;

            if (!ResultsSkip.GameplayRunning)
            {
                return;
            }

            ReplayCore.Playing = false;
            _finishing = true;
            _finishFrames = 0;
            _skipping = true;
            _tabFrames = 1;
            _tabLogged = false;
            _tabReported = false;
            _encounterLogged = false;

            if (ResultsSkip.FinishGameplay())
            {
                _seeked = AudioOffset.FinishNow();
                return;
            }

            if (AudioOffset.FinishNow())
            {
                _seeked = true;
                return;
            }

            _finishing = false;
            _skipping = false;
            _tabFrames = 0;
            ReplayCore.Playing = true;
            ModLog.Warn("replay: Tab could not end the song (no scene, no audio channel); " +
                               "the replay keeps playing");
        }

        internal static bool KeyDown(int key)
        {
            try
            {
                if (_inputClass == IntPtr.Zero && !ResolveInput()) return false;
                IntPtr statics = FieldResolver.Statics(_inputClass);
                if (statics == IntPtr.Zero) return false;
                IntPtr instance = Memory.Ptr(statics + _instanceOffset);
                if (!Memory.LooksLikeObject(instance)) return false;
                IntPtr bit = instance + _keyboardStateOffset + (key >> 3);
                return (Memory.U8(bit) & (1 << (key & 7))) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool ResolveInput()
        {
            _inputClass = MethodResolver.ClassByFullName("ifapp.Game.Input", "InputManager");

            if (_inputClass == IntPtr.Zero)
            {
                _inputClass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(
                    "Game.Input.dll", "ifapp.Game.Input", "InputManager");
            }

            if (_inputClass == IntPtr.Zero)
            {
                ModLog.Warn("replay: the game's InputManager was not found; " +
                                   "Shift and Tab will do nothing");
                return false;
            }

            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(_inputClass);
            _instanceOffset = FieldOffset(_inputClass, "Instance", InstanceFallback);
            _keyboardStateOffset = FieldOffset(_inputClass, "currentKeyboardState", KeyboardStateFallback);

            try
            {
                IntPtr statics = FieldResolver.Statics(_inputClass);
                IntPtr instance = statics == IntPtr.Zero ? IntPtr.Zero : Memory.Ptr(statics + _instanceOffset);

                if (Memory.LooksLikeObject(instance))
                {
                    IntPtr state = instance + _keyboardStateOffset;
                }
            }
            catch (Exception)
            {
            }

            return _instanceOffset >= 0 && _keyboardStateOffset >= 0;
        }

        private static int FieldOffset(IntPtr klass, string name, int fallback)
        {
            try
            {
                IntPtr field = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, name);
                if (field == IntPtr.Zero) return fallback;
                return (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        internal static void Poll()
        {
            if (_tabFrames > 0) _tabFrames++;

            if (_returnRequested)
            {
                _returnRequested = false;

                try
                {
                    ReturnToResults();
                }
                catch (Exception e)
                {
                    ModLog.Error("replay: the return transition threw: " + ModLog.Describe(e));
                }
            }

            if (_finishing)
            {
                _finishFrames++;

                if (!_seeked && _finishFrames == 30)
                {
                    _seeked = AudioOffset.FinishNow();
                    if (_seeked)
                        {
                        }
                }

                if (_finishFrames > FinishFrames)
                {
                    _finishing = false;
                        ModLog.Warn("replay: the song never ended after Tab; the replay is left " +
                                       "where it stopped");
                }
            }

            if (_scene == IntPtr.Zero) return;

            try
            {
                if (!Memory.LooksLikeObject(_scene))
                {
                    Forget();
                    return;
                }

                if (_retry == IntPtr.Zero || !Memory.LooksLikeObject(_retry))
                {
                    Forget();
                    return;
                }

                CheckShift();
            }
            catch (Exception e)
            {
                ModLog.Warn("replay: the retry label could not be updated: " + ModLog.Describe(e));
            }
        }

        internal static void RetryStarting()
        {
            if (!ReplayCore.HasRecording) return;
            if (!ShiftHeld) return;
            ReplayCore.Pending = true;
        }

        internal static bool ShiftHeld => KeyDown(LeftShiftKey) || KeyDown(RightShiftKey);

        private static void CheckShift()
        {
            bool shift = ShiftHeld;
            if (shift != _lastShift)
            {
                _lastShift = shift;
            }

            bool want = shift && ReplayCore.HasRecording;
            if (want == _swapped) return;
            if (want) Swap(true);
            else Swap(false);
        }

        private static void UpdatePostfix(Il2CppObjectBase __instance)
        {
            try
            {
                IntPtr scene = HarmonyHooks.Pointer(__instance);
                if (!Memory.LooksLikeObject(scene)) return;

                if (!_updateSeen)
                {
                    _updateSeen = true;
                }

                if (scene != _scene) ResultsStarted(scene);
                if (scene != _scene) return;
                CheckShift();
            }
            catch (Exception e)
            {
                ModLog.Warn("replay: the retry label could not be updated: " + ModLog.Describe(e));
            }
        }

        private static void ClickedPrefix(Il2CppObjectBase __instance)
        {
            try
            {
                if (_scene == IntPtr.Zero || _retry == IntPtr.Zero) return;
                if (HarmonyHooks.Pointer(__instance) != _retry) return;
                bool shift = KeyDown(LeftShiftKey) || KeyDown(RightShiftKey);
                if (!shift || !ReplayCore.HasRecording) return;
                ReplayCore.Pending = true;
            }
            catch (Exception e)
            {
                ModLog.Warn("replay: the retry press could not be read: " + ModLog.Describe(e));
            }
        }

        private static void Swap(bool replay)
        {
            if (replay)
            {
                if (!ResolveTexts())
                {
                    if (!_labelWarned)
                    {
                        _labelWarned = true;
                        ModLog.Warn("replay: the retry label texts were not found; " +
                                           "Shift+retry still works, only the label stays as it is");
                    }
                    return;
                }

                if (_originals == null) _originals = new string[_texts.Length];
                int written = 0;
                for (int i = 0; i < _texts.Length; i++)
                {
                    if (_originals[i] == null) _originals[i] = GetText(_texts[i]);
                    if (AutoResults.SetText(_texts[i], "回放")) written++;
                }

                _swapped = true;
                string was = _originals.Length > 0 ? _originals[0] : null;
                return;
            }

            if (_originals != null)
            {
                for (int i = 0; i < _texts.Length && i < _originals.Length; i++)
                {
                    if (_originals[i] == null) continue;
                    AutoResults.SetText(_texts[i], _originals[i]);
                }
            }

            _swapped = false;
        }

        private static bool ResolveTexts()
        {
            if (_texts != null) return _texts.Length > 0;
            if (!Memory.LooksLikeObject(_scene)) return false;
            int mappingOffset = FieldResolver.Lookup("ResultsScene", "textMapping");
            if (mappingOffset < 0) mappingOffset = TextMappingFallback;
            IntPtr mapping = Memory.Ptr(_scene + mappingOffset);
            int textsOffset = FieldResolver.Lookup("ResultsSceneTextMapping", "retryButtonTexts");
            if (textsOffset < 0) textsOffset = RetryTextsFallback;

            IntPtr array = Memory.LooksLikeObject(mapping)
                ? Memory.Ptr(mapping + textsOffset)
                : IntPtr.Zero;

            int length = Memory.LooksLikeObject(array)
                ? Memory.I32(array + Offsets.Runtime.ArrayLength)
                : -1;

            if (!_labelProbed)
            {
                _labelProbed = true;
            }

            var list = new List<IntPtr>(length > 0 && length <= 16 ? length : 2);

            if (length > 0 && length <= 16)
            {
                for (int i = 0; i < length; i++)
                {
                    IntPtr text = Memory.Ptr(array + Offsets.Runtime.ArrayDataOffset + i * 8);
                    if (Memory.LooksLikeObject(text) && !list.Contains(text)) list.Add(text);
                }
            }

            FromButton(list);

            if (_labelProbed && list.Count > 0)
            {
                for (int i = 0; i < list.Count && i < 8; i++)
                    {
                    }
            }

            _texts = list.ToArray();
            return _texts.Length > 0;
        }

        private static void FromButton(List<IntPtr> list)
        {
            if (!Memory.LooksLikeObject(_retry)) return;

            try
            {
                GameObject root = new Component(_retry).gameObject;
                if (root == null) return;

                foreach (Component part in root.GetComponentsInChildren<Component>(true))
                {
                    if (part == null) continue;
                    IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(part.Pointer);
                    if (klass == IntPtr.Zero) continue;
                    if (Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(klass) != "FastText") continue;

                    if (Memory.LooksLikeObject(part.Pointer) && !list.Contains(part.Pointer))
                        list.Add(part.Pointer);
                }
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("replay: the retry button's texts could not be read: " +
                                   ModLog.Describe(e));
            }
        }

        private static bool Active(IntPtr text)
        {
            try
            {
                return new Component(text).gameObject.activeInHierarchy;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetText(IntPtr text)
        {
            if (_getText == IntPtr.Zero)
            {
                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(
                    "FastText.dll", "FastText", "FastText");

                if (klass != IntPtr.Zero)
                {
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);
                    _getText = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                        klass, "GetText", 0);
                }

                if (_getText == IntPtr.Zero)
                {
                    ModLog.Warn("replay: FastText.GetText was not found; the retry label " +
                                       "cannot be restored after Shift is released");
                    return null;
                }
            }

            IntPtr raised = IntPtr.Zero;
            IntPtr result = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(
                _getText, text, null, ref raised);
            return raised == IntPtr.Zero ? Memory.Text(result) : null;
        }
    }
}
