extern alias UnityEngineCore;
using System;
using System.Collections.Generic;
using UnityEngineCore::UnityEngine;

namespace CustomSong
{
    internal static class TrackMove
    {
        private const int TrackMasterScene = 0xD8;
        private const int TrackNotesContainer = 0x1F0;
        private const float YawSign = -1f;
        private const float PitchSign = 1f;
        private const float RollSign = 1f;

        private struct V3
        {
            internal float X, Y, Z;
        }

        private struct Qt
        {
            internal float X, Y, Z, W;
            internal static Qt Identity => new Qt { W = 1f };
        }

        private static bool _captured;
        private static IntPtr _track;
        private static int _retiredGeneration = -1;
        private static V3 _baseMasterPos, _baseNotesPos, _pivot;
        private static Qt _baseMasterRot = Qt.Identity, _baseNotesRot = Qt.Identity;
        private static V3 _offsetPos;
        private static Qt _offsetRot = Qt.Identity;
        private static bool _applied;
        private static int _cursor;
        private static bool _animating;
        private static double _animStart;
        private static double _animDuration;
        private static int _animEase;
        private static V3 _animFromPos, _animToPos;
        private static Qt _animFromRot = Qt.Identity, _animToRot = Qt.Identity;
        private static SceneDirectives.Entry[] _source = Array.Empty<SceneDirectives.Entry>();
        private static int _failures;

        internal static void Tick(IntPtr track, double nowMs)
        {
            if (_retiredGeneration >= 0 && _retiredGeneration == SceneDirectives.Generation) return;
            if (!Memory.LooksLikeObject(track)) return;

            if (!ReferenceEquals(_source, SceneDirectives.Current))
            {
                _source = SceneDirectives.Current;
                Restore();
                Discard();
            }

            if (!HasRows) return;

            if (!_captured || _track != track)
            {
                Capture(track);
                _track = track;
                _offsetPos = new V3();
                _offsetRot = Qt.Identity;
                _applied = false;
                _cursor = 0;
                _animating = false;
            }

            SceneDirectives.Entry[] rows = _source;
            while (_cursor < rows.Length && rows[_cursor].Time <= nowMs)
            {
                SceneDirectives.Entry row = rows[_cursor++];
                if (row.Kind != SceneDirectives.Kind.TrackMove) continue;

                bool reset = Math.Abs(row.X) < 1e-6f && Math.Abs(row.Y) < 1e-6f &&
                             Math.Abs(row.Z) < 1e-6f && Math.Abs(row.Xoz) < 1e-6f &&
                             Math.Abs(row.Yoz) < 1e-6f && Math.Abs(row.Xoy) < 1e-6f;
                V3 targetPos;
                Qt targetRot;

                if (reset)
                {
                    targetPos = new V3();
                    targetRot = Qt.Identity;
                }
                else
                {
                    targetPos = new V3
                    {
                        X = _offsetPos.X + row.X,
                        Y = _offsetPos.Y + row.Y,
                        Z = _offsetPos.Z + row.Z,
                    };
                    targetRot = Multiply(_offsetRot,
                        Euler(row.Yoz * PitchSign, row.Xoz * YawSign, row.Xoy * RollSign));
                }

                _animFromPos = _offsetPos;
                _animToPos = targetPos;
                _animFromRot = _offsetRot;
                _animToRot = targetRot;
                _animStart = row.Time;
                _animDuration = row.Duration;
                _animEase = row.Mode;
                _animating = _animDuration > 0.0;

                if (!_animating)
                {
                    _offsetPos = targetPos;
                    _offsetRot = targetRot;
                }
            }

            if (_animating)
            {
                float t = (float)Math.Clamp((nowMs - _animStart) / _animDuration, 0.0, 1.0);
                float k = _animEase == 1 ? t * t * t
                        : _animEase == 2 ? 1f - (1f - t) * (1f - t) * (1f - t)
                        : _animEase == 3 ? t * t * (3f - 2f * t)
                        : t;
                _offsetPos = Lerp(_animFromPos, _animToPos, k);
                _offsetRot = Nlerp(_animFromRot, _animToRot, k);
                if (t >= 1f) _animating = false;
            }

            Apply(track);
        }

        private static bool HasRows
        {
            get
            {
                SceneDirectives.Entry[] rows = _source;
                for (int i = 0; i < rows.Length; i++)
                    if (rows[i].Kind == SceneDirectives.Kind.TrackMove) return true;
                return false;
            }
        }

        private static void Capture(IntPtr track)
        {
            try
            {
                IntPtr master = Memory.Ptr(track + TrackMasterScene);
                IntPtr notes = Memory.Ptr(track + TrackNotesContainer);
                if (!Memory.LooksLikeObject(master) || !Memory.LooksLikeObject(notes)) return;
                var masterTransform = new Transform(master);
                var notesTransform = new Transform(notes);
                _baseMasterPos = Read(masterTransform.position);
                _baseMasterRot = Read(masterTransform.rotation);
                _baseNotesPos = Read(notesTransform.position);
                _baseNotesRot = Read(notesTransform.rotation);
                _pivot = _baseMasterPos;
                Transform line = masterTransform.Find("JUDGEMENT_LINE");
                if (line != null) _pivot = Read(line.position);
                _captured = true;
                _applied = false;
            }
            catch (Exception e)
            {
                NoteFailure("capture", e);
            }
        }

