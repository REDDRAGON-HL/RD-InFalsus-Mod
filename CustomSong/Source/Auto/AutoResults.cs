extern alias UnityEngineCore;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngineCore::UnityEngine;
using Il2CppInterop.Runtime.InteropTypes;

namespace CustomSong.Auto
{
    internal static class AutoMark
    {
        internal static bool Ran;
    }

    internal static unsafe class AutoResults
    {
        private const int RvaContainerAwake = 0x71E0C0;
        private delegate void StartFn(IntPtr self);
        private delegate void TextFn(IntPtr self, IntPtr text, byte updateType);
        private static NativeHook<StartFn> _containerAwake;
        private static StartFn _containerAwakeTramp;
        private static IntPtr _container;
        private static bool _decorated;
        private static TextFn _setText;
        private static int _drops;
        private static int _failed;
        private static int _assist;
        private static int _texts;
        private static int _left;
        private static int _right;
        private static int _encounterBacking;
        private static int _plainBacking;
        private static int _rewardBanner;
        private static int _battleLayer;
        private static bool _asked;
        private static bool _blockOn;
        private static long _addCalls, _addBlocked;
        internal static void BlockParticles(bool on) => _blockOn = on;

        private static bool BeforeAdd()
        {
            _addCalls++;
            if (_addCalls == 1) ModLog.Load("autoplay: the inventory's Add is live");
            if (!_blockOn) return true;
            _addBlocked++;
            return false;
        }

