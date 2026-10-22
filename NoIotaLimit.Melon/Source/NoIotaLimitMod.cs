using System;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(InFalsusNoIotaLimit.NoIotaLimitMod), "InFalsusNoIotaLimit", "1.0.1", "RedDragon", null)]
[assembly: MelonGame("lowiro", "infalsus")]

namespace InFalsusNoIotaLimit
{
    public class NoIotaLimitMod : MelonMod
    {
        private static HarmonyLib.Harmony _harmony;

        public override void OnInitializeMelon()
        {
            NoIotaLimitCore.Info = message => LoggerInstance.Msg(message);
            NoIotaLimitCore.Warn = message => LoggerInstance.Warning(message);
            NoIotaLimitCore.Error = message => LoggerInstance.Error(message);
            LoggerInstance.Msg("loading: removing the 9999 particle cap");
        }

        public override void OnLateInitializeMelon()
        {
            NoIotaLimitCore.PatchUiCapCode();
            try
            {
                _harmony = new HarmonyLib.Harmony("RedDragon.InFalsusNoIotaLimit");
                _harmony.PatchAll(typeof(NoIotaLimitMod).Assembly);
                LoggerInstance.Msg("inventory guard installed");
            }
            catch (Exception e)
            {
                LoggerInstance.Error(e.ToString());
            }
        }

        public override void OnUpdate()
        {
            NoIotaLimitCore.SetCap();
        }

        public override void OnDeinitializeMelon()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
