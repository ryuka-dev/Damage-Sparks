using HarmonyLib;

namespace SulfurDamageNumbersMod
{
    internal static class DamageReceivePatch
    {
        public static void Apply(Harmony harmony)
        {
            DamageLogPatch.Apply(harmony);
        }
    }
}
