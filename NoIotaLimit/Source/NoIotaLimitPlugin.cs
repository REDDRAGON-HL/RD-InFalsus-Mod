using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace InFalsusNoIotaLimit
{
    [BepInPlugin("RedDragon.InFalsusNoIotaLimit", "InFalsusNoIotaLimit", "1.0.1")]
    public class NoIotaLimitPlugin : BasePlugin
    {
        internal static ManualLogSource LogSource;
        internal static ConfigEntry<bool> PatchUiCap;

        public override void Load()
        {
            LogSource = base.Log;
            NoIotaLimitCore.Info = message => LogSource.LogInfo(message);
            NoIotaLimitCore.Warn = message => LogSource.LogWarning(message);
            NoIotaLimitCore.Error = message => LogSource.LogError(message);

            PatchUiCap = Config.Bind("General", "PatchUiCap", true,
                "同时把 UI 里硬编码的 9999 比较改成 65535（粒子数量显示 / 界面元素）。");

            LogSource.LogInfo("loading: removing the 9999 particle cap");
            AddComponent<NoIotaLimitBehaviour>();
            if (PatchUiCap.Value) NoIotaLimitCore.PatchUiCapCode();
        }
    }

    public class NoIotaLimitBehaviour : MonoBehaviour
    {
        private void Update() => NoIotaLimitCore.SetCap();
    }
}
