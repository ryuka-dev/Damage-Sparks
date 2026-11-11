using System;
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
        public const string PluginVersion = "1.2.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<DisplayMode> Mode;
        internal static ConfigEntry<RenderMode> Renderer;
        internal static ConfigEntry<MergedColorMode> AggregateColorMode;
        internal static ConfigEntry<bool> VanillaUseDamageTypeColors;
        internal static ConfigEntry<float> VanillaScale;
        internal static ConfigEntry<bool> UseWeakspotColors;
        internal static ConfigEntry<bool> TreatOneMultiplierAsNormal;
        internal static ConfigEntry<bool> RequireNpcTarget;
        internal static ConfigEntry<bool> RequireSourceUnitPlayer;
        internal static ConfigEntry<bool> ShowPlayerOwnedSecondaryEffects;
        internal static ConfigEntry<bool> ShowStatusEffectDamage;
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
        internal static ConfigEntry<int> MaxNumbersPerFrame;
        internal static ConfigEntry<bool> EnableCrowdingProtection;
        internal static ConfigEntry<float> CrowdingRadius;
        internal static ConfigEntry<int> MaxNearbyNumbers;
        internal static ConfigEntry<bool> DebugLog;
        internal static ConfigEntry<bool> DebugSpawnTestOnF8;

        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true, "Enable damage number display.");
            Mode = Config.Bind("General", "DamageDisplayMode", DisplayMode.PerTargetTotal,
                "PerTargetTotal = damage of one type from the same player attack source on the same target in the same frame is merged into one number; every further number on that target in the same frame (another damage type such as the Holy damage of Scroll of Crusader, or another attack source) is shown one line higher. PerHit = each damage event the game applies is shown separately. Crowding and frame-budget settings are independent visual performance limits applied after either mode.");
            Renderer = Config.Bind("Visual", "Renderer", RenderMode.ScreenSpace,
                "ScreenSpace uses pooled TextMeshProUGUI objects. WorldTextMeshPro uses pooled 3D TextMeshPro objects. Both renders both at the same time. Vanilla reuses the game's own damage text prefab, the one the target dummies use, with its original animation, font and damage-type colors; it shows whole numbers only and falls back to ScreenSpace if the game does not provide that prefab.");
            VanillaUseDamageTypeColors = Config.Bind("Visual", "VanillaUseDamageTypeColors", true,
                "Renderer=Vanilla only. True uses the game's damage-type colors exactly like the target dummies. False keeps this mod's weakspot multiplier colors on the vanilla animation.");
            VanillaScale = Config.Bind("Visual", "VanillaScale", 1.0f,
                "Renderer=Vanilla only. Scale applied to the game's damage text. 1.0 is the size the target dummies use.");
            UseWeakspotColors = Config.Bind("Visual", "UseWeakspotColors", true,
                "Use body-part multiplier colors. Red >= 1.5 (eyes), orange >= 1.0 (heads), yellow >= 0.75 (thorax), white below that (body, limbs) and for damage that carries no body part, such as status effects and explosions.");
            TreatOneMultiplierAsNormal = Config.Bind("Visual", "TreatOneMultiplierAsNormal", false,
                "If true, multiplier 1.0 is shown as white instead of orange. Head hits are 1.0, so this makes them white like body hits.");
            AggregateColorMode = Config.Bind("Visual", "AggregateColorMode", MergedColorMode.HighestMultiplier,
                "Color rule for PerTargetTotal. HighestMultiplier = strongest hit color. DamageWeightedAverage = damage-weighted multiplier. LastHit = last hit color.");
            RequireNpcTarget = Config.Bind("Filter", "RequireNpcTarget", true,
                "Only show damage numbers on Npc targets. Keep true to avoid barrels/objects if they also use Unit damage code.");
            RequireSourceUnitPlayer = Config.Bind("Filter", "RequireSourceUnitPlayer", false,
                "If true, sourceData.sourceUnit must exist and be the player. Default false because some projectiles/explosions keep only the game's isPlayer flag.");
            ShowPlayerOwnedSecondaryEffects = Config.Bind("Filter", "ShowPlayerOwnedSecondaryEffects", true,
                "Show player-owned secondary damage that has no weapon or melee source, such as projectile explosions, if the game reports it as created by the player. Status damage over time is controlled by ShowStatusEffectDamage.");
            ShowStatusEffectDamage = Config.Bind("Filter", "ShowStatusEffectDamage", true,
                "Show damage over time from status effects the player applied, such as burning, poison and electrocution. These are the colored numbers the target dummies show.");
            MinDamageToShow = Config.Bind("Filter", "MinDamageToShow", 0.01f,
                "Final damage below this value will not be shown.");
            ShowDecimals = Config.Bind("Visual", "ShowDecimals", false,
                "Show one decimal place for non-integer damage. Integer values such as 5.0 are still shown as 5. Renderer=Vanilla always shows whole numbers.");
            Lifetime = Config.Bind("Visual", "Lifetime", 0.85f,
                "Damage number lifetime in seconds. Renderer=Vanilla follows the game's own animation instead.");
            FloatSpeed = Config.Bind("Visual", "FloatSpeed", 1.25f,
                "WorldTextMeshPro renderer only. World-space upward speed.");
            WorldScale = Config.Bind("Visual", "WorldScale", 0.045f,
                "WorldTextMeshPro renderer only. World-space scale of each 3D damage number.");
            RandomJitter = Config.Bind("Visual", "RandomJitter", 0.18f,
                "PerHit mode only. Random horizontal world-space offset for each number, useful for shotgun pellets. Not used by the ScreenSpace renderer.");
            FontSize = Config.Bind("Visual", "FontSize", 8.0f,
                "WorldTextMeshPro renderer only. TextMeshPro font size of the 3D damage numbers.");
            ScreenFontSize = Config.Bind("Visual", "ScreenFontSize", 30,
                "ScreenSpace renderer only. Screen-space TextMeshPro font size.");
            ScreenFloatSpeed = Config.Bind("Visual", "ScreenFloatSpeed", 70.0f,
                "ScreenSpace renderer only. Extra upward speed in pixels per second. Numbers also drift up slowly with the point above the target, so 0 does not hold them completely still.");
            ScreenJitter = Config.Bind("Visual", "ScreenJitter", 26.0f,
                "PerHit mode and ScreenSpace renderer only. Random screen-space offset in pixels for each number, useful for shotgun pellets.");
            PerHitStackScreenSpread = Config.Bind("Visual", "PerHitStackScreenSpread", 12.8f,
                "ScreenSpace renderer only. Extra deterministic screen-space spread in pixels for multiple PerHit numbers created on the same target in the same frame. The offset is capped at 42 pixels.");
            PerHitStackWorldSpread = Config.Bind("Visual", "PerHitStackWorldSpread", 0.06f,
                "Extra deterministic world-space spread for multiple PerHit numbers created on the same target in the same frame. Not used by the ScreenSpace renderer. The offset is capped at 0.36.");
            MaxActiveNumbers = Config.Bind("Performance", "MaxActiveNumbers", 160,
                "Maximum active damage numbers in each enabled renderer. Values are hard-clamped to 1-256, or 1-64 for Renderer=Vanilla, and the oldest active number is reused when full.");
            MaxNumbersPerFrame = Config.Bind("Performance", "MaxNumbersPerFrame", 32,
                "Maximum new logical damage numbers spawned in one frame. Values are hard-clamped to 1-256. Extra events are dropped to protect Unity's GC during extreme hit spam.");
            EnableCrowdingProtection = Config.Bind("Performance", "EnableCrowdingProtection", true,
                "Skip a new number when several active numbers already occupy the same small screen area. This is a visual limiter applied after the selected display mode; it does not merge PerHit damage or change PerTargetTotal aggregation.");
            CrowdingRadius = Config.Bind("Performance", "CrowdingRadius", 36f,
                "Screen-space radius in pixels used by crowding protection. Values are hard-clamped to 8-120.");
            MaxNearbyNumbers = Config.Bind("Performance", "MaxNearbyNumbers", 5,
                "Maximum existing numbers allowed inside the crowding radius before a new visually covered number is skipped. Values are hard-clamped to 1-32.");
            DebugLog = Config.Bind("Debug", "DebugLog", false,
                "Print accepted/rejected damage events and rate-limit warnings to LogOutput.log.");
            DebugSpawnTestOnF8 = Config.Bind("Debug", "DebugSpawnTestOnF8", false,
                "Press F8 in-game to spawn a test number at the screen center. The test number always uses the ScreenSpace style.");

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

            DamageLogPatch.Reset();
            DamageNumberManager.Shutdown();

            try
            {
                VanillaDamageText.Shutdown();
            }
            catch (Exception e)
            {
                Logger.LogWarning("Releasing the game's damage text assets failed: " + e.Message);
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
        Both = 3,
        Vanilla = 4
    }

    public enum MergedColorMode
    {
        HighestMultiplier = 1,
        DamageWeightedAverage = 2,
        LastHit = 3
    }
}
