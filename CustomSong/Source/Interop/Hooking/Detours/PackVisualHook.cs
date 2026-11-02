extern alias UnityEngineCore;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngineCore::UnityEngine;

namespace CustomSong
{
    internal static unsafe partial class Hooks
    {
        private const string PackImage = "Game.dll";
        private const string PackNamespace = "ifapp.Game";
        private static int CardsField = Offsets.Unresolved;
        private static int CardViewModel = Offsets.Unresolved;
        private static int PackDataAccess = Offsets.Unresolved;
        private static int ViewModelSong = Offsets.Unresolved;
        private static int LargeAssetsField = Offsets.Unresolved;
        private static int SmallAssetsField = Offsets.Unresolved;
        private static int OutlineAssetsField = Offsets.Unresolved;
        private static int DlcAssetsField = Offsets.Unresolved;
        private static int ModelPackInfo = Offsets.Unresolved;
        private static int ModelSongPack = Offsets.Unresolved;
        private static int ModelSongPackOwned = Offsets.Unresolved;
        private static int PackInfoId = Offsets.Unresolved;
        private static int ScenarioLabelField = Offsets.Unresolved;
        private static int DlcLabelField = Offsets.Unresolved;
        private static int OutlineVisualField = Offsets.Unresolved;
        private static int ModelField = Offsets.Unresolved;
        private static int StripsField = Offsets.Unresolved;
        private static int StripPageField = Offsets.Unresolved;
        private const int StripWindow = 11;
        private static int ModelSongArray = Offsets.Unresolved;
        private static bool _fieldsResolved;
        private static bool _fieldsMissing;

        private static bool ResolveFields()
        {
            if (_fieldsResolved) return true;
            if (_fieldsMissing) return false;
            PackDataAccess = FieldResolver.Field("PackVisualMemberLarge", "dataAccess");
            ViewModelSong = FieldResolver.Field("_SH", "_WEb");
            CardsField = FieldResolver.Field("PackVisualMemberLarge", "_Mf");
            CardViewModel = FieldResolver.Field("PackSongCardMember", "_if");
            ViewModelDifficulty = FieldResolver.Field("_SH", "_xEb");
            LargeAssetsField = FieldResolver.Field("PackVisualMemberLarge", "packSelectSceneAssets");
            SmallAssetsField = FieldResolver.Field("PackVisualMemberSmall", "packSelectSceneAssets");
            OutlineAssetsField = FieldResolver.Field("PackOutlineMember", "packSelectSceneAssets");
            DlcAssetsField = FieldResolver.Field("PackDLCVisualMemberSingle", "packSelectSceneAssets");
            ModelPackInfo = FieldResolver.Field("_iH", "_RDb");
            PackInfoId = FieldResolver.Lookup("PackInfo", "Id");
            ModelSongPack = FieldResolver.Field("_iH", "_hKb");
            ModelSongPackOwned = FieldResolver.Field("_iH", "_HKb");
            ScenarioLabelField = FieldResolver.Field("PackVisualMemberLarge", "scenarioTypeContainer");
            DlcLabelField = FieldResolver.Field("PackVisualMemberLarge", "DLCTypeContainer");
            OutlineVisualField = FieldResolver.Field("PackOutlineMember", "outlineVisual");
            ModelField = FieldResolver.Lookup("PackVisualMemberLarge", "_Lf");
            StripsField = FieldResolver.Lookup("PackVisualMemberLarge", "_Mf");
            StripPageField = FieldResolver.Lookup("PackVisualMemberLarge", "_Nf");
            ModelSongArray = FieldResolver.Lookup("_iH", "_sDb");

            if (PackDataAccess < 0 || ViewModelSong < 0 || CardsField < 0 ||
                CardViewModel < 0 || ViewModelDifficulty < 0 ||
                LargeAssetsField < 0 || SmallAssetsField < 0 ||
                OutlineAssetsField < 0 || DlcAssetsField < 0 ||
                ModelPackInfo < 0 || ModelSongPack < 0 || ModelSongPackOwned < 0 ||
                PackInfoId < 0)
            {
                _fieldsMissing = true;
                ModLog.Error("the pack's card fields could not be read by name in this build; the " +
                                  "cards will keep the difficulty they were first built with");
                return false;
            }

            _fieldsResolved = true;
            return true;
        }

