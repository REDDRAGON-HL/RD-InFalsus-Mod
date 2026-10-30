using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe partial class Hooks
    {
        internal static long PlayGateCalls, PlayGateYes;
        internal static IntPtr SongSelect;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte PlayGateFn(IntPtr self, IntPtr methodInfo);
        private static NativeHook<PlayGateFn> _gate;
        private static PlayGateFn _gateTramp;

        private static bool InstallPlayGate()
        {
            IntPtr target = MethodResolver.ByRuntime("SongSelectScene", "_sN", "Game.dll", "ifapp.Game.Scenes");
            if (target == IntPtr.Zero)
            {
                ModLog.Warn("SongSelectScene._sN could not be resolved; whether a song may be " +
                                 "started will not be reported");
                return false;
            }

            byte[] prologue = Prologue(target);
            _gate = new NativeHook<PlayGateFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte>)&PlayGateDetour,
            };
            _gate.Attach();
            _gateTramp = _gate.Trampoline;
            return Landed(Hook.PlayGate, target, prologue);
        }

        private static void DetachPlayGate()
        {
            _gate?.Detach();
            _gate = null;
            _gateTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte PlayGateDetour(IntPtr self, IntPtr methodInfo)
        {
            byte answer = _gateTramp(self, methodInfo);
            PlayGateCalls++;
            if (answer != 0) PlayGateYes++;
            if (Memory.LooksLikeObject(self)) SongSelect = self;
            return answer;
        }

        internal static long SceneSwitchCalls;
        internal static int SceneSwitchLastState = int.MinValue;
        internal static IntPtr SceneSwitchWait;
        internal static string SceneSwitchWaitName;

        private static string ClassNameOf(IntPtr instance)
        {
            try
            {
                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(instance);
                if (klass == IntPtr.Zero) return null;
                return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(klass);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static string WaitProgress()
        {
            if (SceneSwitchWait == IntPtr.Zero) return null;
            if (SceneSwitchWaitName != "_Ig") return SceneSwitchWaitName;
            if (_igDoneByte == int.MinValue) _igDoneByte = FieldResolver.Field("_Ig", "_TwA");
            if (_igDoneByte < 0) return SceneSwitchWaitName;
            return $"_Ig/+0x{_igDoneByte:X}={Memory.U8(SceneSwitchWait + _igDoneByte)}";
        }

        private static int _igDoneByte = int.MinValue;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte MoveNextFn(IntPtr self, IntPtr methodInfo);
        private static NativeHook<MoveNextFn> _moveNext;
        private static MoveNextFn _moveNextTramp;

        private static bool InstallSceneSwitch()
        {
            IntPtr target = MethodResolver.ByRuntime("_CB", "MoveNext", "Game.dll", "ifapp.Game.Scenes");
            if (target == IntPtr.Zero)
            {
                ModLog.Warn("CoreScene._CB.MoveNext could not be resolved; where the scene switch " +
                                 "stops will not be reported");
                return false;
            }

            byte[] prologue = Prologue(target);
            _moveNext = new NativeHook<MoveNextFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte>)&MoveNextDetour,
            };
            _moveNext.Attach();
            _moveNextTramp = _moveNext.Trampoline;
            return Landed(Hook.SceneSwitch, target, prologue);
        }

        private static void DetachSceneSwitch()
        {
            _moveNext?.Detach();
            _moveNext = null;
            _moveNextTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte MoveNextDetour(IntPtr self, IntPtr methodInfo)
        {
            byte more = _moveNextTramp(self, methodInfo);
            SceneSwitchCalls++;

            if (more == 0) CustomSongCore.RescanSoon();

            if (!Memory.LooksLikeObject(self)) return more;
            int state = Memory.I32(self + StateField);
            if (state != SceneSwitchLastState)
            {
                SceneSwitchLastState = state;
            }

            IntPtr current = more == 0 ? IntPtr.Zero : Memory.Ptr(self + CurrentField);
            if (current == IntPtr.Zero)
            {
                SceneSwitchWait = IntPtr.Zero;
                SceneSwitchWaitName = null;
            }
            else if (current != SceneSwitchWait)
            {
                SceneSwitchWait = current;
                SceneSwitchWaitName = ClassNameOf(current);
            }

            return more;
        }

        private const int CurrentField = Offsets.Runtime.Coroutine.Current;
        private const int StateField = Offsets.Runtime.Coroutine.State;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SelectAwakeFn(IntPtr self);
        private static NativeHook<SelectAwakeFn> _selectAwake;
        private static SelectAwakeFn _selectAwakeTramp;

        internal static bool InstallSelectAwake()
        {
            IntPtr awake = MethodResolver.ByName("SongSelectScene", "Awake");
            if (awake == IntPtr.Zero)
                awake = MethodResolver.ByRuntime("SongSelectScene", "Awake", "Game.dll",
                                                 "ifapp.Game.Scenes");

            if (awake == IntPtr.Zero)
            {
                ModLog.Warn("SongSelectScene.Awake could not be resolved; a new song needs a restart");
                return false;
            }

            _selectAwake = new NativeHook<SelectAwakeFn>
            {
                Target = awake,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&SelectAwakeDetour,
            };
            _selectAwake.Attach();
            _selectAwakeTramp = _selectAwake.Trampoline;
            ModLog.Load("hot reload: the song-select page is watched");
            return true;
        }

        internal static void DetachSelectAwake()
        {
            _selectAwake?.Detach();
            _selectAwake = null;
            _selectAwakeTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void SelectAwakeDetour(IntPtr self)
        {
            try
            {
                CustomSongCore.HotReloadIfChanged();
            }
            catch (Exception e)
            {
                ModLog.Warn($"hot reload hook failed: {ModLog.Describe(e)}");
            }

            _selectAwakeTramp(self);
        }
    }
}
