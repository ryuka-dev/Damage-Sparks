using System;
using System.Runtime.CompilerServices;
using BepInEx;

namespace SulfurDamageNumbersMod.SulfurTogether
{
    /// <summary>
    /// Connects Damage Sparks to SULFUR Together. Both dependencies are hard: with either plugin absent,
    /// BepInEx skips this one and the mod plays on its own.
    ///
    /// A matching GUID does not guarantee the API, because an older SULFUR Together has the GUID but not
    /// NetCombat. The first contact with it therefore happens inside <see cref="Attach"/>, a guarded
    /// non-inlined call, and a missing API leaves one log line instead of a type-load failure.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, Plugin.PluginVersion)]
    [BepInDependency(Plugin.PluginGuid)]
    [BepInDependency(SulfurTogetherGuid)]
    public sealed class SulfurTogetherAdapterPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ryuka.sulfur.damagenumbers_sulfur_together";
        public const string PluginName = "Damage Sparks - SULFUR Together Adapter";

        private const string SulfurTogetherGuid = "com.ryuka.sulfur.together";

        // IDisposable on purpose: no field in this assembly may name a SULFUR Together type.
        private IDisposable _bridge;

        private void Awake()
        {
            try
            {
                _bridge = Attach();
                Logger.LogInfo(_bridge != null
                    ? "Connected to SULFUR Together's combat events."
                    : "The installed SULFUR Together has an unknown combat API; co-op client damage numbers are unavailable.");
            }
            catch (Exception e) when (
                e is TypeLoadException || e is MissingMethodException || e is MissingFieldException ||
                e is TypeInitializationException || e is BadImageFormatException)
            {
                Logger.LogWarning(
                    "The installed SULFUR Together has no combat API (it needs 1.7.0 or newer); co-op client " +
                    "damage numbers are unavailable. (" + e.GetType().Name + ": " + e.Message + ")");
            }
        }

        private void OnDestroy()
        {
            if (_bridge == null) return;

            _bridge.Dispose();
            _bridge = null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IDisposable Attach()
        {
            return NetCombatBridge.TryCreate();
        }
    }
}
