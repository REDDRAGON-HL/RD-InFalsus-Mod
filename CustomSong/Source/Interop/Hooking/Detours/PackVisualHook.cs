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
        private const int CardNewTag = 0x70;
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
        private static int SongScrollListField = Offsets.Unresolved;
        private static int ScrollContentField = Offsets.Unresolved;
        private static int MaskTargetField = Offsets.Unresolved;
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
            SongScrollListField = FieldResolver.Lookup("PackVisualMemberLarge", "songScrollList");
            ScrollContentField = FieldResolver.Lookup("UIScrollListView", "contentContainer");
            MaskTargetField = FieldResolver.Lookup("PackVisualMemberLarge", "maskTarget");
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

        internal static void TickNewTags()
        {
            if (!_newTagsDirty && !_maskWaiting && !_registerStrips) return;
            if (_stripCard == IntPtr.Zero) return;
            if (!Memory.LooksLikeObject(_stripCard) || Memory.Ptr(_stripCard + 0x10) == IntPtr.Zero ||
                Faulted || !ResolveFields())
            {
                _stripCard = IntPtr.Zero;
                _cards.Clear();
                _maskWaiting = false;
                _registerStrips = false;
                return;
            }

            if (_registerStrips)
            {
                _registerStrips = false;
                RegisterStrips(_stripCard);
            }

            if (_newTagsDirty) VisitStrips(_stripCard);

            if (_maskWaiting)
            {
                ReportStripMask(_stripCard);
                if (++_maskTries > 240) _maskWaiting = false;
            }
        }

        private struct CardCall
        {
            public IntPtr Self, A, B, C;
            public int Index, Kind;
        }

        private static readonly List<CardCall> _cards = new List<CardCall>();
        private static bool _newTagsDirty;
        private static bool _saidResume;
        internal static void MarkNewTagsDirty() => _newTagsDirty = true;

        internal static void ResumeStripWindow()
        {
            _newTagsDirty = true;

            if (_stripCard != IntPtr.Zero && Memory.LooksLikeObject(_stripCard) &&
                Memory.Ptr(_stripCard + 0x10) != IntPtr.Zero)
                return;

            try
            {
                GameObject found = GameObject.Find("PackVisualMemberLarge");
                if (found == null) return;
                IntPtr type = IntPtr.Zero;
                IntPtr card = ComponentOf(found, ref type, "ifapp.Game", "PackVisualMemberLarge");
                if (card != IntPtr.Zero) _stripCard = card;
            }
            catch (Exception e)
            {
                ModLog.Warn("the pack card could not be picked up again: " + ModLog.Describe(e));
            }

            if (!_saidResume)
            {
                _saidResume = true;
            }
        }

        private static void Remember(int kind, IntPtr self, IntPtr a, IntPtr b, IntPtr c, int index)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].Self != self) continue;
                _cards[i] = new CardCall { Self = self, A = a, B = b, C = c, Index = index, Kind = kind };
                return;
            }

            _cards.Add(new CardCall { Self = self, A = a, B = b, C = c, Index = index, Kind = kind });
        }

        private static void ReplayCards()
        {
            long mark = Timing.Now;
            for (int i = 0; i < _cards.Count; i++)
            {
                CardCall call = _cards[i];
                if (!Memory.LooksLikeObject(call.Self) || Memory.Ptr(call.Self + 0x10) == IntPtr.Zero) continue;

                try
                {
                    if (call.Kind == 1) _smallCardTramp(call.Self, call.A, call.B, call.Index);
                    else if (call.Kind == 2) _outlineTramp(call.Self, call.A, call.B, call.C, call.Index);
                }
                catch (Exception e)
                {
                    ModLog.Warn("a pack card could not be rebuilt after its table changed: " +
                                ModLog.Describe(e));
                }
            }
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

        private static void ApplyNewTags(IntPtr items, int count)
        {
            if (!Offsets.Ready || !ScoreStore.Ready) return;
            int custom = 0, changed = 0;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    IntPtr card = Memory.Ptr(items + i * IntPtr.Size);
                    if (!Memory.LooksLikeObject(card)) continue;
                    if (Memory.Ptr(card + 0x10) == IntPtr.Zero) continue;
                    IntPtr entry = Memory.Ptr(card + CardViewModel);
                    if (!Memory.LooksLikeObject(entry)) continue;
                    IntPtr songInfo = entry + ViewModelSong;
                    ushort id = Memory.U16(songInfo + Offsets.Song.Id);
                    if (!SongCatalog.CustomSongIds.Contains(id)) continue;
                    custom++;
                    bool show = !ScoreStore.HasAny(songInfo);
                    IntPtr tag = Memory.Ptr(card + CardNewTag);
                    if (!Memory.LooksLikeObject(tag)) continue;
                    if (Memory.Ptr(tag + 0x10) == IntPtr.Zero) continue;
                    GameObject go = new Component(tag).gameObject;
                    if (go == null || go.activeSelf == show) continue;
                    go.SetActive(show);
                    changed++;
                }
                catch (Exception e)
                {
                    ModLog.Warn("the pack strip's NEW tag could not be corrected: " +
                                       ModLog.Describe(e));
                }
            }
        }

        private static void VisitStrips(IntPtr card)
        {
            if (Faulted || !ResolveFields()) return;
            if (StripsField < 0 || ModelField < 0) return;
            IntPtr model = Memory.Ptr(card + ModelField);
            if (!Memory.LooksLikeObject(model) || !SongCatalog.IsCustomPack(PackId(model)))
            {
                _newTagsDirty = false;
                return;
            }

            if (!Memory.TryList(Memory.Ptr(card + StripsField), out IntPtr items, out int count)) return;
            if (!_newTagsDirty) return;
            _newTagsDirty = false;
            ApplyNewTags(items, count);
        }

        private static bool ReportStripMask(IntPtr card)
        {
            try
            {
                IntPtr model = Memory.Ptr(card + ModelField);
                bool ours = Memory.LooksLikeObject(model) && SongCatalog.IsCustomPack(PackId(model));
                IntPtr mask = _stripMask;
                if (mask == IntPtr.Zero || !Memory.LooksLikeObject(mask) ||
                    Memory.Ptr(mask + 0x10) == IntPtr.Zero)
                {
                    GameObject found = GameObject.Find("SongListMask");
                    if (found == null)
                    {
                        if (_maskTries == 0) ModLog.Warn("strip mask: SongListMask not found");
                        return false;
                    }

                    IntPtr type = IntPtr.Zero;
                    mask = ComponentOf(found, ref type, "ifapp.Game", "C2DUpdateMask");
                    if (mask == IntPtr.Zero || Memory.Ptr(mask + 0x10) == IntPtr.Zero)
                    {
                        if (_maskTries == 0)
                            {
                            ModLog.Warn("strip mask: no C2DUpdateMask on " + found.name);
                            }
                        return false;
                    }

                    _stripMask = mask;
                    _maskWhere = $"go={found.name} active={found.activeInHierarchy} " +
                                 $"parent={(found.transform.parent != null ? found.transform.parent.name : "?")}";
                }

                int index = Memory.I32(mask + 0x40);
                if (index < 0)
                {
                    if (_maskTries == 0)
                        {
                        ModLog.Warn($"strip mask: waiting, {_maskWhere} " +
                                           $"enabled={Memory.U8(mask + 0x14)}");
                        }
                    return false;
                }

                if (!ours)
                {
                    if (_maskTries < 3) return false;
                    int official = -1;
                    float officialBlock = -1;
                    if (Memory.TryList(Memory.Ptr(card + StripsField), out IntPtr rows, out official) &&
                        official > 0)
                    {
                        IntPtr row = Memory.Ptr(rows);
                        if (Memory.LooksLikeObject(row))
                        {
                            IntPtr c2d = Memory.Ptr(row + 0x20);
                            if (Memory.LooksLikeObject(c2d))
                                ProbeRenderers(new Component(c2d).gameObject.transform, 0,
                                               ref officialBlock);
                        }
                    }

                    if (!_maskSaid)
                    {
                        _maskSaid = true;
                        ModLog.Warn($"strip mask official: {_maskWhere} index={index} " +
                                           $"block={officialBlock} strips={official}");
                    }
                    return true;
                }

                IntPtr songList = IntPtr.Zero;
                if (SongScrollListField >= 0 && ScrollContentField >= 0)
                {
                    IntPtr list = Memory.Ptr(card + SongScrollListField);
                    if (Memory.LooksLikeObject(list)) songList = Memory.Ptr(list + ScrollContentField);
                }

                int maskParameter = Shader.PropertyToID("_Lowiro_MaskingParameterIndex");

                if (_stripRenderers == null)
                {
                    _stripRenderers = new List<IntPtr>();
                    int count = -1;
                    if (Memory.TryList(Memory.Ptr(card + StripsField), out IntPtr items, out count))
                    {
                        for (int i = 0; i < count; i++)
                        {
                            IntPtr row = Memory.Ptr(items + i * IntPtr.Size);
                            if (!Memory.LooksLikeObject(row)) continue;
                            IntPtr c2d = Memory.Ptr(row + 0x20);
                            if (!Memory.LooksLikeObject(c2d)) continue;
                            if (_firstStrip == IntPtr.Zero) _firstStrip = c2d;
                            Memory.WriteI32(c2d + 0x50, index);
                            _stripsWritten++;
                            CollectRenderers(new Component(c2d).gameObject.transform, 0, index);

                            if (_stripParent == "")
                            {
                                Transform parent = new Component(c2d).gameObject.transform.parent;
                                _stripParent = parent != null ? parent.name : "?";
                            }
                        }
                    }
                }

                float blockValue = -1, afterValue = -1;
                int pushed = 0;
                foreach (IntPtr renderer in _stripRenderers)
                {
                    try
                    {
                        if (!Memory.LooksLikeObject(renderer) || Memory.Ptr(renderer + 0x10) == IntPtr.Zero)
                            continue;
                        var block = new MaterialPropertyBlock();
                        var meshRenderer = new MeshRenderer(renderer);
                        meshRenderer.GetPropertyBlock(block);
                        if (blockValue < 0) blockValue = block.GetFloat(maskParameter);
                        block.SetFloat(maskParameter, index);
                        meshRenderer.SetPropertyBlock(block);

                        if (pushed++ == 0)
                        {
                            var check = new MaterialPropertyBlock();
                            meshRenderer.GetPropertyBlock(check);
                            afterValue = check.GetFloat(maskParameter);

                            try
                            {
                                Material material = meshRenderer.sharedMaterial;
                                if (material != null) material.SetFloat(maskParameter, index);
                            }
                            catch (Exception e)
                            {
                                if (!_materialErrorSaid)
                                {
                                    _materialErrorSaid = true;
                                    ModLog.Warn("the strip mask material could not be set: " +
                                                       ModLog.Describe(e));
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        if (blockValue < 0) blockValue = -3;
                        if (!_pushErrorSaid)
                        {
                            _pushErrorSaid = true;
                            ModLog.Warn("the strip mask property could not be set: " +
                                               ModLog.Describe(e));
                        }
                    }
                }

                int slotMain = -1, slotOther = -1, cacMain = -1, cacOther = -1;
                IntPtr wiClass = FieldResolver.ClassPointer("_wi");
                IntPtr wiStatics = wiClass == IntPtr.Zero ? IntPtr.Zero : FieldResolver.Statics(wiClass);
                if (wiStatics != IntPtr.Zero && _firstStrip != IntPtr.Zero)
                {
                    slotMain = SlotOf(Memory.Ptr(wiStatics + 0x90), _firstStrip);
                    slotOther = SlotOf(Memory.Ptr(wiStatics + 0x98), _firstStrip);
                    if (slotMain >= 0) cacMain = MaskIn(Memory.Ptr(wiStatics + 0xA0), slotMain);
                    if (slotOther >= 0) cacOther = MaskIn(Memory.Ptr(wiStatics + 0xA8), slotOther);
                }

                if (!_maskSaid)
                {
                    _maskSaid = true;
                    ModLog.Warn($"strip mask: {_maskWhere} index={index} " +
                                       $"songList={(Memory.LooksLikeObject(songList) ? Memory.I32(songList + 0x50) : -2)} " +
                                       $"block={blockValue} after={afterValue} strips={_stripsWritten} " +
                                       $"c2ds={_maskWrites} resets={_maskResets} parent={_stripParent} " +
                                       $"renderers={_stripRenderers.Count} pushed={pushed} " +
                                       $"slot={slotMain}/{slotOther} cac={cacMain}/{cacOther} " +
                                       $"sameList={SameList(songList)} targetOk={TargetOk(card, mask)} " +
                                       $"owned={Memory.U8(card + 0x198)}");
                }
                return true;
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("the strip mask could not be read: " + ModLog.Describe(e));
                return true;
            }
        }

        private static bool _maskWaiting;
        private static int _maskTries;
        private static IntPtr _stripMask;
        private static List<IntPtr> _stripRenderers;
        private static int _stripsWritten;
        private static int _maskWrites;
        private static int _maskResets;
        private static IntPtr _c2dReset;
        private static string _stripParent = "";
        private static IntPtr _firstStrip;
        private static IntPtr _meshRendererType;
        private static IntPtr _constrained2dType;
        private static bool _pushErrorSaid;
        private static bool _materialErrorSaid;

        private static void ProbeRenderers(Transform at, int depth, ref float first)
        {
            if (at == null || depth > 4) return;

            IntPtr renderer = ComponentOf(at.gameObject, ref _meshRendererType,
                                          "UnityEngine", "MeshRenderer");
            if (renderer != IntPtr.Zero && first < 0 && Memory.Ptr(renderer + 0x10) != IntPtr.Zero)
            {
                try
                {
                    if (_maskParameterId < 0)
                        _maskParameterId = Shader.PropertyToID("_Lowiro_MaskingParameterIndex");
                    var block = new MaterialPropertyBlock();
                    new MeshRenderer(renderer).GetPropertyBlock(block);
                    first = block.GetFloat(_maskParameterId);
                }
                catch (Exception)
                {
                }
            }

            int children = at.childCount;
            for (int i = 0; i < children; i++) ProbeRenderers(at.GetChild(i), depth + 1, ref first);
        }

        private static int _maskParameterId = -1;
        private static bool _registerStrips;
        private const int C2DId = 0x178;
        private static IntPtr _wiEra, _wiBra, _setEnabled, _getEnabled;

        private static void RegisterStrips(IntPtr card)
        {
            try
            {
                if (StripsField < 0 || ModelField < 0) return;
                IntPtr model = Memory.Ptr(card + ModelField);
                if (!Memory.LooksLikeObject(model) || !SongCatalog.IsCustomPack(PackId(model))) return;
                if (!Memory.TryList(Memory.Ptr(card + StripsField), out IntPtr items, out int count)) return;
                var c2ds = new List<IntPtr>();
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = Memory.Ptr(items + i * IntPtr.Size);
                    if (!Memory.LooksLikeObject(row)) continue;
                    IntPtr c2d = Memory.Ptr(row + 0x20);
                    if (!Memory.LooksLikeObject(c2d)) continue;
                    CollectC2ds(new Component(c2d).gameObject.transform, c2ds, 0);
                }
                if (c2ds.Count == 0) return;
                EnsureRegisterApi();
                if (_wiEra == IntPtr.Zero || _wiBra == IntPtr.Zero || _c2dReset == IntPtr.Zero)
                {
                    ModLog.Warn("strip register: the batch entry points are missing");
                    return;
                }

                var reopened = new List<IntPtr>();
                var woken = 0;
                foreach (IntPtr c2d in c2ds)
                {
                    GameObject go = new Component(c2d).gameObject;
                    if (go != null && !go.activeSelf)
                    {
                        go.SetActive(true);
                        reopened.Add(go.Pointer);
                    }

                    if (_getEnabled != IntPtr.Zero && _setEnabled != IntPtr.Zero &&
                        ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte>)_getEnabled)(c2d, IntPtr.Zero) == 0)
                    {
                        ((delegate* unmanaged[Cdecl]<IntPtr, byte, IntPtr, void>)_setEnabled)(c2d, 1, IntPtr.Zero);
                        woken++;
                    }
                }

                int placed = 0;
                ModLog.Warn($"strip register: start {c2ds.Count} c2ds, woken={woken}, " +
                                   $"reopened={reopened.Count}");
                foreach (IntPtr c2d in c2ds)
                {
                    if (Memory.U16(c2d + C2DId) != 0xFFFF) continue;
                    if (!ParentHasC2D(c2d)) continue;

                    try
                    {
                        ushort slot = ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, ushort>)_wiBra)(c2d, IntPtr.Zero);
                        if (slot == 0) continue;
                        Memory.WriteU16(c2d + C2DId, slot);
                        placed++;
                    }
                    catch (Exception)
                    {
                    }
                }

                foreach (IntPtr c2d in c2ds)
                    ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)_c2dReset)(c2d, IntPtr.Zero);
                Recompute();
                int live = LiveCount(c2ds);

                if (live < c2ds.Count)
                {
                    foreach (IntPtr c2d in c2ds)
                    {
                        if (Memory.U16(c2d + C2DId) != 0xFFFF) continue;
                        if (!ParentHasC2D(c2d)) continue;

                        try
                        {
                            ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, ushort>)_wiBra)(c2d, IntPtr.Zero);
                        }
                        catch (Exception)
                        {
                        }
                    }

                    Recompute();
                    live = LiveCount(c2ds);
                }

                for (int i = reopened.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        new GameObject(reopened[i]).SetActive(false);
                    }
                    catch (Exception)
                    {
                    }
                }

                Recompute();

                ModLog.Warn($"strip register: {c2ds.Count} c2ds, placed={placed}, live={live}, " +
                                   $"woken={woken}, reopened={reopened.Count}");
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("strip register failed: " + ModLog.Describe(e));
            }
        }

        private static void EnsureRegisterApi()
        {
            if (_wiEra == IntPtr.Zero)
                _wiEra = Fallback(MethodResolver.ByName("_wi", "_ERA"), 0x50F100);
            if (_wiBra == IntPtr.Zero)
                _wiBra = Fallback(MethodResolver.ByName("_wi", "_bRA"), 0x511690);
            if (_c2dReset == IntPtr.Zero)
                _c2dReset = Fallback(MethodResolver.ByName("Constrained2D", "Reset"), 0x502110);
            if (_setEnabled != IntPtr.Zero && _getEnabled != IntPtr.Zero) return;

            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(
                "UnityEngine.CoreModule.dll", "UnityEngine", "Behaviour");
            if (klass == IntPtr.Zero) return;

            if (_getEnabled == IntPtr.Zero)
            {
                IntPtr entry = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                    klass, "get_enabled", 0);
                if (entry != IntPtr.Zero) _getEnabled = *(IntPtr*)entry;
            }
            if (_setEnabled == IntPtr.Zero)
            {
                IntPtr entry = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                    klass, "set_enabled", 1);
                if (entry != IntPtr.Zero) _setEnabled = *(IntPtr*)entry;
            }
        }

        private static IntPtr Fallback(IntPtr method, int rva) =>
            method != IntPtr.Zero ? method
                : GameAssembly.Base == IntPtr.Zero ? IntPtr.Zero : GameAssembly.Base + rva;

        private static void Recompute()
        {
            ((delegate* unmanaged[Cdecl]<byte, IntPtr, void>)_wiEra)(1, IntPtr.Zero);
        }

        private static int LiveCount(List<IntPtr> c2ds)
        {
            IntPtr klass = FieldResolver.ClassPointer("_wi");
            IntPtr statics = klass == IntPtr.Zero ? IntPtr.Zero : FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return -1;
            var live = new HashSet<IntPtr>();
            CollectLive(live, Memory.Ptr(statics + 0x90));
            CollectLive(live, Memory.Ptr(statics + 0x98));
            int found = 0;
            foreach (IntPtr c2d in c2ds) if (live.Contains(c2d)) found++;
            return found;
        }

        private static void CollectLive(HashSet<IntPtr> live, IntPtr array)
        {
            if (!Memory.LooksLikeObject(array)) return;
            int length = Memory.I32(array + Offsets.Runtime.ArrayLength);
            if (length <= 0 || length > 0x10000) return;

            for (int i = 0; i < length; i++)
            {
                IntPtr entry = Memory.Ptr(array + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                if (entry != IntPtr.Zero) live.Add(entry);
            }
        }

        private static int SlotOf(IntPtr array, IntPtr c2d)
        {
            if (!Memory.LooksLikeObject(array)) return -1;
            int length = Memory.I32(array + Offsets.Runtime.ArrayLength);
            if (length <= 0 || length > 0x10000) return -1;

            for (int i = 0; i < length; i++)
            {
                IntPtr entry = Memory.Ptr(array + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                if (entry == c2d) return i;
            }

            return -1;
        }

        private static int MaskIn(IntPtr array, int index)
        {
            if (!Memory.LooksLikeObject(array)) return -1;
            int length = Memory.I32(array + Offsets.Runtime.ArrayLength);
            if (index < 0 || index >= length) return -1;
            return Memory.I32(array + Offsets.Runtime.ArrayDataOffset + index * 0x3C + 0x34);
        }

        private static bool SameList(IntPtr songList)
        {
            if (_firstStrip == IntPtr.Zero || songList == IntPtr.Zero) return false;

            try
            {
                Transform parent = new Component(_firstStrip).gameObject.transform.parent;
                if (parent == null) return false;

                IntPtr c2d = ComponentOf(parent.gameObject, ref _constrained2dType,
                                         "ifapp.Game.UI.Common", "Constrained2D");
                return c2d == songList;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TargetOk(IntPtr card, IntPtr mask)
        {
            if (MaskTargetField < 0 || mask == IntPtr.Zero) return false;
            IntPtr target = Memory.Ptr(card + MaskTargetField);
            return target != IntPtr.Zero && Memory.Ptr(mask + 0x28) == target;
        }

        private static void CollectC2ds(Transform at, List<IntPtr> c2ds, int depth)
        {
            if (at == null || depth > 4 || c2ds.Count > 512) return;

            IntPtr c2d = ComponentOf(at.gameObject, ref _constrained2dType,
                                     "ifapp.Game.UI.Common", "Constrained2D");
            if (c2d != IntPtr.Zero) c2ds.Add(c2d);
            int children = at.childCount;
            for (int i = 0; i < children; i++) CollectC2ds(at.GetChild(i), c2ds, depth + 1);
        }

        private static bool ParentHasC2D(IntPtr c2d)
        {
            try
            {
                GameObject go = new Component(c2d).gameObject;
                Transform parent = go != null ? go.transform.parent : null;
                if (parent == null) return false;

                return ComponentOf(parent.gameObject, ref _constrained2dType,
                                   "ifapp.Game.UI.Common", "Constrained2D") != IntPtr.Zero;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void CollectRenderers(Transform at, int depth, int index)
        {
            if (at == null || depth > 4 || _stripRenderers.Count >= 512) return;
            GameObject go = at.gameObject;
            IntPtr c2d = ComponentOf(go, ref _constrained2dType, "ifapp.Game.UI.Common", "Constrained2D");
            if (c2d != IntPtr.Zero)
            {
                Memory.WriteI32(c2d + 0x50, index);
                _maskWrites++;

                if (_c2dReset == IntPtr.Zero)
                    _c2dReset = MethodResolver.ByName("Constrained2D", "Reset");
                if (_c2dReset != IntPtr.Zero)
                {
                    ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)_c2dReset)(c2d, IntPtr.Zero);
                    _maskResets++;
                }
            }

            IntPtr renderer = ComponentOf(go, ref _meshRendererType, "UnityEngine", "MeshRenderer");
            if (renderer != IntPtr.Zero) _stripRenderers.Add(renderer);
            int children = at.childCount;
            for (int i = 0; i < children; i++) CollectRenderers(at.GetChild(i), depth + 1, index);
        }

        private static string _maskWhere = "";
        private static bool _maskSaid;

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
            long cardMark = Timing.Now;
            CustomSongCore.PageArrived();
            long changesAtBuild = PackText.Changes;
            long step = Timing.Now;
            ApplyCardTable(self, LargeAssetsField, "large");
            _maskWaiting = true;
            _registerStrips = true;
            _maskTries = 0;
            _stripMask = IntPtr.Zero;
            _stripRenderers = null;
            _stripsWritten = 0;
            _maskWrites = 0;
            _maskResets = 0;
            _stripParent = "";
            _firstStrip = IntPtr.Zero;
            _pushErrorSaid = false;
            _materialErrorSaid = false;
            _maskWhere = "";
            _maskSaid = false;
            _newTagsDirty = true;
            step = Timing.Now;
            _largeCardTramp(self, input, model, index);
            step = Timing.Now;
            ApplyCardTable(self, LargeAssetsField, "large");
            if (PackText.Changes != changesAtBuild) ReplayCards();
            ReportStripCount(self, model);
            VisitStrips(self);
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
            Remember(1, self, dataAccess, model, IntPtr.Zero, index);
        }

        private static bool _saidCards;

        private static void ReportCardArt(IntPtr card, IntPtr model, int index)
        {
            if (_saidCards) return;
            _saidCards = true;
        }

        private static bool _saidOutline;

        private static void ReportOutlineArt(IntPtr card, IntPtr model)
        {
            if (_saidOutline) return;
            _saidOutline = true;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OutlineDetour(IntPtr self, IntPtr dataAccess, IntPtr input, IntPtr model,
                                          int index)
        {
            Alive(Hook.PackVisual);
            ApplyCardTable(self, OutlineAssetsField, "outline");
            _outlineTramp(self, dataAccess, input, model, index);
            ApplyCardTable(self, OutlineAssetsField, "outline");
            Remember(2, self, dataAccess, input, model, index);
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
                if (Memory.LooksLikeObject(assets)) FillTable(assets, which);

                if (PackText.TryGetVisuals(out IntPtr fromDataAccess) &&
                    Memory.LooksLikeObject(fromDataAccess) && fromDataAccess != assets)
                    FillTable(fromDataAccess, "DataAccess");
            }
            catch (Exception e)
            {
                Fault(Hook.PackVisual, e);
            }
        }

        private static void FillTable(IntPtr assets, string which)
        {
            bool first = _cardTables.Add(assets);
            if (first)
            {
            }

            CustomSongCore.ApplyVisualsTo(assets);
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

            ModLog.Warn($"strip row pack {packId}{(ours ? " (ours)" : "")}: " +
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

            _newTagsDirty = true;
            VisitStrips(self);
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
