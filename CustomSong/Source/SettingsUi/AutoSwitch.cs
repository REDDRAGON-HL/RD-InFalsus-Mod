extern alias UnityEngineCore;
using System;
using UnityEngineCore::UnityEngine;

namespace CustomSong
{
    internal static class AutoSwitch
    {
        internal static bool On;
        private static IntPtr _panel;
        private static bool _openLogged;

        internal static void Attach(IntPtr panel)
        {
            if (Memory.LooksLikeObject(panel)) _panel = panel;
        }

        internal static bool PageIsOpen
        {
            get
            {
                if (!Memory.LooksLikeObject(_panel)) return false;

                try
                {
                    GameObject page = new Component(_panel).gameObject;
                    return page != null && page.activeInHierarchy;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static void Toggle()
        {
            On = !On;
        }

        internal static void NoteOpen(string from)
        {
            bool open = PageIsOpen;
            if (open == _openLogged) return;
            _openLogged = open;
        }
    }
}
