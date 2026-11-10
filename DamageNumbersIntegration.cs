using System;
using UnityEngine;

namespace SulfurDamageNumbersMod
{
    /// <summary>A hit another system resolved for the local player, in this mod's own terms.</summary>
    public struct ResolvedHit
    {
        /// <summary>The unit that was hit (a game Unit), or null when it no longer exists locally.</summary>
        public object Target;

        /// <summary>Where the hit landed; places the number when <see cref="Target"/> is gone.</summary>
        public Vector3? Position;

        /// <summary>The weapon that dealt the hit (a game Weapon), or null for weaponless damage.</summary>
        public object Weapon;

        public bool Melee;

        /// <summary>The final damage, after crits, hitbox multipliers and mitigation.</summary>
        public float Damage;

        /// <summary>The game's DamageTypes value.</summary>
        public int DamageType;

        /// <summary>The body part's multiplier; 0 when the damage carried no body part.</summary>
        public float HitMultiplier;

        public bool IsCritical;

        /// <summary>
        /// A bonus another system applied to this hit, shown as a small multiplier beside its number, for
        /// example 1.5. <see cref="Damage"/> already includes it. 1 or less means the hit had none.
        /// </summary>
        public float BonusMultiplier;

        /// <summary>
        /// Time.frameCount of the frame the hit was dealt in, which is what same-frame merging goes by.
        /// 0 means the frame it is reported in.
        /// </summary>
        public int DealtFrame;
    }

    /// <summary>
    /// The surface an optional integration plugin uses. The dependency points one way: an integration
    /// references this assembly, and this assembly knows nothing about any integration. Without one,
    /// everything here is inert.
    /// </summary>
    public static class DamageNumbersIntegration
    {
        // Below this a multiplier would be drawn as "x1"; above the cap the label would outgrow the number.
        private const float MinShownBonus = 1.005f;
        private const float MaxShownBonus = 99f;

        private static Func<bool> _isResolvingRemoteHit;
        private static Func<float> _currentHitBonus;
        private static bool _showsHitBonus;

        /// <summary>
        /// Sets the check for "the damage running right now belongs to another player": such a hit goes
        /// through the game's damage functions on this machine but is not the local player's. Pass null
        /// to clear it. Called on the main thread, once per damage event.
        /// </summary>
        public static void SetRemoteHitScope(Func<bool> isResolvingRemoteHit)
        {
            _isResolvingRemoteHit = isResolvingRemoteHit;
        }

        /// <summary>
        /// Sets the read for "the bonus another system applied to the damage running right now": a
        /// multiplier such as 1.5 that the logged damage already includes, or 1 when there is none. It is
        /// only shown beside the number, never applied. Pass null to clear it. Called on the main thread,
        /// once per accepted damage event, from inside the game's damage call.
        /// </summary>
        public static void SetHitBonusScope(Func<float> currentHitBonus)
        {
            _currentHitBonus = currentHitBonus;
        }

        /// <summary>
        /// Whether a hit's bonus multiplier is drawn beside its number right now. False while the mod is
        /// disabled and while the renderer in use cannot draw it (the game's own damage text). An
        /// integration that takes this display over from another mod should only do so while this is true.
        /// </summary>
        public static bool ShowsHitBonus
        {
            get { return _showsHitBonus; }
        }

        /// <summary>Raised on the main thread when <see cref="ShowsHitBonus"/> changes.</summary>
        public static event Action ShowsHitBonusChanged;

        /// <summary>
        /// Shows a hit the game did not resolve on this machine. It goes through this mod's display filters,
        /// modes and limits like a local hit; the caller vouches that it is the local player's own hit, so
        /// the source checks that need the game's damage data do not apply. Main thread only; report the
        /// hits in the order they were dealt, and the hits of one frame together.
        /// </summary>
        public static void ShowResolvedHit(ResolvedHit hit)
        {
            DamageLogPatch.ShowResolvedHit(hit);
        }

        internal static bool IsResolvingRemoteHit
        {
            get { return _isResolvingRemoteHit != null && _isResolvingRemoteHit(); }
        }

        /// <summary>The bonus of the damage call that is running; 0 when it has none.</summary>
        internal static float CurrentHitBonus
        {
            get { return _currentHitBonus != null ? SanitizeBonus(_currentHitBonus()) : 0f; }
        }

        /// <summary>
        /// A bonus comes from another mod, so anything that is not a real multiplier above 1 counts as
        /// "no bonus", which is 0 everywhere inside this mod.
        /// </summary>
        internal static float SanitizeBonus(float bonus)
        {
            if (float.IsNaN(bonus) || float.IsInfinity(bonus) || bonus < MinShownBonus)
                return 0f;
            return Mathf.Min(bonus, MaxShownBonus);
        }

        internal static void SetShowsHitBonus(bool shows)
        {
            if (_showsHitBonus == shows) return;

            _showsHitBonus = shows;

            Action handlers = ShowsHitBonusChanged;
            if (handlers == null) return;

            try
            {
                handlers();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[DamageNumbers] An integration failed while following the hit bonus display: " + e);
            }
        }

        internal static void Reset()
        {
            _isResolvingRemoteHit = null;
            _currentHitBonus = null;
            SetShowsHitBonus(false);
        }
    }
}
