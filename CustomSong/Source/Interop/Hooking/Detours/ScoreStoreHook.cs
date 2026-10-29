using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe partial class Hooks
    {
        private const string ResultsImage = "Game.Data.dll";
        private const string ResultsNamespace = "ifapp.Game.Data";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte ReadResultFn(IntPtr results, IntPtr songInfo, byte difficulty,
                                           IntPtr result, IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte ReadResultFullFn(IntPtr results, IntPtr songInfo, byte difficulty,
                                               IntPtr score, IntPtr shiny, IntPtr perfect, IntPtr far,
                                               IntPtr miss, IntPtr lamp, IntPtr rank, IntPtr clear,
                                               IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int UpdateResultFn(IntPtr results, IntPtr songInfo, IntPtr newResult,
                                            IntPtr existingScore, IntPtr methodInfo);
        private static NativeHook<ReadResultFn> _readResult;
        private static ReadResultFn _readResultTramp;
        private static NativeHook<ReadResultFullFn> _readResultFull;
        private static ReadResultFullFn _readResultFullTramp;
        private static NativeHook<UpdateResultFn> _updateResult;
        private static UpdateResultFn _updateResultTramp;
        private const int NoChange = 2;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte UpdateEncounterFn(IntPtr results, IntPtr newResult, IntPtr methodInfo);
        private static NativeHook<UpdateEncounterFn> _updateEncounter;
        private static UpdateEncounterFn _updateEncounterTramp;

        private static bool InstallScoreStore()
        {
            try
            {
                IntPtr klass = FieldResolver.ClassPointer("GameResultsV4");
                if (klass == IntPtr.Zero)
                    klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(ResultsImage, ResultsNamespace,
                                                                        "GameResultsV4");
                IntPtr read = ScoreStore.MethodOn(klass, "TryGetResult", 3);
                IntPtr readFull = ScoreStore.MethodOn(klass, "TryGetResult", 10);
                IntPtr update = ScoreStore.MethodOn(klass, "TryUpdate", 3);
                IntPtr readCode = read == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)read;
                IntPtr readFullCode = readFull == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)readFull;
                IntPtr updateCode = update == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)update;

                if (readCode == IntPtr.Zero || readFullCode == IntPtr.Zero || updateCode == IntPtr.Zero)
                {
                    ModLog.Error("the game's result table could not be found; custom scores will go " +
                                      "into the game's own save file");
                    return false;
                }

                byte[] readPrologue = Prologue(readCode);
                _readResult = new NativeHook<ReadResultFn>
                {
                    Target = readCode,
                    Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, IntPtr, byte>)
                        &ReadResultDetour,
                };
                _readResult.Attach();
                _readResultTramp = _readResult.Trampoline;
                byte[] readFullPrologue = Prologue(readFullCode);
                _readResultFull = new NativeHook<ReadResultFullFn>
                {
                    Target = readFullCode,
                    Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, IntPtr, IntPtr,
                        IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, byte>)&ReadResultFullDetour,
                };
                _readResultFull.Attach();
                _readResultFullTramp = _readResultFull.Trampoline;
                byte[] updatePrologue = Prologue(updateCode);
                _updateResult = new NativeHook<UpdateResultFn>
                {
                    Target = updateCode,
                    Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int>)
                        &UpdateResultDetour,
                };
                _updateResult.Attach();
                _updateResultTramp = _updateResult.Trampoline;
                IntPtr encounterClass = FieldResolver.ClassPointer("EncounterResults");
                if (encounterClass == IntPtr.Zero)
                    encounterClass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(ResultsImage, ResultsNamespace,
                                                                                "EncounterResults");
                IntPtr encounter = ScoreStore.MethodOn(encounterClass, "UpdateEncounterResult", 1);
                IntPtr encounterCode = encounter == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)encounter;
                bool encounterOk = false;

                if (encounterCode == IntPtr.Zero)
                {
                    ModLog.Error("the game's encounter results could not be found; a custom song's " +
                                      "encounter result will be written into the game's own save");
                }
                else
                {
                    byte[] encounterPrologue = Prologue(encounterCode);
                    _updateEncounter = new NativeHook<UpdateEncounterFn>
                    {
                        Target = encounterCode,
                        Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>)
                            &UpdateEncounterDetour,
                    };
                    _updateEncounter.Attach();
                    _updateEncounterTramp = _updateEncounter.Trampoline;
                    encounterOk = Landed(Hook.EncounterResult, encounterCode, encounterPrologue);
                }

                return Landed(Hook.ResultRead, readCode, readPrologue)
                     & Landed(Hook.ResultReadFull, readFullCode, readFullPrologue)
                     & Landed(Hook.ResultUpdate, updateCode, updatePrologue)
                     & encounterOk;
            }
            catch (Exception e)
            {
                ModLog.Warn("the custom result hooks could not be installed: " + ModLog.Describe(e));
                return false;
            }
        }

        private const int PlayerScore = 0x50;

        internal static ulong StoredScore(IntPtr table, IntPtr songInfo, int difficulty)
        {
            if (_readResultTramp == null || table == IntPtr.Zero || songInfo == IntPtr.Zero) return 0;
            if (difficulty < 0 || difficulty > 0x80) return 0;
            byte* record = stackalloc byte[0x80];
            new Span<byte>(record, 0x80).Clear();

            if (_readResultTramp(table, songInfo, (byte)difficulty, (IntPtr)record, IntPtr.Zero) == 0)
                return 0;
            return *(ulong*)(record + PlayerScore);
        }

        private static void DetachScoreStore()
        {
            _updateEncounter?.Detach();
            _updateEncounter = null;
            _updateEncounterTramp = null;
            _updateResult?.Detach();
            _updateResult = null;
            _updateResultTramp = null;
            _readResultFull?.Detach();
            _readResultFull = null;
            _readResultFullTramp = null;
            _readResult?.Detach();
            _readResult = null;
            _readResultTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte ReadResultDetour(IntPtr results, IntPtr songInfo, byte difficulty,
                                             IntPtr result, IntPtr methodInfo)
        {
            ScoreStore.Reads++;
            IntPtr table = results;
            if (!Faulted)
            {
#if DEBUG
                long t0 = Now;
#endif
                try
                {
                    if (ScoreStore.IsOurs(songInfo))
                    {
                        if (!ScoreStore.Ready) return 0;
                        ScoreStore.Served++;
                        table = ScoreStore.Table;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.ResultRead, e);
                }
#if DEBUG
                TicksRead += Now - t0;
#endif
            }

            return _readResultTramp(table, songInfo, difficulty, result, methodInfo);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte ReadResultFullDetour(IntPtr results, IntPtr songInfo, byte difficulty,
                                                 IntPtr score, IntPtr shiny, IntPtr perfect, IntPtr far,
                                                 IntPtr miss, IntPtr lamp, IntPtr rank, IntPtr clear,
                                                 IntPtr methodInfo)
        {
            ScoreStore.Reads++;
            IntPtr table = results;
            if (!Faulted)
            {
#if DEBUG
                long t0 = Now;
#endif
                try
                {
                    if (ScoreStore.IsOurs(songInfo))
                    {
                        if (!ScoreStore.Ready) return 0;
                        ScoreStore.Served++;
                        table = ScoreStore.Table;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.ResultReadFull, e);
                }
#if DEBUG
                TicksRead += Now - t0;
#endif
            }

            return _readResultFullTramp(table, songInfo, difficulty, score, shiny, perfect, far, miss,
                                        lamp, rank, clear, methodInfo);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int UpdateResultDetour(IntPtr results, IntPtr songInfo, IntPtr newResult,
                                              IntPtr existingScore, IntPtr methodInfo)
        {
            ScoreStore.Updates++;
            bool ours = false;
            IntPtr table = results;
            if (!Faulted)
            {
                try
                {
                    if (ScoreStore.IsOurs(songInfo))
                    {
                        if (!ScoreStore.Ready) return NoChange;
                        ours = true;
                        table = ScoreStore.Table;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.ResultUpdate, e);
                }
            }

            if (Auto.AutoMark.Ran)
            {
                HandOverTheStoredScore(table, songInfo, newResult, existingScore);
                return NoChange;
            }

            int outcome;
            try
            {
                outcome = _updateResultTramp(table, songInfo, newResult, existingScore, methodInfo);
            }
            catch (Exception e)
            {
                ModLog.Error("the game's result table threw on a write: " + ModLog.Describe(e));
                return NoChange;
            }

            if (ours)
            {
                ScoreStore.Merged++;
                ScoreStore.Save();
            }

            return outcome;
        }

        private const int ResultDifficulty = 0x2;

        private static void HandOverTheStoredScore(IntPtr table, IntPtr songInfo, IntPtr newResult,
                                                   IntPtr target)
        {
            if (target == IntPtr.Zero || newResult == IntPtr.Zero) return;

            try
            {
                int difficulty = Memory.U8(newResult + ResultDifficulty);
                ulong best = StoredScore(table, songInfo, difficulty);
                if (best == 0)
                {
                    ModLog.Load($"autoplay: no stored score for difficulty flag {difficulty} " +
                                $"(table ours, song {Memory.U16(songInfo + Offsets.Song.Id)})");
                    return;
                }

                Memory.WriteI64(target, (long)best);
                ModLog.Load($"autoplay: the results screen is given the stored score {best} " +
                            $"(difficulty flag {difficulty})");
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("autoplay: the stored score could not be handed over: " + ModLog.Describe(e));
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte UpdateEncounterDetour(IntPtr results, IntPtr newResult, IntPtr methodInfo)
        {
            ScoreStore.EncounterWrites++;

            if (!Faulted)
            {
                try
                {
                    bool ours = false;
                    IntPtr playing = IntPtr.Zero;
#if DEBUG
                    string seen = "no payload";
#endif
                    if (CurrentSong.TryReadSong(out playing))
                    {
                        ours = ScoreStore.IsOurs(playing);
#if DEBUG
                        seen = $"song {Memory.U16(playing + Offsets.Song.Id)}, {(ours ? "ours" : "not ours")}";
#endif
                    }

                    if (Auto.AutoMark.Ran)
                    {
                        ScoreStore.EncounterRan = true;
                        ScoreStore.EncounterDropped++;
                        ModLog.Load("autoplay: the encounter result is not written");
                        return 0;
                    }

                }
                catch (Exception e)
                {
                    Fault(Hook.EncounterResult, e);
                }
            }

            try
            {
                return _updateEncounterTramp(results, newResult, methodInfo);
            }
            catch (Exception e)
            {
                ModLog.Error("the game's encounter table threw on a write: " + ModLog.Describe(e));
                return 0;
            }
        }
    }
}