        private static int ViewModelDifficulty = Offsets.Unresolved;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PackDifficultyFn(IntPtr self, byte difficulty, IntPtr methodInfo);
        private static NativeHook<PackDifficultyFn> _packDifficulty;
        private static PackDifficultyFn _packDifficultyTramp;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void> _buildCard;
        private static IntPtr _buildCardInfo;
        internal static long PackDifficultyCalls, PackCardsRebuilt;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void LargeCardFn(IntPtr self, IntPtr input, IntPtr model, int index);
        private static NativeHook<LargeCardFn> _largeCard;
        private static LargeCardFn _largeCardTramp;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SmallCardFn(IntPtr self, IntPtr dataAccess, IntPtr model, int index);
        private static NativeHook<SmallCardFn> _smallCard;
        private static SmallCardFn _smallCardTramp;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void OutlineFn(IntPtr self, IntPtr dataAccess, IntPtr input, IntPtr model, int index);
        private static NativeHook<OutlineFn> _outline;
        private static OutlineFn _outlineTramp;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DlcCardFn(IntPtr self, IntPtr dataAccess, IntPtr model, int index);
        private static NativeHook<DlcCardFn> _dlcCard;
        private static DlcCardFn _dlcCardTramp;
        private static readonly HashSet<IntPtr> _cardTables = new HashSet<IntPtr>();

        private static bool InstallPackVisual()
        {
            IntPtr info = MethodResolver.MethodInfoByRuntime("PackSongCardMember", "_pc", PackImage, PackNamespace);
            if (info == IntPtr.Zero)
            {
                ModLog.Warn("PackSongCardMember._pc could not be found; a difficulty change on the " +
                                 "pack screen will not reach the jackets until the screen is reloaded");
            }
            else
            {
                _buildCardInfo = info;
                _buildCard = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)(*(IntPtr*)info);
            }

            bool cards = InstallPackCards();
            IntPtr target = MethodResolver.ByRuntime("PackVisualMemberLarge", "_vc", PackImage, PackNamespace);
            if (target == IntPtr.Zero)
            {
                ModLog.Warn("PackVisualMemberLarge._vc could not be found; the jackets on the pack " +
                                 "screen will keep the difficulty they were first built with");
                return false;
            }

