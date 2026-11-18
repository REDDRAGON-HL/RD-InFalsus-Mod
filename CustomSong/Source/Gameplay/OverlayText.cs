extern alias UnityEngineCore;
extern alias UnityEngineImgui;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngineCore::UnityEngine;
using UnityEngineImgui::UnityEngine;

namespace CustomSong
{
    internal static class OverlayText
    {
        private const float DesignWidth = 1920f;
        private const float DesignHeight = 1080f;
        private const float SideMargin = 60f;
        private const float TrackLeft = 560f;
        private const float TrackRight = 1490f;

        private sealed class Active
        {
            internal SceneDirectives.Text Row;
            internal float Alpha;
            internal float Scale;
            internal float X;
            internal float Y;
        }

        private static readonly List<Active> _active = new List<Active>();
        private static int _errors;
        private static bool _reported;

        internal static void Tick(double nowMs)
        {
            _active.Clear();
            NowMs = nowMs;
            SceneDirectives.Text[] rows = SceneDirectives.CurrentTexts;
            if (rows == null || rows.Length == 0) return;
            float scale = Screen.height / DesignHeight;

            for (int i = 0; i < rows.Length; i++)
            {
                SceneDirectives.Text row = rows[i];
                if (nowMs < row.Start || nowMs >= row.End) continue;
                float alpha = 1f;
                if (row.FadeIn > 0f)
                    alpha = Math.Min(alpha, (float)(nowMs - row.Start) / row.FadeIn);
                if (row.FadeOut > 0f)
                    alpha = Math.Min(alpha, (float)(row.End - nowMs) / row.FadeOut);

                _active.Add(new Active
                {
                    Row = row,
                    Alpha = Math.Clamp(alpha, 0f, 1f),
                    Scale = scale,
                    X = Horizontal(row, scale),
                    Y = Vertical(row, scale, i),
                });
            }
        }

        private static float Horizontal(in SceneDirectives.Text row, float scale)
        {
            switch (row.Side)
            {
                case 1: return TrackRight * scale;
                case 2: return (DesignWidth * 0.5f - 250f) * scale;
                case 3: return Math.Clamp(row.X, 0f, 1f) * Screen.width;
                default: return SideMargin * scale;
            }
        }

        private static float Vertical(in SceneDirectives.Text row, float scale, int index)
        {
            if (row.Side == 3) return Math.Clamp(row.Y, 0f, 1f) * Screen.height;

            if (row.Y >= 0f)
            {
                return row.Y <= 1.5f ? row.Y * Screen.height : row.Y * scale;
            }

            uint hash = 2166136261u;
            foreach (char c in row.Value ?? "") hash = (hash ^ c) * 16777619u;
            float fraction = 0.18f + (hash % 1000u) / 1000f * 0.54f;
            return fraction * Screen.height + index * 8f * scale;
        }

        internal static void Draw()
        {
            if (_active.Count == 0) return;

            try
            {
                GUIStyle style = GUI.skin.label;
                if (style == null) return;
                style.wordWrap = false;

                foreach (Active item in _active)
                {
                    SceneDirectives.Text row = item.Row;
                    int size = Math.Max(8, (int)Math.Round(row.Size * item.Scale));
                    style.fontSize = size;
                    var content = new GUIContent(row.Value ?? "");
                    Vector2 measured = style.CalcSize(content);
                    float x = item.X;
                    float y = item.Y;

                    if (row.Amp > 0f && row.Hz > 0f)
                    {
                        double seconds = NowMs / 1000.0;
                        y += (float)Math.Sin(seconds * row.Hz * Math.PI * 2.0) * row.Amp * item.Scale;
                    }

                    var area = new Rect(x, y, measured.x + 8f, measured.y + 8f);

                    if (row.ShadowPx > 0f)
                    {
                        Color previous = GUI.color;
                        uint shadow = row.Shadow;
                        GUI.color = new Color(((shadow >> 24) & 0xFF) / 255f,
                                              ((shadow >> 16) & 0xFF) / 255f,
                                              ((shadow >> 8) & 0xFF) / 255f,
                                              ((shadow & 0xFF) / 255f) * item.Alpha);
                        GUI.Label(new Rect(area.x + row.ShadowPx * item.Scale, area.y + row.ShadowPx * item.Scale,
                                           area.width, area.height), content);
                        GUI.color = previous;
                    }

                    uint fill = row.Fill;
                    GUI.color = new Color(((fill >> 24) & 0xFF) / 255f,
                                          ((fill >> 16) & 0xFF) / 255f,
                                          ((fill >> 8) & 0xFF) / 255f,
                                          ((fill & 0xFF) / 255f) * item.Alpha);
                    GUI.Label(area, content);
                }

                GUI.color = new Color(1f, 1f, 1f, 1f);

                if (!_reported)
                {
                    _reported = true;
                    ModLog.Load($"showtext: {_active.Count} line(s) drawn");
                }
            }
            catch (Exception e)
            {
                if (_errors++ < 3)
                    ModLog.WarnRelease("showtext: the overlay could not be drawn: " + ModLog.Describe(e));
            }
        }

        internal static double NowMs;

        internal static void Clear()
        {
            _active.Clear();
            _reported = false;
            NowMs = 0.0;
        }
    }
}
