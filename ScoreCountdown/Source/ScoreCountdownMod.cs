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
    /// <summary>
    /// 把游玩界面左上角的分数换成**投影分**：从现在开始不再失误的话，最后结算会拿到的分。
    ///
    /// <para>
    /// 游戏本来显示的是**当前分**，会一路涨；投影分只在打出非 Exact+ 判定时才掉，掉的值正好是
    /// 「这个判定的得分」与「Exact+ 的得分」之差 —— 每颗音符的得分是 `1e8 / 物量`（按判定折算：
    /// Exact+ 与 Exact 算 1、Near 算 0.5、Break 算 0）再加 `Exact+ 才有的 1 分`。
    /// 开局它等于满分 `1e8 + 物量`，全 Exact+ 打完正好落到结算分。
    /// </para>
    /// <para>
    /// 算式**直接用游戏自己那套**（`_VD._Tz` 里 `A = 5e7`、`B = 1e8` 两个常量 + `+c210` 那一项；
    /// `_VD._Nz` 的分发表把每个判定记进哪个字段钉死了）：
    /// </para>
    /// <code>
    /// 已得分值 = Exact+ + Exact + 0.5 × Near
    /// 投影分   = ceil(1e8 × (已得分值 + 剩余物量) / 物量) + (Exact+ + 剩余物量)
    /// </code>
    /// <para>
    /// 取整是 **`ceil`**（游戏向上进一位），不是四舍五入 —— 这一条用存档 156 局历史成绩验过
    /// （`ceil` 155/156，`Round` 只有 80/156）。
    /// </para>
    /// <para>
    /// 写显示的办法：钩住 `ScoreContainer._pCA(long)` 之后**不调用原函数**，直接把字符串写进
    /// `scoreText` 里那两个 `FastText`。原函数只做两件事 —— 按 `_GpA`（+0x40）那道「只增不减」的
    /// 门槛决定要不要重绘、以及从 `_GpA` 到新值做一次 0.5 秒的数字滚动 —— 两件都是我们要绕开的：
    /// 投影分是往下走的，过不了那道门槛；而借门槛重绘就会带上那个滚动，屏幕上看起来正是
    /// 「判定时扣 1 分、窗口过去又加回来」。绕开之后 `_GpA` 一直保持原样，游戏自己的记账不受影响。
    /// </para>
    /// </summary>
    public class ScoreCountdownMod : MelonMod
    {
        /// <summary>游戏算分的基数。满分是它加上物量。</summary>
        private const long BaseScore = 100_000_000L;

        /// <summary>`ScoreContainer._pCA(long)`，1.0.5 的 RVA。按名字拿不到 MethodInfo 时才用。</summary>
        private const long RvaSetScore = 0x68AB80;

        /// <summary>`_VD._Tz(EncounterDetail&, Queue&lt;_Sh&gt;)`，每帧结算分的那个人。</summary>
        private const long RvaScoreTick = 0x5F48A0;

        private static readonly byte[] SetScorePrologue = { 0x53, 0x41, 0x54 };              // push rbx; push r12
        private static readonly byte[] ScoreTickPrologue = { 0x4C, 0x89, 0x44, 0x24, 0x18 }; // mov [rsp+0x18], r8

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

        private static int _scoreTextOffset;     // ScoreContainer.scoreText: FastText[]（正文 + 阴影）
        private static int _engineScoreOffset;   // _VD._UEA: _Uh，分数结算状态

        // _Uh 上的分数状态。字段名来自 _VD._Nz 的分发表（见 README 的对照表）。
        private static int _totalOffset;         // _cCb  物量
        private static int _judgedOffset;        // _CCb  已判定数
        private static int _exactPlusOffset;     // _iCb  Exact+（_FH grade 6）
        private static int _exactAOffset;        // _gCb  Exact 早（grade 4）
        private static int _exactBOffset;        // _GCb  Exact 晚（grade 5）
        private static int _nearAOffset;         // _hCb  Near 早（grade 2）
        private static int _nearBOffset;         // _HCb  Near 晚（grade 3）
        private static int _breakOffset;         // _ICb  Break（grade 1）

        private static IntPtr _state;            // 本曲的 _Uh，由 _VD._Tz 每次调用时抄下来
        private static IntPtr _container;        // 最近一次 _pCA 的 ScoreContainer
        private static long _painted = long.MinValue;   // 上次画上去的值
        private static bool _faulted;

        public override void OnLateInitializeMelon()
        {
            // 互操作程序集要等 MelonLoader 生成完才在，所以钩子装在这里而不是 OnInitializeMelon。
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

            // SetTextNonLocalized 有两个重载，要的是收 string 的那个
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

        /// <summary>
        /// 拿方法的代码地址。优先走运行时的 MethodInfo（游戏更新挪了函数也不会失效），拿不到就退回
        /// RVA —— 但退回前先核对开头几个字节，认不出来宁可不挂钩，也好过挂到别的函数上把游戏打崩。
        /// </summary>
        private IntPtr MethodPointer(Type type, string methodName, string signatureHint)
        {
            foreach (FieldInfo f in type.GetFields(AnyMember))
            {
                if (!f.Name.StartsWith($"NativeMethodInfoPtr_{methodName}_", StringComparison.Ordinal)) continue;
                if (signatureHint != null && !f.Name.Contains(signatureHint)) continue;

                IntPtr info = (IntPtr)f.GetValue(null);
                if (info == IntPtr.Zero) continue;

                IntPtr fn = Marshal.ReadIntPtr(info);   // MethodInfo 的第一个字段就是函数指针
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

        // ---------------------------------------------------------------- 分数状态

        /// <summary>
        /// `_VD._Tz` 的 detour。只做一件事：把 `_VD` 里的分数状态（`_UEA`）抄下来。
        /// 指针在调用这一刻一定是活的，所以不用管跨曲失效。
        /// </summary>
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

        /// <summary>`_Uh` 上这一刻的判定统计。</summary>
        private struct Tally
        {
            public int Total;        // 物量（判定点总数）
            public int Judged;       // 已判定数
            public int ExactPlus;    // Exact+
            public int Exact;        // Exact（早 + 晚）
            public int Near;         // Near（早 + 晚）
            public int Break;        // Break
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

        /// <summary>
        /// 投影分 = 把剩下的音符全部当 Exact+ 之后的结算分。
        ///
        /// <para>
        /// 结算分 = **`ceil(1e8 × 已得分值 / 物量) + Exact+个数`**，已得分值 = `Exact+ + Exact + 0.5 × Near`
        /// （`_VD._Tz` 里两个常量 5e7 / 1e8 与那一项 `+c210` 就是这么对应的）。把剩余物量整个加进
        /// 「已得分值」和「Exact+ 个数」就是投影分。
        /// </para>
        /// </summary>
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

        // ---------------------------------------------------------------- 显示

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
            // 刻意不调用原函数：它只负责按 `_GpA` 决定重绘 + 做数字滚动，两件都正是要绕开的。
            // 不调用也就等于 `_GpA` 一直保持原样，游戏自己的记账不受影响。
        }

        /// <summary>
        /// 把分数写进 `scoreText` 里的每个 `FastText`（正文 + 它的阴影，两项同一个数）。
        /// </summary>
        private static void Paint(IntPtr container, long shown)
        {
            IntPtr array = Marshal.ReadIntPtr(container + _scoreTextOffset);
            if (array == IntPtr.Zero) return;

            int count = Marshal.ReadInt32(array + 0x18);        // IL2CPP 数组长度
            if (count <= 0 || count > 8) return;

            IntPtr str = IL2CPP.ManagedStringToIl2Cpp(shown.ToString());
            for (int i = 0; i < count; i++)
            {
                IntPtr text = Marshal.ReadIntPtr(array + 0x20 + i * 8);
                if (text != IntPtr.Zero) _setText(text, str, 0, IntPtr.Zero);
            }
            _painted = shown;
        }

        /// <summary>
        /// 投影分在「打出一个非 Exact+ 判定」时才变，而那一下未必伴随 `_pCA`（它只跟当前分的变化走）；
        /// 加上换歌时 `_pCA(0)` 可能比 `_Tz` 先跑、拿到的还是上一首的状态。所以每帧自己比一次，
        /// 对不上就重画 —— 屏幕上那个数字不会停在旧值。
        /// </summary>
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

        /// <summary>只报一次：一个每帧都会走的钩子不该把日志刷爆。</summary>
        private static void Report(Exception e)
        {
            if (_faulted) return;
            _faulted = true;
            MelonLoader.MelonLogger.Error($"投影分的钩子里出错，接下来按原样显示：{e.GetType().Name}: {e.Message}");
        }

        // ---------------------------------------------------------------- 名字 → 运行时对象

        private const BindingFlags AnyMember =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        /// <summary>
        /// 按类名找 MelonLoader 生成的互操作类型。名字是混淆后的短名（`_VD`、`_Uh`），
        /// 全名带生成器的前缀和命名空间，所以只比 `Name`；<paramref name="mustHaveFieldPrefix"/>
        /// 用来在同名类型里挑出对的那个。
        /// </summary>
        private static Type InteropType(string name, string mustHaveFieldPrefix)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName = asm.GetName().Name;
#if BEPINEX
                // BepInEx 生成的游戏互操作程序集叫 Game.dll / Game.Data.dll / FastText.dll，
                // 没有 Il2Cpp 前缀；扫描全部程序集。
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

        /// <summary>字段偏移，问运行时要。问不到返回 -1 —— 偏移猜错就是读/写错地方，不猜。</summary>
        private static int FieldOffset(Type type, string fieldName)
        {
            FieldInfo pointer = type.GetField("NativeFieldInfoPtr_" + fieldName, AnyMember);
            IntPtr info = pointer == null ? IntPtr.Zero : (IntPtr)pointer.GetValue(null);
            return info == IntPtr.Zero ? -1 : (int)IL2CPP.il2cpp_field_get_offset(info);
        }

        /// <summary>对象头之后、组件自己的字段该在的范围。用来挡掉「拿不到偏移」返回的 -1 之类的值。</summary>
        private static bool Plausible(int offset) => offset >= 0x10 && offset < 0x1000;
    }
}
