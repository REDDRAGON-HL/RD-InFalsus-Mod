extern alias UnityEngineCore;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static class AudioOffset
    {
        private const int ControllerChannel = 0x10;
        private const uint TimeUnitMs = 1;

        private sealed class Channel
        {
            internal IntPtr Controller;
            internal bool Sought;
            internal bool SoughtAtZero;
            internal bool Released;
        }

        private static readonly List<Channel> _channels = new List<Channel>();
        private static IntPtr _module;
        private static SetPositionFn _setPosition;
        private static SetPausedFn _setPaused;
        private static int _failures;
        private static int _reports;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetPositionFn(IntPtr channel, uint position, uint timeUnit);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetPausedFn(IntPtr channel, int paused);

        internal static void Install()
        {
            bool ok = HarmonyHooks.Patch(
                HarmonyHooks.Target("_zf", "_RFA", "_J"),
                null,
                HarmonyHooks.Handler(typeof(AudioOffset), nameof(Capture)));
            ModLog.Load($"audiooffset: {(ok ? 1 : 0)}/1 hook installed");
        }

        private static void Capture(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase __result,
                                    bool __6)
        {
            IntPtr controller = __result != null ? __result.Pointer : IntPtr.Zero;
            if (__6 || !Memory.LooksLikeObject(controller)) return;

            foreach (Channel known in _channels)
                if (known.Controller == controller) return;
            _channels.Add(new Channel { Controller = controller });
        }

        internal static void Tick(double nowMs)
        {
            if (_channels.Count == 0) return;
            int offset = Offset();
            if (offset == 0) return;

            foreach (Channel state in _channels)
            {
                if (!Memory.LooksLikeObject(state.Controller)) continue;
                IntPtr channel = Memory.Ptr(state.Controller + ControllerChannel);
                if (!Memory.LooksLikeObject(channel)) continue;

                if (offset < 0)
                {
                    uint seek = (uint)Math.Min(int.MaxValue, -offset);
                    if (!state.Sought && nowMs >= -250.0)
                    {
                        Seek(channel, seek);
                        state.Sought = true;
                    }
                    if (!state.SoughtAtZero && nowMs >= 0.0)
                    {
                        Seek(channel, seek);
                        state.SoughtAtZero = true;
                    }
                }
                else if (nowMs < offset)
                {
                    Paused(channel, true);
                }
                else if (!state.Released)
                {
                    Paused(channel, false);
                    state.Released = true;

                    if (_reports++ < 6)
                        ModLog.Load($"audiooffset: released after {offset}ms");
                }
            }
        }

        private static int Offset()
        {
            foreach (SceneDirectives.Entry row in SceneDirectives.Current)
                if (row.Kind == SceneDirectives.Kind.AudioOffset) return row.Value;
            return 0;
        }

        internal static void Forget()
        {
            _channels.Clear();
            _reports = 0;
        }

        private static void Seek(IntPtr channel, uint position)
        {
            SetPositionFn call = SetPosition();
            if (call == null) return;
            int result = call(channel, position, TimeUnitMs);
            if (result != 0 && _failures++ < 3)
                ModLog.Warn($"audiooffset: Channel_SetPosition returned {result}");
        }

        private static void Paused(IntPtr channel, bool paused)
        {
            SetPausedFn call = SetPaused();
            if (call == null) return;
            int result = call(channel, paused ? 1 : 0);
            if (result != 0 && _failures++ < 3)
                ModLog.Warn($"audiooffset: Channel_SetPaused returned {result}");
        }

        private static SetPositionFn SetPosition()
        {
            if (_setPosition != null) return _setPosition;
            IntPtr export = Resolve("FMOD5_Channel_SetPosition");
            if (export == IntPtr.Zero) return null;
            _setPosition = (SetPositionFn)Marshal.GetDelegateForFunctionPointer(export, typeof(SetPositionFn));
            return _setPosition;
        }

        private static SetPausedFn SetPaused()
        {
            if (_setPaused != null) return _setPaused;
            IntPtr export = Resolve("FMOD5_Channel_SetPaused");
            if (export == IntPtr.Zero) return null;
            _setPaused = (SetPausedFn)Marshal.GetDelegateForFunctionPointer(export, typeof(SetPausedFn));
            return _setPaused;
        }

        private static IntPtr Resolve(string name)
        {
            if (_module == IntPtr.Zero)
            {
                _module = GetModuleHandle("fmodstudioL.dll");
                if (_module == IntPtr.Zero) _module = GetModuleHandle("fmodstudio.dll");
                if (_module == IntPtr.Zero)
                {
                    ModLog.WarnRelease("audiooffset: the fmodstudio DLL is not loaded; the offset is ignored");
                    return IntPtr.Zero;
                }
            }

            return GetProcAddress(_module, name);
        }

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);
    }
}
