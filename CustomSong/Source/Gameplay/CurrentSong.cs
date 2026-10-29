using System;

namespace CustomSong
{
    internal static class CurrentSong
    {
        private static int PayloadField = Offsets.Unresolved;
        private static int PayloadSong = Offsets.Unresolved;
        private static int PayloadDifficulty = Offsets.Unresolved;
        private static int SongInfoCharts => Offsets.Song.Charts;
        private static readonly byte[] DifficultyFlags = { 1, 2, 4, 8 };
        private static IntPtr _payload;
        private static bool _fieldsAsked;
        private static bool _fieldsMissing;

        internal static bool TryRead(out IntPtr songInfo, out int difficulty)
        {
            songInfo = IntPtr.Zero;
            difficulty = -1;
            if (!Offsets.Ready) return false;
            IntPtr payload = Payload();
            if (payload == IntPtr.Zero) return false;
            int index = IndexOf(Memory.U8(payload + PayloadDifficulty));
            if (index < 0)
            {
                Refused(payload, $"the difficulty byte is not one of the four flags");
                return false;
            }

            IntPtr song = payload + PayloadSong;

            if (!Memory.LooksLikeObject(Memory.Ptr(song + SongInfoCharts)))
            {
                Refused(payload, "the selected song has no chart array");
                return false;
            }

            songInfo = song;
            difficulty = index;
            return true;
        }

        internal static bool TryReadSong(out IntPtr songInfo)
        {
            songInfo = IntPtr.Zero;
            if (!Offsets.Ready) return false;
            IntPtr payload = Payload();
            if (payload == IntPtr.Zero) return false;
            IntPtr song = payload + PayloadSong;
            if (Memory.U16(song + Offsets.Song.Id) == 0) return false;
            if (!Memory.LooksLikeObject(Memory.Ptr(song + SongInfoCharts))) return false;
            songInfo = song;
            return true;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void Refused(IntPtr payload, string why)
        {
#if DEBUG
            if (_refused) return;
            _refused = true;
            var words = new System.Text.StringBuilder();
            for (int i = 0; i < 12; i++)
            {
                if (i > 0) words.Append(' ');
                words.Append(Memory.I64(payload + PayloadSong + i * 8).ToString("X16"));
            }

#endif
        }

#if DEBUG
        private static bool _refused;
#endif

        private static int SelectedSong = Offsets.Unresolved;
        private static int SceneDifficulty = Offsets.Unresolved;
        private static int SaveContainer = Offsets.Unresolved;
        private static int SaveObject = Offsets.Unresolved;
        private static int SaveState = Offsets.Unresolved;
        private static int SaveDifficulty = Offsets.Unresolved;
        private static IntPtr _saveClass;
        private static bool _saveMissing;
        private static IntPtr _songSelectClass;
        private static int _applied = -1;
        private static ushort _appliedSong;

        internal static bool Applying(IntPtr scene, ushort songId, byte difficultyFlag)
        {
            if (IndexOf(difficultyFlag) < 0) return false;
            bool changed = ApplyingDifficulty(difficultyFlag);
            _appliedSong = songId;
            if (Memory.LooksLikeObject(scene)) Hooks.SongSelect = scene;
            return changed;
        }

        internal static bool ApplyingDifficulty(byte difficultyFlag)
        {
            int index = IndexOf(difficultyFlag);
            if (index < 0) return false;
            bool changed = index != _applied;
            _applied = index;
            return changed;
        }

        private static ushort Shadow(ushort songId) => (ushort)(songId ^ 0xFFFF);

        internal static bool ShadowUnlessSongChanged(IntPtr scene, ushort songId, bool difficultyChanged)
        {
            if (!difficultyChanged) return false;
            if (!Memory.LooksLikeObject(scene)) return false;
            if (!Fields()) return false;
            if (Memory.U16(scene + SelectedSong) != songId) return false;
            Memory.WriteU16(scene + SelectedSong, Shadow(songId));
            return true;
        }

        internal static void Unshadow(IntPtr scene, ushort songId)
        {
            if (!Memory.LooksLikeObject(scene)) return;
            if (!Fields()) return;
            if (Memory.U16(scene + SelectedSong) != Shadow(songId)) return;
            Memory.WriteU16(scene + SelectedSong, songId);
        }

        internal static int AppliedSong => _appliedSong;
        internal static int AppliedDifficulty => _applied;

        internal static int Difficulty()
        {
            if (_applied >= 0) return _applied;
            int saved = FromSave();
            if (saved >= 0) return saved;
            if (TryRead(out IntPtr _, out int difficulty)) return difficulty;
            return FromScene();
        }

        private static int FromSave()
        {
            if (_saveMissing) return -1;
            if (_saveClass == IntPtr.Zero) _saveClass = FieldResolver.ClassPointer("_NH");
            if (_saveClass == IntPtr.Zero) return -1;
            IntPtr statics = FieldResolver.Statics(_saveClass);
            if (statics == IntPtr.Zero) return -1;
            IntPtr save = Memory.Ptr(statics);
            if (!Memory.LooksLikeObject(save)) return -1;

            if (SaveContainer == Offsets.Unresolved)
            {
                SaveState = FieldResolver.Field("SingleFileSaveDataV2", "GeneralSaveState");
                SaveDifficulty = FieldResolver.Field("GeneralSaveStateV5", "LastSelectedDifficulty");
                SaveContainer = FieldResolver.Field("_NH", "_DEb");

                if (SaveState < 0 || SaveDifficulty < 0 || SaveContainer < 0)
                {
                    _saveMissing = true;
                    ModLog.Error("the save's difficulty could not be read by name in this build; " +
                                      "which difficulty is selected will not be known before the " +
                                      "song select has been visited");
                    return -1;
                }
            }

            IntPtr container = Memory.Ptr(save + SaveContainer);
            if (!Memory.LooksLikeObject(container)) return -1;

            if (SaveObject == Offsets.Unresolved)
            {
                SaveObject = FieldOn(container, "_iEb");
                if (SaveObject < 0) { _saveMissing = true; return -1; }
            }

            IntPtr data = Memory.Ptr(container + SaveObject);
            if (!Memory.LooksLikeObject(data)) return -1;
            return IndexOf(Memory.U8(data + SaveState + SaveDifficulty));
        }

        private static int FieldOn(IntPtr instance, string name)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(instance);
            if (klass == IntPtr.Zero) { ModLog.Error($"{name}: no class to ask in this build"); return -1; }

            IntPtr field = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, name);
            if (field == IntPtr.Zero) { ModLog.Error($"{name}: no field by that name in this build"); return -1; }

