using MelonLoader;

[assembly: MelonInfo(typeof(InFalsusNoIotaLimit.NoIotaLimitMod), "InFalsusNoIotaLimit", "1.0.1", "RedDragon", null)]
[assembly: MelonGame("lowiro", "infalsus")]

namespace InFalsusNoIotaLimit
{
    public class NoIotaLimitMod : MelonMod
    {
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
        }

        public override void OnUpdate()
        {
            NoIotaLimitCore.SetCap();
        }
    }
}
