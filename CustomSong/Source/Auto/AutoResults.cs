extern alias UnityEngineCore;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngineCore::UnityEngine;

namespace CustomSong.Auto
{
    internal static class AutoMark
    {
        internal static bool Ran;
    }

    internal static unsafe class AutoResults
    {
        private const int RvaResultsStart = 0x727DF0;
        private const int RvaResultsDestroy = 0x727C20;
        private const int RvaIotaAdd = 0x4A6BF0;
        private delegate void StartFn(IntPtr self);

        [StructLayout(LayoutKind.Sequential)]
        private struct Iota
        {
            private long _a, _b, _c, _d;
        }

        private delegate void AddFn(IntPtr self, Iota instance, IntPtr methodInfo);
        private delegate void TextFn(IntPtr self, IntPtr text, byte updateType);
        private static NativeHook<StartFn> _start;
        private static StartFn _startTramp;
        private static NativeHook<StartFn> _destroy;
        private static StartFn _destroyTramp;
        private static NativeHook<AddFn> _add;
        private static AddFn _addTramp;
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
        private static IntPtr _noop;
        private static NativeHook<AddFn> _blocker;
        private static IntPtr _addTarget;

        internal static void BlockParticles(bool on)
        {
            try
            {
                if (on)
                {
                    if (_blocker != null) return;
                    if (_noop == IntPtr.Zero) _noop = Noop();
                    if (_noop == IntPtr.Zero) return;

                    if (_addTarget == IntPtr.Zero)
                    {
                        _addTarget = MethodResolver.ByName("IotaInventory", "Add");
                        if (_addTarget == IntPtr.Zero)
                            _addTarget = MethodResolver.ByRuntime("IotaInventory", "Add", "Game.Data.dll",
                                                                  "ifapp.Game.Data");
                        if (_addTarget == IntPtr.Zero && GameAssembly.Base != IntPtr.Zero)
                            _addTarget = GameAssembly.Base + RvaIotaAdd;
                    }

                    if (_addTarget == IntPtr.Zero) return;
                    _blocker = new NativeHook<AddFn> { Target = _addTarget, Detour = _noop };
                    _blocker.Attach();
                    ModLog.Load("autoplay: the inventory no longer takes particles");
                }
                else if (_blocker != null)
                {
                    _blocker.Detach();
                    _blocker = null;
                    ModLog.Load("autoplay: the inventory takes particles again");
                }
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("autoplay: the particle block could not be switched: " + ModLog.Describe(e));
                _blocker = null;
            }
        }

        private static IntPtr Noop()
        {
            IntPtr memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(64);
            if (memory == IntPtr.Zero) return IntPtr.Zero;
            byte* at = (byte*)memory;
            at[0] = 0xC3;
            uint old;
            if (!VirtualProtect(memory, 64, 0x40 , out old))
            {
                ModLog.WarnRelease("autoplay: the particle block could not be made executable");
                return IntPtr.Zero;
            }

            return memory;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr address, IntPtr size, uint protection, out uint old);

        private static bool VirtualProtect(IntPtr address, int size, uint protection, out uint old) =>
            VirtualProtect(address, (IntPtr)size, protection, out old);

        internal static void Install()
        {
            if (_asked) return;
            _asked = true;
            ResolveFields();
            int installed = 0;
            int attempted = 0;
            void Count(bool ok) { attempted++; if (ok) installed++; }

            ResultsSkip.Install();

            Count(Attach(ref _start, "ResultsScene", "Start", "ifapp.Game.Scenes", RvaResultsStart,
                         (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&StartDetour, out _startTramp));

            Count(Attach(ref _destroy, "ResultsScene", "OnDestroy", "ifapp.Game.Scenes", RvaResultsDestroy,
                         (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&DestroyDetour, out _destroyTramp));
            IntPtr text = StringSetter();

            if (text != IntPtr.Zero)
                _setText = (TextFn)Marshal.GetDelegateForFunctionPointer(text, typeof(TextFn));

            ModLog.Load($"autoplay: results hooks {installed}/{attempted}" +
                        $", text={(text != IntPtr.Zero ? "yes" : "no")}" +
                        "");
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
            _start?.Detach(); _start = null; _startTramp = null;
            _add?.Detach(); _add = null; _addTramp = null;
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
            hook.Attach();
            trampoline = hook.Trampoline;
            return true;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void AddDetour(IntPtr self, Iota instance, IntPtr methodInfo)
        {
            if (AutoMark.Ran) return;

            try
            {
                _addTramp(self, instance, methodInfo);
            }
            catch (Exception e)
            {
                ModLog.Error("autoplay: the inventory threw while taking a particle: " +
                             ModLog.Describe(e));
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void StartDetour(IntPtr self)
        {
            try
            {
                _startTramp(self);
            }
            catch (Exception e)
            {
                ModLog.Error("autoplay: the results screen threw while it was being built: " +
                             ModLog.Describe(e));
                AutoMark.Ran = false;
                return;
            }

            if (!AutoMark.Ran) return;

            try
            {
                Plain(self);
                Decorate(self);
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("autoplay: the results screen could not be marked: " + ModLog.Describe(e));
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void DestroyDetour(IntPtr self)
        {
            try
            {
                _destroyTramp(self);
            }
            catch (Exception e)
            {
                ModLog.Error("autoplay: the results screen threw while it was going away: " +
                             ModLog.Describe(e));
            }
            AutoMark.Ran = false;
            ScoreStore.EncounterRan = false;
            BlockParticles(false);
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
        }

        private static void Decorate(IntPtr scene)
        {
            if (!Memory.LooksLikeObject(scene)) return;
            IntPtr container = Memory.Ptr(scene + _drops);
            if (!Memory.LooksLikeObject(container))
            {
                ModLog.WarnRelease("autoplay: the results screen has no drop container to mark");
                    return;
            }

            int shown = 0;
            shown += Show(container, _failed, true);
            shown += Show(container, _assist, false);
            shown += Show(container, _left, false);
            shown += Show(container, _right, false);
            shown += HideCells(container);
            int texts = WriteTexts(container);
            ModLog.Load($"autoplay: results marked ({shown} object(s) switched, {texts} text(s))");
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


    }
}
