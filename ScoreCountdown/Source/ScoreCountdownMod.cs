using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using MelonLoader;
using MelonLoader.NativeUtils;

[assembly: MelonInfo(typeof(InFalsusScoreCountdown.ScoreCountdownMod), "InFalsusScoreCountdown", "3.0.0", "infalsus", null)]
[assembly: MelonGame("lowiro", "infalsus")]

namespace InFalsusScoreCountdown
{
    public class ScoreCountdownMod : MelonMod
    {
        private const long BaseScore = 100_000_000L;

        private const long RvaSetScore = 0x68AB80;

        private const long RvaScoreTick = 0x5F48A0;

        private static readonly byte[] SetScorePrologue = { 0x53, 0x41, 0x54 };              
        private static readonly byte[] ScoreTickPrologue = { 0x4C, 0x89, 0x44, 0x24, 0x18 }; 

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetScoreFn(IntPtr self, long value, IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ScoreTickFn(IntPtr self, IntPtr encounter, IntPtr queue, IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetTextFn(IntPtr self, IntPtr text, byte updateType, IntPtr methodInfo);

        private static NativeHook<SetScoreFn> _setScoreHook;
        private static NativeHook<ScoreTickFn> _tickHook;
        private static ScoreTickFn _tickTramp;
        private static SetTextFn _setText;

        private static int _scoreTextOffset;     
        private static int _engineScoreOffset;   

        private static int _totalOffset;         
        private static int _judgedOffset;        
        private static int _exactPlusOffset;     
        private static int _exactAOffset;        
        private static int _exactBOffset;        
        private static int _nearAOffset;         
        private static int _nearBOffset;         
        private static int _breakOffset;         

        private static IntPtr _state;            
        private static IntPtr _container;        
        private static long _painted = long.MinValue;   
        private static bool _faulted;

        public override void OnLateInitializeMelon()
        {
            try
            {
                Install();
            }
            catch (Exception e)
            {
                LoggerInstance.Error($"挂钩失败，分数不会换成投影分：{e.GetType().Name}: {e.Message}");
            }
        }

        private unsafe void Install()
        {
            Type container = InteropType("ScoreContainer", "NativeFieldInfoPtr_scoreText");
            Type engine = InteropType("_VD", "NativeFieldInfoPtr__UEA");
            Type score = InteropType("_Uh", "NativeFieldInfoPtr__cCb");
            Type text = InteropType("FastText", "NativeMethodInfoPtr_SetTextNonLocalized_");
            if (container == null || engine == null || score == null || text == null)
            {
                LoggerInstance.Error("找不到 ScoreContainer / _VD / _Uh / FastText 的互操作类型，什么也不做");
                return;
            }

            _scoreTextOffset = FieldOffset(container, "scoreText");
            _engineScoreOffset = FieldOffset(engine, "_UEA");
            _totalOffset = FieldOffset(score, "_cCb");
            _judgedOffset = FieldOffset(score, "_CCb");
            _exactPlusOffset = FieldOffset(score, "_iCb");
            _exactAOffset = FieldOffset(score, "_gCb");
            _exactBOffset = FieldOffset(score, "_GCb");
            _nearAOffset = FieldOffset(score, "_hCb");
            _nearBOffset = FieldOffset(score, "_HCb");
            _breakOffset = FieldOffset(score, "_ICb");

            bool readable = Plausible(_scoreTextOffset) && Plausible(_engineScoreOffset)
                            && Plausible(_totalOffset) && Plausible(_judgedOffset)
                            && Plausible(_exactPlusOffset) && Plausible(_exactAOffset) && Plausible(_exactBOffset)
                            && Plausible(_nearAOffset) && Plausible(_nearBOffset) && Plausible(_breakOffset);
            if (!readable)
            {
                LoggerInstance.Error("字段偏移拿不到或不可信，什么也不做（不猜偏移去读写内存）");
                return;
            }

            IntPtr setText = MethodPointer(text, "SetTextNonLocalized", "String");
            if (setText == IntPtr.Zero)
            {
                LoggerInstance.Error("拿不到 FastText.SetTextNonLocalized(string)，什么也不做");
                return;
            }
            _setText = Marshal.GetDelegateForFunctionPointer<SetTextFn>(setText);

            IntPtr setScore = MethodPointer(container, "_pCA", null);
            if (setScore == IntPtr.Zero) setScore = FromRva(RvaSetScore, SetScorePrologue, "_pCA");
            if (setScore == IntPtr.Zero) return;

            IntPtr tick = MethodPointer(engine, "_Tz", null);
            if (tick == IntPtr.Zero) tick = FromRva(RvaScoreTick, ScoreTickPrologue, "_VD._Tz");

            _setScoreHook = new NativeHook<SetScoreFn>
            {
                Target = setScore,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, long, IntPtr, void>)&SetScoreDetour,
            };
            _setScoreHook.Attach();

            if (tick != IntPtr.Zero)
            {
                _tickHook = new NativeHook<ScoreTickFn>
                {
                    Target = tick,
                    Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&ScoreTickDetour,
                };
                _tickHook.Attach();
                _tickTramp = _tickHook.Trampoline;
            }

            LoggerInstance.Msg($"已挂钩（_pCA {setScore.ToInt64():X}，_VD._Tz {tick.ToInt64():X}）：" +
                               "分数改为显示投影分（从现在起不失误的结算分）");
            LoggerInstance.Msg($"偏移 scoreText +0x{_scoreTextOffset:X}，_UEA +0x{_engineScoreOffset:X}，" +
                               $"_cCb +0x{_totalOffset:X}，_CCb +0x{_judgedOffset:X}，" +
                               $"_iCb +0x{_exactPlusOffset:X}，_gCb +0x{_exactAOffset:X}，" +
                               $"_GCb +0x{_exactBOffset:X}，_hCb +0x{_nearAOffset:X}，" +
                               $"_HCb +0x{_nearBOffset:X}，_ICb +0x{_breakOffset:X}");
        }

        private IntPtr MethodPointer(Type type, string methodName, string signatureHint)
        {
            foreach (FieldInfo f in type.GetFields(AnyMember))
            {
                if (!f.Name.StartsWith($"NativeMethodInfoPtr_{methodName}_", StringComparison.Ordinal)) continue;
                if (signatureHint != null && !f.Name.Contains(signatureHint)) continue;

                IntPtr info = (IntPtr)f.GetValue(null);
                if (info == IntPtr.Zero) continue;

                IntPtr fn = Marshal.ReadIntPtr(info);   
                if (fn != IntPtr.Zero) return fn;
            }
            return IntPtr.Zero;
        }

        private IntPtr FromRva(long rva, byte[] prologue, string label)
        {
            IntPtr module = GetModuleHandle("GameAssembly.dll");
            if (module == IntPtr.Zero)
            {
                LoggerInstance.Error("GameAssembly.dll 还没加载，挂不了钩");
                return IntPtr.Zero;
            }

            IntPtr target = (IntPtr)(module.ToInt64() + rva);
            for (int i = 0; i < prologue.Length; i++)
            {
                if (Marshal.ReadByte(target, i) != prologue[i])
                {
                    LoggerInstance.Error($"{label} 的 RVA 0x{rva:X} 处机器码对不上（游戏大概更新过），不挂钩");
                    return IntPtr.Zero;
                }
            }
            LoggerInstance.Warning($"{label} 用的是 1.0.5 的 RVA 0x{rva:X}，游戏更新后它可能已经失效");
            return target;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void ScoreTickDetour(IntPtr self, IntPtr encounter, IntPtr queue, IntPtr methodInfo)
        {
            try
            {
                IntPtr state = Marshal.ReadIntPtr(self + _engineScoreOffset);
                if (state != IntPtr.Zero && state != _state)
                {
                    _state = state;
                    if (ReadState(state, out Tally t))
                        MelonLoader.MelonLogger.Msg($"本曲物量 {t.Total}，满分 {BaseScore + t.Total}");
                }
            }
            catch (Exception e)
            {
                Report(e);
            }

            _tickTramp(self, encounter, queue, methodInfo);
        }

        private struct Tally
        {
            public int Total;        
            public int Judged;       
            public int ExactPlus;    
            public int Exact;        
            public int Near;         
            public int Break;        
        }

        private static bool ReadState(IntPtr state, out Tally t)
        {
            t = default;
            t.Total = Marshal.ReadInt32(state + _totalOffset);
            t.Judged = Marshal.ReadInt32(state + _judgedOffset);
            t.ExactPlus = Marshal.ReadInt32(state + _exactPlusOffset);
            t.Exact = Marshal.ReadInt32(state + _exactAOffset) + Marshal.ReadInt32(state + _exactBOffset);
            t.Near = Marshal.ReadInt32(state + _nearAOffset) + Marshal.ReadInt32(state + _nearBOffset);
            t.Break = Marshal.ReadInt32(state + _breakOffset);

            return t.Total > 0 && t.Total <= 1_000_000
                   && t.Judged >= 0 && t.Judged <= t.Total
                   && t.ExactPlus >= 0 && t.Exact >= 0 && t.Near >= 0 && t.Break >= 0
                   && t.ExactPlus + t.Exact + t.Near + t.Break <= t.Total;
        }

        private static bool Projection(out long shown)
        {
            shown = 0;
            IntPtr state = _state;
            if (state == IntPtr.Zero || !ReadState(state, out Tally t)) return false;

            long remaining = t.Total - t.Judged;
            double earned = t.ExactPlus + t.Exact + t.Near * 0.5 + remaining;
            double projected = BaseScore * earned / t.Total + (t.ExactPlus + remaining);
            if (double.IsNaN(projected) || projected < 0) return false;

            shown = (long)Math.Ceiling(projected);
            return true;
        }


        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void SetScoreDetour(IntPtr self, long value, IntPtr methodInfo)
        {
            try
            {
                _container = self;
                if (Projection(out long shown)) Paint(self, shown);
            }
            catch (Exception e)
            {
                Report(e);
            }
        }

        private static void Paint(IntPtr container, long shown)
        {
            IntPtr array = Marshal.ReadIntPtr(container + _scoreTextOffset);
            if (array == IntPtr.Zero) return;

            int count = Marshal.ReadInt32(array + 0x18);        
            if (count <= 0 || count > 8) return;

            IntPtr str = IL2CPP.ManagedStringToIl2Cpp(shown.ToString());
            for (int i = 0; i < count; i++)
            {
                IntPtr text = Marshal.ReadIntPtr(array + 0x20 + i * 8);
                if (text != IntPtr.Zero) _setText(text, str, 0, IntPtr.Zero);
            }
            _painted = shown;
        }

        public override void OnLateUpdate()
        {
            if (_container == IntPtr.Zero) return;
            if (!Projection(out long projected) || projected == _painted) return;

            try
            {
                Paint(_container, projected);
            }
            catch (Exception e)
            {
                Report(e);
            }
        }

        private static void Report(Exception e)
        {
            if (_faulted) return;
            _faulted = true;
            MelonLoader.MelonLogger.Error($"投影分的钩子里出错，接下来按原样显示：{e.GetType().Name}: {e.Message}");
        }


        private const BindingFlags AnyMember =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private static Type InteropType(string name, string mustHaveFieldPrefix)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName = asm.GetName().Name;
#if BEPINEX
                if (asmName == null) continue;
#else
                if (asmName == null || !asmName.StartsWith("Il2Cpp", StringComparison.Ordinal)) continue;
#endif

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }

                foreach (Type t in types)
                {
                    if (t == null || t.Name != name) continue;
                    foreach (FieldInfo f in t.GetFields(AnyMember))
                    {
                        if (f.Name.StartsWith(mustHaveFieldPrefix, StringComparison.Ordinal)) return t;
                    }
                }
            }
            return null;
        }

        private static int FieldOffset(Type type, string fieldName)
        {
            FieldInfo pointer = type.GetField("NativeFieldInfoPtr_" + fieldName, AnyMember);
            IntPtr info = pointer == null ? IntPtr.Zero : (IntPtr)pointer.GetValue(null);
            return info == IntPtr.Zero ? -1 : (int)IL2CPP.il2cpp_field_get_offset(info);
        }

        private static bool Plausible(int offset) => offset >= 0x10 && offset < 0x1000;
    }
}
