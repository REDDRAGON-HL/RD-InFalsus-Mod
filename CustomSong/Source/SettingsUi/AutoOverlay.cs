extern alias UnityEngineCore;
extern alias UnityEngineImgui;
using System;
using UnityEngineCore::UnityEngine;
using UnityEngineImgui::UnityEngine;

namespace CustomSong
{
    internal static class AutoOverlay
    {
        private const float Width = 300f;
        private const float Height = 136f;
        private const int TitleSize = 24;
        private const int ButtonSize = 32;
        private static bool _sized, _drawn, _failed;

        internal static void Draw()
        {
            AutoSwitch.NoteOpen("gui");
            if (AutoRow.Present || !AutoSwitch.PageIsOpen || _failed) return;

            try
            {
                Paint();
            }
            catch (Exception e)
            {
                _failed = true;
                ModLog.WarnRelease("AUTO overlay: the switch could not be drawn: " + ModLog.Describe(e));
            }
        }

        private static void Paint()
        {
            Rect area = new Rect(Screen.width - Width - 40f, Screen.height - Height - 40f, Width, Height);
            Part("panel", () => GUI.Box(area, GUIContent.none));
            Part("title", () => GUI.Label(new Rect(area.x + 18f, area.y + 12f, area.width - 36f, 34f),
                                          new GUIContent("AUTO PLAY")));
            Color previous = GUI.color;
            GUI.color = AutoSwitch.On ? new Color(0.55f, 0.95f, 0.85f) : new Color(0.6f, 0.6f, 0.6f);
            bool pressed = false;
            Part("button", () => pressed = GUI.Button(new Rect(area.x + 18f, area.y + 56f,
                                                               area.width - 36f, 62f),
                                                       new GUIContent(AutoSwitch.On ? "ON" : "OFF")));
            GUI.color = previous;

            if (!_drawn)
            {
                _drawn = true;
                ModLog.Load($"AUTO overlay: drawn at {Screen.width}x{Screen.height}");
            }

            if (pressed) AutoSwitch.Toggle();
        }

        private static void Size()
        {
            if (_sized) return;
            _sized = true;
            GUI.skin.label.fontSize = TitleSize;
            GUI.skin.button.fontSize = ButtonSize;
        }

        private static void Part(string what, Action draw)
        {
            if (_failed) return;

            try
            {
                Size();
                draw();
            }
            catch (Exception e)
            {
                _failed = true;
                ModLog.WarnRelease($"AUTO overlay: the {what} could not be drawn: " + ModLog.Describe(e));
            }
        }
    }
}