        private static void Apply(IntPtr track)
        {
            try
            {
                bool identity = Nearly(_offsetPos.X) && Nearly(_offsetPos.Y) && Nearly(_offsetPos.Z) &&
                                Nearly(_offsetRot.X) && Nearly(_offsetRot.Y) && Nearly(_offsetRot.Z) &&
                                Nearly(_offsetRot.W - 1f);
                IntPtr master = Memory.Ptr(track + TrackMasterScene);
                IntPtr notes = Memory.Ptr(track + TrackNotesContainer);
                if (!Memory.LooksLikeObject(master) || !Memory.LooksLikeObject(notes)) return;
                var masterTransform = new Transform(master);
                var notesTransform = new Transform(notes);

                if (identity)
                {
                    if (!_applied) return;
                    Write(masterTransform, _baseMasterPos, _baseMasterRot);
                    Write(notesTransform, _baseNotesPos, _baseNotesRot);
                    _applied = false;
                    return;
                }

                V3 masterPos = Offset(_baseMasterPos);
                V3 notesPos = Offset(_baseNotesPos);
                Write(masterTransform, masterPos, Multiply(_offsetRot, _baseMasterRot));
                Write(notesTransform, notesPos, Multiply(_offsetRot, _baseNotesRot));
                _applied = true;
            }
            catch (Exception e)
            {
                NoteFailure("apply", e);
            }
        }

        private static V3 Offset(V3 basePos)
        {
            V3 relative = Subtract(basePos, _pivot);
            V3 turned = Rotate(relative, _offsetRot);
            return new V3
            {
                X = _pivot.X + turned.X + _offsetPos.X,
                Y = _pivot.Y + turned.Y + _offsetPos.Y,
                Z = _pivot.Z + turned.Z + _offsetPos.Z,
            };
        }

        internal static void Retire()
        {
            Restore();
            Forget();
            _retiredGeneration = SceneDirectives.Generation;
        }

        internal static void Forget()
        {
            Discard();
            _source = Array.Empty<SceneDirectives.Entry>();
            _retiredGeneration = -1;
        }

        private static void Restore()
        {
            if (!_captured || !_applied || _track == IntPtr.Zero) return;

            try
            {
                IntPtr master = Memory.Ptr(_track + TrackMasterScene);
                IntPtr notes = Memory.Ptr(_track + TrackNotesContainer);
                if (Memory.LooksLikeObject(master) && Memory.LooksLikeObject(notes))
                {
                    Write(new Transform(master), _baseMasterPos, _baseMasterRot);
                    Write(new Transform(notes), _baseNotesPos, _baseNotesRot);
                }
            }
            catch
            {
            }

            _applied = false;
        }

        private static void Discard()
        {
            _captured = false;
            _applied = false;
            _animating = false;
            _track = IntPtr.Zero;
            _cursor = 0;
            _offsetPos = new V3();
            _offsetRot = Qt.Identity;
        }

        private static bool Nearly(float value) => Math.Abs(value) < 1e-6f;
        private static V3 Read(Vector3 v) => new V3 { X = v.x, Y = v.y, Z = v.z };
        private static Qt Read(Quaternion q) => new Qt { X = q.x, Y = q.y, Z = q.z, W = q.w };

        private static void Write(Transform transform, V3 position, Qt rotation)
        {
            transform.SetPositionAndRotation(
                new Vector3(position.X, position.Y, position.Z),
                new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W));
        }

        private static V3 Subtract(V3 a, V3 b) => new V3 { X = a.X - b.X, Y = a.Y - b.Y, Z = a.Z - b.Z };

        private static V3 Lerp(V3 a, V3 b, float t) => new V3
        {
            X = a.X + (b.X - a.X) * t,
            Y = a.Y + (b.Y - a.Y) * t,
            Z = a.Z + (b.Z - a.Z) * t,
        };

        private static V3 Rotate(V3 v, Qt q)
        {
            float x = q.X, y = q.Y, z = q.Z, w = q.W;
            float ix = w * v.X + y * v.Z - z * v.Y;
            float iy = w * v.Y + z * v.X - x * v.Z;
            float iz = w * v.Z + x * v.Y - y * v.X;
            float iw = -x * v.X - y * v.Y - z * v.Z;

            return new V3
            {
                X = ix * w + iw * -x + iy * -z - iz * -y,
                Y = iy * w + iw * -y + iz * -x - ix * -z,
                Z = iz * w + iw * -z + ix * -y - iy * -x,
            };
        }

        private static Qt Multiply(Qt a, Qt b) => new Qt
        {
            X = a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            Y = a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            Z = a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            W = a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
        };

        private static Qt Axis(float ax, float ay, float az, float degrees)
        {
            float half = degrees * 0.5f * 0.017453292f;
            float sin = (float)Math.Sin(half);
            return new Qt { X = ax * sin, Y = ay * sin, Z = az * sin, W = (float)Math.Cos(half) };
        }

        private static Qt Euler(float xDeg, float yDeg, float zDeg) =>
            Multiply(Multiply(Axis(0f, 1f, 0f, yDeg), Axis(1f, 0f, 0f, xDeg)), Axis(0f, 0f, 1f, zDeg));

        private static Qt Nlerp(Qt a, Qt b, float t)
        {
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            float sign = dot < 0f ? -1f : 1f;

            var r = new Qt
            {
                X = a.X + (b.X * sign - a.X) * t,
                Y = a.Y + (b.Y * sign - a.Y) * t,
                Z = a.Z + (b.Z * sign - a.Z) * t,
                W = a.W + (b.W * sign - a.W) * t,
            };
            float length = (float)Math.Sqrt(r.X * r.X + r.Y * r.Y + r.Z * r.Z + r.W * r.W);
            if (length > 1e-6f)
            {
                r.X /= length; r.Y /= length; r.Z /= length; r.W /= length;
            }
            return r;
        }

        private static void NoteFailure(string where, Exception e)
        {
            if (_failures++ < 3)
                ModLog.Warn($"scene rows: trackmove {where} failed: {ModLog.Describe(e)}");
        }
    }
}