            int offset = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field);
            if (offset <= 0) { ModLog.Error($"{name}: no field by that name in this build"); return -1; }

            return offset;
        }

        private static int FromScene()
        {
            IntPtr scene = Hooks.SongSelect;
            if (scene == IntPtr.Zero) return -1;

            if (_songSelectClass == IntPtr.Zero)
                _songSelectClass = FieldResolver.ClassPointer("SongSelectScene");
            if (_songSelectClass == IntPtr.Zero) return -1;
            if (Memory.Ptr(scene) != _songSelectClass) return -1;
            if (!Fields()) return -1;
            return IndexOf(Memory.U8(scene + SceneDifficulty));
        }

        private static bool Fields()
        {
            if (_fieldsMissing) return false;
            if (_fieldsAsked) return true;
            IntPtr klass = FieldResolver.ClassPointer("SongSelectScene");
            if (klass == IntPtr.Zero) return false;
            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return false;
            PayloadSong = FieldResolver.Field("_nG", "_LYA");
            PayloadDifficulty = FieldResolver.Field("_nG", "_mYA");
            SelectedSong = FieldResolver.Field("SongSelectScene", "_sr");
            SceneDifficulty = FieldResolver.Field("SongSelectScene", "_Sr");
            PayloadField = FieldResolver.Field("SongSelectScene", "_xr");

            if (PayloadSong < 0 || PayloadDifficulty < 0 || SelectedSong < 0 ||
                SceneDifficulty < 0 || PayloadField < 0)
            {
                _fieldsMissing = true;
                ModLog.Error("SongSelectScene's payload could not be read by name in this build; " +
                                  "which difficulty is selected will not be known, so a song with a " +
                                  "picture per difficulty will not show one");
                return false;
            }

            _fieldsAsked = true;
            _payload = statics + PayloadField;
            return true;
        }

        private static IntPtr Payload() => Fields() ? _payload : IntPtr.Zero;

        private static int IndexOf(byte flag)
        {
            for (int i = 0; i < DifficultyFlags.Length; i++)
                if (DifficultyFlags[i] == flag) return i;
            return -1;
        }
    }
}