        internal static void Install()
        {
            if (_asked) return;
            _asked = true;
            ResolveFields();
            int installed = 0;
            int attempted = 0;
            void Count(bool ok) { attempted++; if (ok) installed++; }

            ResultsSkip.Install();
            bool practice = Loaded("InFalsusPractice");
            if (practice)
            {
                ModLog.Load("autoplay: In Falsus Practice owns ResultsScene.Start; " +
                            "the drop container marks the results screen");
            }
            else
            {
                MethodInfo start = HarmonyHooks.Target("ResultsScene", "Start", "ifapp.Game.Scenes");
                Count(HarmonyHooks.Patch(start,
                    HarmonyHooks.Handler(typeof(AutoResults), nameof(StartPrefix)),
                    HarmonyHooks.Handler(typeof(AutoResults), nameof(StartPostfix)),
                    HarmonyHooks.Handler(typeof(AutoResults), nameof(StartFinalizer))));
            }

            MethodInfo destroy = HarmonyHooks.Target("ResultsScene", "OnDestroy", "ifapp.Game.Scenes");
            Count(HarmonyHooks.Patch(destroy, null,
                HarmonyHooks.Handler(typeof(AutoResults), nameof(DestroyPostfix))));

            Count(Attach(ref _containerAwake, "IotaDropsContainer", "Awake", "ifapp.Game.Scenes", RvaContainerAwake,
                         (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&ContainerAwakeDetour, out _containerAwakeTramp));
            MethodInfo add = HarmonyHooks.Target("IotaInventory", "Add", "ifapp.Game.Data");
            bool addPatched = HarmonyHooks.Patch(add,
                HarmonyHooks.Handler(typeof(AutoResults), nameof(BeforeAdd)));
            IntPtr text = StringSetter();

            if (text != IntPtr.Zero)
                _setText = (TextFn)Marshal.GetDelegateForFunctionPointer(text, typeof(TextFn));

            ModLog.Load($"autoplay: results hooks {installed}/{attempted}" +
                        $", text={(text != IntPtr.Zero ? "yes" : "no")}" +
                        $", inventory={(addPatched ? "watched" : "not watched")}");
        }

        private static bool Loaded(string assembly)
        {
            try
            {
                foreach (System.Reflection.Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
                    if (loaded.GetName().Name == assembly) return true;
            }
            catch (Exception e)
            {
                ModLog.Warn("autoplay: the loaded plugins could not be listed: " + ModLog.Describe(e));
            }

            return false;
        }

        internal static unsafe IntPtr StringSetter()
        {
            try
            {
                if (!InteropTypeIndex.ByName().TryGetValue("FastText", out List<Type> types))
                    return IntPtr.Zero;

                foreach (Type type in types)
                {
                    foreach (System.Reflection.MethodInfo method in type.GetMethods(
                                 System.Reflection.BindingFlags.Public |
                                 System.Reflection.BindingFlags.NonPublic |
                                 System.Reflection.BindingFlags.Static |
                                 System.Reflection.BindingFlags.Instance |
                                 System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        if (method.Name != "SetTextNonLocalized") continue;
                        System.Reflection.ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length != 2 || parameters[0].ParameterType != typeof(string)) continue;

                        System.Reflection.FieldInfo field = Il2CppInterop.Common.Il2CppInteropUtils
                            .GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
                        if (field == null) continue;
                        IntPtr info = (IntPtr)field.GetValue(null);
                        if (info != IntPtr.Zero) return *(IntPtr*)info;
                    }
                }
            }
            catch (Exception e)
            {
                ModLog.Warn("autoplay: the results text setter could not be resolved: " + ModLog.Describe(e));
            }

            return IntPtr.Zero;
        }

        internal static void Uninstall()
        {
            _containerAwake?.Detach(); _containerAwake = null; _containerAwakeTramp = null;
            _asked = false;
        }

        private static void ResolveFields()
        {
            _drops = Offset("ResultsScene", "iotaDropsContainer", 0x130);
            _failed = Offset("IotaDropsContainer", "encounterFailedContainer", 0x78);
            _texts = Offset("IotaDropsContainer", "encounterFailedTexts", 0x80);
            _assist = Offset("IotaDropsContainer", "assistModeContainer", 0x88);
            _left = Offset("IotaDropsContainer", "leftButton", 0x60);
            _right = Offset("IotaDropsContainer", "rightButton", 0x68);
            _encounterBacking = Offset("ResultsScene", "encounterBacking", 0x88);
            _plainBacking = Offset("ResultsScene", "nonEncounterBacking", 0x90);
            _rewardBanner = Offset("ResultsScene", "rewardBanner", 0x128);
            _battleLayer = Offset("ResultsScene", "resultsBattleLayer", 0x138);
        }

        private static int Offset(string type, string field, int fallback)
        {
            int offset = FieldResolver.Lookup(type, field);
            return offset < 0 ? fallback : offset;
        }

        private static bool Attach<T>(ref NativeHook<T> hook, string type, string method, string namespaze,
                                      int rva, IntPtr detour, out T trampoline) where T : Delegate
        {
            trampoline = null;
            IntPtr target = MethodResolver.ByName(type, method);
            if (target == IntPtr.Zero)
                target = MethodResolver.ByRuntime(type, method, "Game.dll", namespaze);
            if (target == IntPtr.Zero)
            {
                IntPtr image = GameAssembly.Base;
                if (image != IntPtr.Zero) target = image + rva;
            }
            if (target == IntPtr.Zero) return false;
            hook = new NativeHook<T> { Target = target, Detour = detour };
            if (!hook.Attach()) return false;
            trampoline = hook.Trampoline;
            return true;
        }

        private static void StartPrefix(Il2CppObjectBase __instance)
        {
            if (AutoMark.Ran)
                ModLog.Load("autoplay: the results screen is starting" +
                            (ScoreStore.EncounterRan ? " (with a recollection)" : ""));
        }

        private static void StartPostfix(Il2CppObjectBase __instance)
        {
            if (!AutoMark.Ran) return;

            try
            {
                ModLog.Load("autoplay: the results screen is up");
                IntPtr scene = HarmonyHooks.Pointer(__instance);
                Plain(scene);
                Decorate(scene);
                _decorated = true;
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("autoplay: the results screen could not be marked: " + ModLog.Describe(e));
            }
        }

        private static Exception StartFinalizer(Exception __exception)
        {
            if (__exception == null) return null;

            ModLog.Error("autoplay: the results screen threw while it was being built: " +
                         ModLog.Describe(__exception));
            AutoMark.Ran = false;
            return null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void ContainerAwakeDetour(IntPtr self)
        {
            Hooks.Alive("IotaDropsContainer.Awake");
            _containerAwakeTramp(self);
            if (!AutoMark.Ran || _decorated) return;
            _container = Memory.LooksLikeObject(self) ? self : IntPtr.Zero;
        }

        internal static void Poll()
        {
            if (_decorated) return;
            if (_container == IntPtr.Zero) return;
            IntPtr container = _container;
            _container = IntPtr.Zero;
            if (!AutoMark.Ran) return;
            IntPtr scene = SceneAbove(container);

            try
            {
                ModLog.Load("autoplay: the results screen is up (from its drop container)");
                if (scene != IntPtr.Zero) Plain(scene);
                else ModLog.Load("autoplay: the screen object was not found above the drop container; " +
                                 "only the drop row is marked");
                DecorateAt(container);
                _decorated = true;
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("autoplay: the results screen could not be marked: " + ModLog.Describe(e));
            }
        }

        private static IntPtr SceneAbove(IntPtr component)
        {
            try
            {
                Transform at = new Component(component).transform;
                if (at == null) return IntPtr.Zero;
                while (at.parent != null) at = at.parent;
                GameObject root = at.gameObject;
                if (root == null) return IntPtr.Zero;

                foreach (Component part in root.GetComponentsInChildren<Component>(true))
                {
                    if (part == null) continue;
                    IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(part.Pointer);
                    if (klass == IntPtr.Zero) continue;
                    if (Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(klass) == "ResultsScene")
                        return part.Pointer;
                }
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("autoplay: the screen could not be found from its drop container: " +
                                   ModLog.Describe(e));
            }

            return IntPtr.Zero;
        }

        private static void DestroyPostfix(Il2CppObjectBase __instance) => OnLeavingResults();

        internal static void OnLeavingResults()
        {
            _decorated = false;
            _container = IntPtr.Zero;
            AutoMark.Ran = false;
            ScoreStore.EncounterRan = false;
            BlockParticles(false);
            CustomSongCore.RescanResults();
        }

        private static void Plain(IntPtr scene)
        {
            if (!Memory.LooksLikeObject(scene)) return;

            bool Show(int offset, bool on)
            {
                IntPtr component = Memory.Ptr(scene + offset);
                if (!Memory.LooksLikeObject(component)) return false;

                try
                {
                    GameObject go = new Component(component).gameObject;
                    if (go == null) return false;
                    if (go.activeSelf != on) go.SetActive(on);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            int changed = 0;
            if (Show(_plainBacking, true)) changed++;
            if (Show(_encounterBacking, false)) changed++;
            if (Show(_rewardBanner, false)) changed++;
            if (Show(_battleLayer, false)) changed++;
            ModLog.Load($"autoplay: {changed}/4 recollection object(s) put away");
        }

        private static void Decorate(IntPtr scene)
        {
            if (!Memory.LooksLikeObject(scene)) return;
            IntPtr container = Memory.Ptr(scene + _drops);
            if (!Memory.LooksLikeObject(container))
            {
                ModLog.WarnRelease("autoplay: the results screen has no drop container to mark");
                Report();
                return;
            }

            DecorateAt(container);
        }

        private static void DecorateAt(IntPtr container)
        {
            int shown = 0;
            shown += Show(container, _failed, true);
            shown += Show(container, _assist, false);
            shown += Show(container, _left, false);
            shown += Show(container, _right, false);
            shown += HideCells(container);
            int texts = WriteTexts(container);
            ModLog.Load($"autoplay: results marked ({shown} object(s) switched, {texts} text(s))");
            Report();
        }

        private static int HideCells(IntPtr container)
        {
            try
            {
                GameObject root = new Component(container).gameObject;
                if (root == null) return 0;
                int hidden = 0;
                foreach (Component cell in root.GetComponentsInChildren<Component>(true))
                {
                    if (cell == null) continue;
                    IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(cell.Pointer);
                    if (klass == IntPtr.Zero) continue;
                    if (Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(klass) != "IotaItemCell") continue;
                    GameObject go = cell.gameObject;
                    if (go == null || !go.activeSelf) continue;
                    go.SetActive(false);
                    hidden++;
                }

                return hidden;
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("autoplay: the reward cells could not be put away: " + ModLog.Describe(e));
                return 0;
            }
        }

        private static int Show(IntPtr container, int offset, bool on)
        {
            IntPtr component = Memory.Ptr(container + offset);
            if (!Memory.LooksLikeObject(component)) return 0;

            try
            {
                GameObject go = new Component(component).gameObject;
                if (go == null) return 0;
                if (go.activeSelf != on) go.SetActive(on);
                return 1;
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("autoplay: a results object could not be switched: " + ModLog.Describe(e));
                return 0;
            }
        }

        private static int WriteTexts(IntPtr container)
        {
            if (_setText == null) return 0;
            IntPtr array = Memory.Ptr(container + _texts);
            if (!Memory.LooksLikeObject(array)) return 0;
            int length = Memory.I32(array + AutoOffsets.Runtime.ListSize);
            if (length <= 0 || length > 16) return 0;
            IntPtr value = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp("AUTOPLAY");
            int written = 0;

            for (int i = 0; i < length; i++)
            {
                IntPtr text = Memory.Ptr(array + AutoOffsets.Runtime.ArrayDataOffset + i * 8);
                if (!Memory.LooksLikeObject(text)) continue;
                _setText(text, value, 0);
                written++;
            }

            return written;
        }

        private static void Report()
        {
            if (!CurrentSong.TryRead(out IntPtr song, out int difficulty))
            {
                ModLog.Load($"autoplay: results could not name the song (reads {ScoreStore.Reads}, " +
                            $"served {ScoreStore.Served})");
                return;
            }

            ushort id = Offsets.Ready ? Memory.U16(song + Offsets.Song.Id) : (ushort)0;
            bool ours = ScoreStore.IsOurs(song);
            ulong stored = ours ? Hooks.StoredScore(ScoreStore.Table, song, 1 << difficulty) : 0;

            ModLog.Load($"autoplay: results song {id} difficulty {difficulty} ours={ours} " +
                        $"stored={stored} (reads {ScoreStore.Reads}, served {ScoreStore.Served}, " +
                        $"updates {ScoreStore.Updates})");
        }
    }
}
