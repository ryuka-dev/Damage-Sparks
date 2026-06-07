using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace SulfurDamageNumbersMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ryuka.sulfur.damagenumbers";
        public const string PluginName = "SULFUR Damage Numbers";
        public const string PluginVersion = "1.0.7";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<DisplayMode> Mode;
        internal static ConfigEntry<RenderMode> Renderer;
        internal static ConfigEntry<MergedColorMode> AggregateColorMode;
        internal static ConfigEntry<bool> UseWeakspotColors;
        internal static ConfigEntry<bool> TreatOneMultiplierAsNormal;
        internal static ConfigEntry<bool> RequireNpcTarget;
        internal static ConfigEntry<bool> RequireSourceUnitPlayer;
        internal static ConfigEntry<bool> ShowPlayerOwnedSecondaryEffects;
        internal static ConfigEntry<float> MinDamageToShow;
        internal static ConfigEntry<bool> ShowDecimals;
        internal static ConfigEntry<float> Lifetime;
        internal static ConfigEntry<float> FloatSpeed;
        internal static ConfigEntry<float> WorldScale;
        internal static ConfigEntry<float> RandomJitter;
        internal static ConfigEntry<float> FontSize;
        internal static ConfigEntry<int> ScreenFontSize;
        internal static ConfigEntry<float> ScreenFloatSpeed;
        internal static ConfigEntry<float> ScreenJitter;
        internal static ConfigEntry<float> PerHitStackScreenSpread;
        internal static ConfigEntry<float> PerHitStackWorldSpread;
        internal static ConfigEntry<int> MaxActiveNumbers;
        internal static ConfigEntry<bool> DebugLog;
        internal static ConfigEntry<bool> DebugSpawnTestOnF8;

        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true, "Enable damage number display.");
            Mode = Config.Bind("General", "DamageDisplayMode", DisplayMode.PerTargetTotal,
                "PerTargetTotal = same frame, same player attack source, same target damage is merged. PerHit = every real damage log call creates one number.");
            Renderer = Config.Bind("Visual", "Renderer", RenderMode.ScreenSpace,
                "ScreenSpace is the most reliable renderer. WorldTextMeshPro uses 3D TextMeshPro objects. Both renders both at the same time for debugging.");
            UseWeakspotColors = Config.Bind("Visual", "UseWeakspotColors", true,
                "Use weakspot/body-part multiplier colors. The color thresholds match the PositiveShapes debug overlay style: red >= 1.5, orange >= 1.0, yellow >= 0.75, white when no multiplier is available.");
            TreatOneMultiplierAsNormal = Config.Bind("Visual", "TreatOneMultiplierAsNormal", false,
                "If true, multiplier 1.0 is shown as white instead of orange. Enable this if the game reports ordinary body hits as multiplier 1.0 and you want only special weakpoints colored.");
            AggregateColorMode = Config.Bind("Visual", "AggregateColorMode", MergedColorMode.HighestMultiplier,
                "Color rule for PerTargetTotal. HighestMultiplier = use the strongest pellet/hit color. DamageWeightedAverage = average multipliers by damage. LastHit = use the last hit color.");
            RequireNpcTarget = Config.Bind("Filter", "RequireNpcTarget", true,
                "Only show damage numbers on Npc targets. Keep true to avoid barrels/objects if they also use Unit damage code.");
            RequireSourceUnitPlayer = Config.Bind("Filter", "RequireSourceUnitPlayer", false,
                "If true, sourceData.sourceUnit must exist and be the player. Default false because some projectiles/explosions keep only the game's isPlayer flag.");
            ShowPlayerOwnedSecondaryEffects = Config.Bind("Filter", "ShowPlayerOwnedSecondaryEffects", true,
                "Show player-owned secondary damage such as projectile explosions if the game reports them as created by the player. Turn off if poison/fire DoT becomes noisy.");
            MinDamageToShow = Config.Bind("Filter", "MinDamageToShow", 0.01f,
                "Final damage below this value will not be shown.");
            ShowDecimals = Config.Bind("Visual", "ShowDecimals", false,
                "Show one decimal place for non-integer damage. Integer values such as 5.0 are still shown as 5.");
            Lifetime = Config.Bind("Visual", "Lifetime", 0.85f,
                "Damage number lifetime in seconds.");
            FloatSpeed = Config.Bind("Visual", "FloatSpeed", 1.25f,
                "World-space upward speed for WorldTextMeshPro renderer.");
            WorldScale = Config.Bind("Visual", "WorldScale", 0.045f,
                "World-space scale of each TextMeshPro damage number.");
            RandomJitter = Config.Bind("Visual", "RandomJitter", 0.18f,
                "Random horizontal world-space offset. Useful for PerHit shotgun pellets.");
            FontSize = Config.Bind("Visual", "FontSize", 6.0f,
                "TextMeshPro font size for WorldTextMeshPro renderer.");
            ScreenFontSize = Config.Bind("Visual", "ScreenFontSize", 28,
                "Screen-space GUI font size.");
            ScreenFloatSpeed = Config.Bind("Visual", "ScreenFloatSpeed", 70.0f,
                "Screen-space upward speed in pixels per second.");
            ScreenJitter = Config.Bind("Visual", "ScreenJitter", 26.0f,
                "Random screen-space offset in pixels. Useful for PerHit shotgun pellets.");
            PerHitStackScreenSpread = Config.Bind("Visual", "PerHitStackScreenSpread", 12.8f,
                "Extra deterministic screen-space spread in pixels for multiple PerHit numbers created on the same target in the same frame. This makes shotgun pellets easier to read without delaying or merging damage.");
            PerHitStackWorldSpread = Config.Bind("Visual", "PerHitStackWorldSpread", 0.06f,
                "Extra deterministic world-space spread for multiple PerHit numbers created on the same target in the same frame. Used by WorldTextMeshPro and Both renderers.");
            MaxActiveNumbers = Config.Bind("Performance", "MaxActiveNumbers", 160,
                "Maximum active screen-space damage numbers. When exceeded, the oldest entries are discarded first to prevent UI spam during extreme PerHit cases.");
            DebugLog = Config.Bind("Debug", "DebugLog", false,
                "Print accepted/rejected damage events to LogOutput.log.");
            DebugSpawnTestOnF8 = Config.Bind("Debug", "DebugSpawnTestOnF8", true,
                "Press F8 in-game to spawn a test number at the screen center. This verifies the renderer even without hitting enemies.");

            DamageNumberManager.EnsureExists();

            _harmony = new Harmony(PluginGuid);
            DamageLogPatch.Apply(_harmony);

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded. Renderer=" + Renderer.Value + ", Mode=" + Mode.Value + ".");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
        }
    }

    public enum DisplayMode
    {
        PerTargetTotal = 1,
        PerHit = 2
    }

    public enum RenderMode
    {
        ScreenSpace = 1,
        WorldTextMeshPro = 2,
        Both = 3
    }

    public enum MergedColorMode
    {
        HighestMultiplier = 1,
        DamageWeightedAverage = 2,
        LastHit = 3
    }
}
