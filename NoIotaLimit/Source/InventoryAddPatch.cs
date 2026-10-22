using System;
using HarmonyLib;
using ifapp.Game.Data;

namespace InFalsusNoIotaLimit
{
    [HarmonyPatch(typeof(IotaInventory), nameof(IotaInventory.Add))]
    internal static class InventoryAddPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(IotaInventory __instance, ref IotaInstance __0)
        {
            var items = __instance.allIota;
            if (items == null) return true;

            long total = 0;
            var buffer = items.RawBuffer;
            for (int i = 0; i < items.Length; i++)
                total += buffer[i].Count;

            long remaining = ushort.MaxValue - total;
            if (remaining <= 0) return false;

            __0.Count = (ushort)Math.Min(__0.Count, remaining);
            return __0.Count != 0;
        }
    }
}