            byte[] prologue = Prologue(target);
            _packDifficulty = new NativeHook<PackDifficultyFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, byte, IntPtr, void>)&PackDifficultyDetour,
            };
            _packDifficulty.Attach();
            _packDifficultyTramp = _packDifficulty.Trampoline;
            return Landed(Hook.PackVisual, target, prologue) & cards;
        }

        private static bool InstallPackCards()
        {
            bool large = InstallCard("PackVisualMemberLarge", "_Ef",
                                     (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int, void>)&LargeCardDetour,
                                     out _largeCard, out _largeCardTramp);

            bool small = InstallCard("PackVisualMemberSmall", "_bC",
                                     (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int, void>)&SmallCardDetour,
                                     out _smallCard, out _smallCardTramp);

            bool outline = InstallCard("PackOutlineMember", "_xB",
                                       (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, int, void>)&OutlineDetour,
                                       out _outline, out _outlineTramp);
            bool button = InstallPackButton();

            bool dlc = InstallCard("PackDLCVisualMemberSingle", "_Xc",
                                   (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int, void>)&DlcCardDetour,
                                   out _dlcCard, out _dlcCardTramp);
            return large & small & outline & dlc & button;
        }

        private static IntPtr _stripCard;
        private static int _appliedPage = int.MinValue, _appliedStrips = -1;

        internal static void TickStripWindow()
        {
            if (_stripCard == IntPtr.Zero) return;
            if (!Memory.LooksLikeObject(_stripCard) || Faulted || !ResolveFields())
            {
                _stripCard = IntPtr.Zero;
                return;
            }

            ApplyStripWindow(_stripCard);
        }

        private static IntPtr ComponentOf(GameObject gameObject, ref IntPtr type,
                                          string namespaze, string name)
        {
            if (gameObject == null) return IntPtr.Zero;

            if (type == IntPtr.Zero)
            {
                IntPtr klass = MethodResolver.ClassByFullName(namespaze, name);
                if (klass == IntPtr.Zero) { SayComponent(name); return IntPtr.Zero; }

                type = Il2CppInterop.Runtime.IL2CPP.il2cpp_type_get_object(
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_type(klass));
                if (type == IntPtr.Zero) { SayComponent(name); return IntPtr.Zero; }
            }

            if (_getComponent == IntPtr.Zero)
            {
                _getComponent = MethodResolver.MethodInfoByRuntime(
                    "GameObject", "GetComponent", "UnityEngine.CoreModule.dll", "UnityEngine", 1);
                if (_getComponent == IntPtr.Zero) { SayComponent("GetComponent"); return IntPtr.Zero; }
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = type;
            IntPtr raised = IntPtr.Zero;
            IntPtr component = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(
                _getComponent, gameObject.Pointer, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
            {
                SayComponent(name);
                return IntPtr.Zero;
            }

            return Memory.LooksLikeObject(component) ? component : IntPtr.Zero;
        }

        private static IntPtr _getComponent, _textType;
        private static readonly HashSet<string> _componentReports = new HashSet<string>();

        private static void SayComponent(string what)
        {
            if (!_componentReports.Add(what)) return;
            ModLog.Warn($"the card's {what} component could not be looked up");
        }

        private static readonly HashSet<IntPtr> _stripsHidden = new HashSet<IntPtr>();

        private static void ApplyStripWindow(IntPtr card)
        {
            if (Faulted || !ResolveFields()) return;
            if (StripsField < 0 || StripPageField < 0 || ModelField < 0) return;
            IntPtr model = Memory.Ptr(card + ModelField);
            if (!Memory.LooksLikeObject(model) || !SongCatalog.IsCustomPack(PackId(model))) return;
            if (!Memory.TryList(Memory.Ptr(card + StripsField), out IntPtr items, out int count)) return;
            int first = Memory.I32(card + StripPageField);
            if (first == _appliedPage && count == _appliedStrips) return;
            _appliedPage = first;
            _appliedStrips = count;

            if (!_saidWindow)
            {
                _saidWindow = true;
                ModLog.Load($"strip window: {count} strips, first={first}, window={StripWindow}");
            }

            for (int i = 0; i < count; i++)
            {
                try
                {
                    IntPtr strip = Memory.Ptr(items + i * IntPtr.Size);
                    if (!Memory.LooksLikeObject(strip)) continue;
                    GameObject gameObject = new Component(strip).gameObject;
                    if (gameObject == null) continue;
                    bool visible = i >= first && i < first + StripWindow;
                    if (!visible)
                    {
                        if (gameObject.activeSelf && _stripsHidden.Add(strip))
                            gameObject.SetActive(false);
                    }
                    else if (_stripsHidden.Remove(strip))
                    {
                        gameObject.SetActive(true);
                    }
                }
                catch (Exception e)
                {
                    _stripCard = IntPtr.Zero;
                    if (_windowReported) continue;
                    _windowReported = true;
                    ModLog.Error("the pack card's strip window could not be applied: " +
                                 ModLog.Describe(e));
                }
            }
        }

        private static bool _windowReported;
        private static bool _saidWindow;

        private static bool InstallCard<T>(string type, string method, IntPtr detour,
                                           out NativeHook<T> hook, out T trampoline) where T : Delegate
        {
            hook = null;
            trampoline = null;
            IntPtr target = MethodResolver.ByRuntime(type, method, PackImage, PackNamespace);
            if (target == IntPtr.Zero)
            {
                ModLog.Warn($"{type}.{method} could not be found; its cards will draw whatever " +
                                 "pictures the game's own table holds");
                return false;
            }

            byte[] prologue = Prologue(target);
            hook = new NativeHook<T> { Target = target, Detour = detour };
            hook.Attach();
            trampoline = hook.Trampoline;
            return Landed(Hook.PackVisual, target, prologue);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void LargeCardDetour(IntPtr self, IntPtr input, IntPtr model, int index)
        {
            Alive(Hook.PackVisual);
            ApplyCardTable(self, LargeAssetsField, "large");
            _stripsHidden.Clear();
            _saidWindow = false;
            _appliedPage = int.MinValue;
            _largeCardTramp(self, input, model, index);
            ReportStripCount(self, model);
            ApplyStripWindow(self);
            ReportCardFonts(self);
            _stripCard = self;
        }

        private static bool _saidFonts;

        private static void ReportCardFonts(IntPtr card)
        {
            if (_saidFonts || Faulted || StripsField < 0) return;
            if (!Memory.LooksLikeObject(Memory.Ptr(card + ModelField))) return;
            if (!SongCatalog.IsCustomPack(PackId(Memory.Ptr(card + ModelField)))) return;
            if (!Memory.TryList(Memory.Ptr(card + StripsField), out IntPtr items, out int count)) return;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    IntPtr strip = Memory.Ptr(items + i * IntPtr.Size);
                    if (!Memory.LooksLikeObject(strip)) continue;
                    GameObject gameObject = new Component(strip).gameObject;
                    if (gameObject == null) continue;
                    _saidFonts = true;
                    SayFonts(gameObject.transform, 0);
                    return;
                }
                catch (Exception e)
                {
                    _saidFonts = true;
                    ModLog.Warn("the card's texts could not be read: " + ModLog.Describe(e));
                }
            }
        }

        private static void SayFonts(Transform at, int depth)
        {
            if (at == null || depth > 6 || _fontLines >= 30) return;

            try
            {
                GameObject gameObject = at.gameObject;
                IntPtr text = ComponentOf(gameObject, ref _textType, "FastText", "FastText");
                if (text != IntPtr.Zero)
                {
                    _fontLines++;
                    ModLog.Load($"card text font: '{gameObject.name}' family={Families(Memory.Ptr(text + 0x20))}");
                }

                int children = at.childCount;
                for (int i = 0; i < children; i++) SayFonts(at.GetChild(i), depth + 1);
            }
            catch (Exception)
            {
            }
        }

        private static int _fontLines;

        private static string Families(IntPtr family)
        {
            if (!Memory.LooksLikeObject(family)) return "?";
            IntPtr assets = Memory.Ptr(family + 0x20);
            if (!Memory.LooksLikeObject(assets)) return "?";
            int count = Memory.I32(assets + Offsets.Runtime.ArrayLength);
            if (count <= 0 || count > 8) return "?";
            var names = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                IntPtr asset = Memory.Ptr(assets + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                string identifier = Memory.LooksLikeObject(asset) ? Memory.Text(Memory.Ptr(asset + 0x18), 48) : null;
                names.Add(identifier ?? "null");
            }

            return string.Join(", ", names.ToArray());
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void SmallCardDetour(IntPtr self, IntPtr dataAccess, IntPtr model, int index)
        {
            Alive(Hook.PackVisual);
            ApplyCardTable(self, SmallAssetsField, "list card");
            ReportCardArt(self, model, index);
            _smallCardTramp(self, dataAccess, model, index);
        }

        private static bool _saidCards;

        private static void ReportCardArt(IntPtr card, IntPtr model, int index)
        {
            if (_saidCards) return;
            _saidCards = true;
            ModLog.Load($"song select: the pack list is being built (first card index {index})");
        }

        private static bool _saidOutline;

        private static void ReportOutlineArt(IntPtr card, IntPtr model)
        {
            if (_saidOutline) return;
            _saidOutline = true;
            ModLog.Load("song select: the progress list is being built");
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OutlineDetour(IntPtr self, IntPtr dataAccess, IntPtr input, IntPtr model,
                                          int index)
        {
            Alive(Hook.PackVisual);
            ApplyCardTable(self, OutlineAssetsField, "outline");
            _outlineTramp(self, dataAccess, input, model, index);
            ReportOutlineArt(self, model);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void DlcCardDetour(IntPtr self, IntPtr dataAccess, IntPtr model, int index)
        {
            Alive(Hook.PackVisual);
            ApplyCardTable(self, DlcAssetsField, "DLC card");
            _dlcCardTramp(self, dataAccess, model, index);
        }

        private static void ApplyCardTable(IntPtr card, int field, string which)
        {
            if (Faulted || field < 0) return;

            try
            {
                IntPtr assets = Memory.Ptr(card + field);
                if (!Memory.LooksLikeObject(assets)) return;

                if (_cardTables.Add(assets))
                    ModLog.Load($"pack visuals: the {which} card reads a table at 0x{assets.ToInt64():X}");
                CustomSongCore.ApplyVisualsTo(assets);
            }
            catch (Exception e)
            {
                Fault(Hook.PackVisual, e);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PackButtonFn(IntPtr self, IntPtr modelRef, IntPtr methodInfo);
        private static NativeHook<PackButtonFn> _packButton;
        private static PackButtonFn _packButtonTrampoline;

        private static bool InstallPackButton()
        {
            IntPtr target = MethodResolver.ByRuntime("PackSelectButtonContainer", "_Tn",
                                                     PackImage, PackNamespace);
            if (target == IntPtr.Zero)
            {
                ModLog.Warn("PackSelectButtonContainer._Tn could not be found; a custom pack's " +
                                 "enter button will keep saying it is a story chapter");
                return false;
            }

            byte[] prologue = Prologue(target);
            _packButton = new NativeHook<PackButtonFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&PackButtonDetour,
            };
            _packButton.Attach();
            _packButtonTrampoline = _packButton.Trampoline;
            return Landed(Hook.PackVisual, target, prologue);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PackButtonDetour(IntPtr self, IntPtr modelRef, IntPtr methodInfo)
        {
            Alive(Hook.PackVisual);
            bool raised = false;

            if (!Faulted && modelRef != IntPtr.Zero)
            {
                try
                {
                    IntPtr model = Memory.Ptr(modelRef);
                    if (ResolveFields() && Memory.LooksLikeObject(model) &&
                        SongCatalog.IsCustomPack(PackId(model)))
                    {
                        Memory.WriteU8(model + ModelSongPack, 1);
                        Memory.WriteU8(model + ModelSongPackOwned, 1);
                        raised = true;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.PackVisual, e);
                }
            }

            _packButtonTrampoline(self, modelRef, methodInfo);
            if (!raised) return;

            try
            {
                Memory.WriteU8(Memory.Ptr(modelRef) + ModelSongPack, 0);
            }
            catch (Exception e)
            {
                Fault(Hook.PackVisual, e);
            }
        }

        private static void ReportStripCount(IntPtr card, IntPtr model)
        {
            if (Faulted || !ResolveFields() || !Memory.LooksLikeObject(model)) return;
            if (StripsField < 0 || StripPageField < 0) return;
            int packId = PackId(model);
            bool ours = SongCatalog.IsCustomPack(packId);
            if (!ours && _reportedShipped) return;
            if (ours && !_reportedPacks.Add(packId)) return;
            if (!ours) _reportedShipped = true;
            IntPtr strips = Memory.Ptr(card + StripsField);
            int count = Memory.LooksLikeObject(strips) ? Memory.I32(strips + Offsets.Runtime.ArrayLength) : -1;

            ModLog.Load($"strip row pack {packId}{(ours ? " (ours)" : "")}: " +
                        $"strips={count} page={Memory.I32(card + StripPageField)} " +
                        $"songs={SongCount(model)}");
        }

        private static int SongCount(IntPtr model)
        {
            IntPtr songs = Memory.Ptr(model + ModelSongArray);
            return Memory.LooksLikeObject(songs) ? Memory.I32(songs + Offsets.Runtime.ArrayLength) : -1;
        }

        private static readonly HashSet<int> _reportedPacks = new HashSet<int>();
        private static bool _reportedShipped;

        private static int PackId(IntPtr model)
        {
            if (!Memory.LooksLikeObject(model) || PackInfoId < 0) return -1;
            return Memory.U16(model + ModelPackInfo + PackInfoId);
        }

        private static void DetachPackVisual()
        {
            _stripCard = IntPtr.Zero;
            _largeCard?.Detach();
            _largeCard = null;
            _largeCardTramp = null;
            _smallCard?.Detach();
            _smallCard = null;
            _smallCardTramp = null;
            _outline?.Detach();
            _outline = null;
            _outlineTramp = null;
            _dlcCard?.Detach();
            _dlcCard = null;
            _dlcCardTramp = null;
            _packButton?.Detach();
            _packButton = null;
            _packButtonTrampoline = null;
            _packDifficulty?.Detach();
            _packDifficulty = null;
            _packDifficultyTramp = null;
            _buildCard = null;
            _buildCardInfo = IntPtr.Zero;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PackDifficultyDetour(IntPtr self, byte difficulty, IntPtr methodInfo)
        {
            Alive(Hook.PackVisual);
            PackDifficultyCalls++;
            bool rebuild = false;
            if (!Faulted)
            {
                try
                {
                    CurrentSong.ApplyingDifficulty(difficulty);
                    rebuild = CardsForAnotherDifficulty(self, difficulty);
                }
                catch (Exception e)
                {
                    Fault(Hook.PackVisual, e);
                }
            }

            _packDifficultyTramp(self, difficulty, methodInfo);

            if (rebuild)
            {
                RebuildCards(self);
            }

            ApplyStripWindow(self);
        }

        private static bool CardsForAnotherDifficulty(IntPtr packVisual, byte difficulty)
        {
            if (!ResolveFields()) return false;

            if (!Memory.TryList(Memory.Ptr(packVisual + CardsField), out IntPtr items, out int count))
                return false;

            for (int i = 0; i < count; i++)
            {
                if (!Ours(items, i, out IntPtr _, out IntPtr viewModel)) continue;
                if (Memory.U8(viewModel + ViewModelDifficulty) != difficulty) return true;
            }

            return false;
        }

        private static void RebuildCards(IntPtr packVisual)
        {
            if (_buildCard == null) return;
            if (!ResolveFields()) return;
            IntPtr dataAccess = Memory.Ptr(packVisual + PackDataAccess);
            if (!Memory.LooksLikeObject(dataAccess)) return;

            if (!Memory.TryList(Memory.Ptr(packVisual + CardsField), out IntPtr items, out int count))
                return;

            for (int i = 0; i < count; i++)
            {
                if (!Ours(items, i, out IntPtr card, out IntPtr viewModel)) continue;

                try
                {
                    _buildCard(card, dataAccess, viewModel, _buildCardInfo);
                    PackCardsRebuilt++;
                }
                catch (Exception e)
                {
                    Reported("rebuilding a card on the pack screen raised: " + ModLog.Describe(e));
                }
            }
        }

        private static bool Ours(IntPtr items, int index, out IntPtr card, out IntPtr viewModel)
        {
            card = IntPtr.Zero;
            viewModel = IntPtr.Zero;
            if (!ResolveFields()) return false;
            card = Memory.Ptr(items + index * IntPtr.Size);
            if (!Memory.LooksLikeObject(card)) return false;
            viewModel = Memory.Ptr(card + CardViewModel);
            if (!Memory.LooksLikeObject(viewModel)) return false;
            return JacketCatalog.IsOurs(viewModel + ViewModelSong);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void Reported(string why)
        {
            if (_rebuildReported) return;
            _rebuildReported = true;
            ModLog.Warn(why);
        }

        private static bool _rebuildReported;
    }
}
