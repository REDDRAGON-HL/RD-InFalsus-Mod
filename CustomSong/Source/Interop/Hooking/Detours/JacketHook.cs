using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe partial class Hooks
    {
        private const string Image = "Game.Common.dll";
        private const string Namespace = "ifapp.Game.Common";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ApAFn(IntPtr songData, IntPtr songInfo, byte large, IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MiaFn(IntPtr owner, IntPtr releaser, IntPtr reference, byte forceReload,
                                      IntPtr methodInfo);
        private static NativeHook<ApAFn> _apa;
        private static ApAFn _apaTramp;
        private static NativeHook<MiaFn> _mia;
        private static MiaFn _miaTramp;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ZoaFn(IntPtr songData, IntPtr chartInfo, byte large, IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr LiaFn(IntPtr owner, IntPtr reference, IntPtr methodInfo);
        private static NativeHook<ZoaFn> _zoa;
        private static ZoaFn _zoaTramp;
        private static NativeHook<LiaFn> _lia;
        private static LiaFn _liaTramp;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr UmaFn(IntPtr backgrounds, int background, IntPtr methodInfo);
        private static NativeHook<UmaFn> _uma;
        private static UmaFn _umaTramp;

        private static bool InstallJacket()
        {
            IntPtr reference = MethodResolver.ByName("SongData", "_apA");
            if (reference == IntPtr.Zero) return false;
            byte[] referencePrologue = Prologue(reference);
            _apa = new NativeHook<ApAFn>
            {
                Target = reference,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, IntPtr>)&ApADetour,
            };
            _apa.Attach();
            _apaTramp = _apa.Trampoline;
            bool ok = Landed(Hook.JacketReference, reference, referencePrologue);

            IntPtr load = MethodResolver.ByRuntime("AddressableHandleAutoReleaser", "_MIA",
                                                   Image, Namespace);
            if (load == IntPtr.Zero)
            {
                ModLog.Error("AddressableHandleAutoReleaser._MIA could not be found; custom " +
                                  "jackets will not be substituted onto the song list");
            }
            else
            {
                byte[] loadPrologue = Prologue(load);
                _mia = new NativeHook<MiaFn>
                {
                    Target = load,
                    Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte, IntPtr, IntPtr>)&MiaDetour,
                };
                _mia.Attach();
                _miaTramp = _mia.Trampoline;
                ok &= Landed(Hook.JacketMaterial, load, loadPrologue);
            }

            ok &= InstallChartReference();
            ok &= InstallChartLoader();
            ok &= InstallBackground();
            return ok;
        }

        private static bool InstallBackground()
        {
            IntPtr target = MethodResolver.ByName("GameplayBackgrounds", "_UmA");
            if (target == IntPtr.Zero)
            {
                ModLog.Warn("GameplayBackgrounds._UmA could not be found; a custom song will play " +
                                 "over the game's own background");
                return false;
            }

            byte[] prologue = Prologue(target);
            _uma = new NativeHook<UmaFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, IntPtr>)&UmaDetour,
            };
            _uma.Attach();
            _umaTramp = _uma.Trampoline;
            return Landed(Hook.Background, target, prologue);
        }

        private static bool InstallChartReference()
        {
            IntPtr target = MethodResolver.ByName("SongData", "_ZOA");
            if (target == IntPtr.Zero)
            {
                ModLog.Warn("SongData._ZOA could not be found; the loading screen and the " +
                                 "results screen will keep the game's fallback jacket");
                return false;
            }

            byte[] prologue = Prologue(target);
            _zoa = new NativeHook<ZoaFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, IntPtr>)&ZoaDetour,
            };
            _zoa.Attach();
            _zoaTramp = _zoa.Trampoline;
            return Landed(Hook.ChartReference, target, prologue);
        }

        private static bool InstallChartLoader()
        {
            IntPtr target = MethodResolver.ByRuntime("AddressableHandleAutoReleaser", "_LIA",
                                                     Image, Namespace);
            if (target == IntPtr.Zero)
            {
                ModLog.Warn("AddressableHandleAutoReleaser._LIA could not be found; anything " +
                                 "that loads a jacket through it will draw the game's fallback");
                return false;
            }

            byte[] prologue = Prologue(target);
            _lia = new NativeHook<LiaFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr>)&LiaDetour,
            };
            _lia.Attach();
            _liaTramp = _lia.Trampoline;
            return Landed(Hook.ChartMaterial, target, prologue);
        }

        private static void DetachJacket()
        {
            _uma?.Detach();
            _uma = null;
            _umaTramp = null;
            _lia?.Detach();
            _lia = null;
            _liaTramp = null;
            _zoa?.Detach();
            _zoa = null;
            _zoaTramp = null;
            _mia?.Detach();
            _mia = null;
            _miaTramp = null;
            _apa?.Detach();
            _apa = null;
            _apaTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr ApADetour(IntPtr songData, IntPtr songInfo, byte large, IntPtr methodInfo)
        {
            IntPtr fallback = _apaTramp(songData, songInfo, large, methodInfo);
            ApACalls++;

            if (!Faulted)
            {
#if DEBUG
                long t0 = Now;
#endif
                try
                {
                    if (JacketCatalog.Find(songInfo, out IntPtr material))
                    {
                        JacketCatalog.Arm(fallback, material, false);
                        ApAArmed++;
                    }
                    else JacketCatalog.Disarm();
                }
                catch (Exception e)
                {
                    JacketCatalog.Disarm();
                    Fault(Hook.JacketReference, e);
                }
#if DEBUG
                TicksApa += Now - t0;
#endif
            }

            return fallback;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr MiaDetour(IntPtr owner, IntPtr releaser, IntPtr reference, byte forceReload,
                                        IntPtr methodInfo)
        {
            MiaCalls++;

            if (!Faulted)
            {
#if DEBUG
                long t0 = Now;
#endif
                try
                {
                    if (JacketCatalog.TryClaim(reference, out IntPtr material, out bool _))
                    {
                        MiaClaimed++;
#if DEBUG
                        TicksMia += Now - t0;
#endif
                        return material;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.JacketMaterial, e);
                }
#if DEBUG
                TicksMia += Now - t0;
#endif
            }

            return _miaTramp(owner, releaser, reference, forceReload, methodInfo);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr ZoaDetour(IntPtr songData, IntPtr chartInfo, byte large, IntPtr methodInfo)
        {
            IntPtr fallback = _zoaTramp(songData, chartInfo, large, methodInfo);
            ZoaCalls++;

            if (!Faulted)
            {
                try
                {
                    if (JacketCatalog.FindByChart(chartInfo, out IntPtr material))
                    {
                        JacketCatalog.Arm(fallback, material, false);
                        ZoaArmed++;
                    }
                    else JacketCatalog.Disarm();
                }
                catch (Exception e)
                {
                    JacketCatalog.Disarm();
                    Fault(Hook.ChartReference, e);
                }
            }

            return fallback;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr LiaDetour(IntPtr owner, IntPtr reference, IntPtr methodInfo)
        {
            LiaCalls++;

            if (!Faulted)
            {
                try
                {
                    if (JacketCatalog.TryClaim(reference, out IntPtr material, out bool background))
                    {
                        LiaClaimed++;
                        return background
                            ? CloneBackground(owner, reference, material, methodInfo)
                            : material;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.ChartMaterial, e);
                }
            }

            return _liaTramp(owner, reference, methodInfo);
        }

        private static IntPtr CloneBackground(IntPtr owner, IntPtr reference, IntPtr ours, IntPtr methodInfo)
        {
            IntPtr game = _liaTramp(owner, reference, methodInfo);
            if (_backgroundCloneFailed) return game;
            string reason = null;
            if (!Memory.LooksLikeObject(game)) reason = "the game's own material did not load";
            IntPtr clone = reason == null ? BackgroundClone.Build(game, ours, out reason) : IntPtr.Zero;

            if (clone != IntPtr.Zero)
            {
                return clone;
            }

            _backgroundCloneFailed = true;
            ModLog.Error($"the background material could not be cloned from the game's own: " +
                              $"{reason}; this mod's background substitution is off for this session");
            return game;
        }

        private static bool _backgroundCloneFailed;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr UmaDetour(IntPtr backgrounds, int background, IntPtr methodInfo)
        {
            IntPtr fallback = _umaTramp(backgrounds, background, methodInfo);
            UmaCalls++;

            if (!Faulted)
            {
                try
                {
                    string why = null;
                    var entry = default(BackgroundCatalog.Entry);
                    bool armed = CurrentSong.TryReadSong(out IntPtr song) &&
                                 BackgroundCatalog.Find(song, CurrentSong.Difficulty(),
                                                        out entry, out why);

                    if (armed)
                    {
                        JacketCatalog.Arm(fallback, entry.Material, true);
                        UmaArmed++;
                    }
                    else
                    {
                        JacketCatalog.Disarm();
                        VideoBackground.Stop();

                        if (why != null)
                        {
                            UmaUnset++;
                            ModLog.Warn($"the in-play background was not replaced: {why}");
                        }
                    }
                }
                catch (Exception e)
                {
                    JacketCatalog.Disarm();
                    Fault(Hook.Background, e);
                }
            }

            return fallback;
        }
    }
}
