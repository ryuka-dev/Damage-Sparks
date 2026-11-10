using System;
using System.Runtime.CompilerServices;
using SULFURTogether.Api;

namespace SulfurDamageNumbersMod.SulfurTogether
{
    /// <summary>
    /// Team marks, which SULFUR Together added in combat API 2: a hit on an enemy another player has marked
    /// deals more, and the mod shows that multiplier beside the hit's number. The damage reported on either end
    /// already includes it, so nothing here changes an amount.
    ///
    /// SULFUR Together shows the multiplier next to the crosshair unless a mod registers as a hit presenter.
    /// This one is registered exactly while the mod can draw the multiplier, so that a player never ends up
    /// with neither.
    ///
    /// Only create this on API 2 or newer. Each method that names an API 2 member is kept out of line, so that
    /// it is compiled when it first runs and not as part of a caller that also runs on API 1. As in
    /// <see cref="NetCombatBridge"/>, no field may name an API type, and the delegates are made from instance
    /// methods.
    /// </summary>
    internal sealed class TeamBonusBridge : IDisposable
    {
        // IDisposable is all SULFUR Together hands back for a registration.
        private IDisposable _hitPresenter;

        public static TeamBonusBridge Attach()
        {
            TeamBonusBridge bridge = new TeamBonusBridge();
            DamageNumbersIntegration.SetHitBonusScope(bridge.LocalHostHitMultiplier);
            DamageNumbersIntegration.ShowsHitBonusChanged += bridge.FollowHitBonusDisplay;
            bridge.FollowHitBonusDisplay();
            return bridge;
        }

        public void Dispose()
        {
            DamageNumbersIntegration.ShowsHitBonusChanged -= FollowHitBonusDisplay;
            DamageNumbersIntegration.SetHitBonusScope(null);
            ReleaseHitPresenter();
        }

        /// <summary>The team bonus the host applied to a client's hit; 1 when it applied none.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public float MultiplierOf(LocalHitResolvedArgs result)
        {
            return result.TeamMultiplier;
        }

        /// <summary>
        /// On the host, the team bonus of the damage call that is running. It is 1 inside damage the game
        /// deals from within that hit, inside another player's hit, on a client and offline.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private float LocalHostHitMultiplier()
        {
            return NetCombat.LocalHostHitMultiplier;
        }

        private void FollowHitBonusDisplay()
        {
            bool shows = DamageNumbersIntegration.ShowsHitBonus;
            if (shows == (_hitPresenter != null))
                return;

            if (shows)
                _hitPresenter = RegisterHitPresenter();
            else
                ReleaseHitPresenter();
        }

        private void ReleaseHitPresenter()
        {
            if (_hitPresenter == null) return;

            _hitPresenter.Dispose();
            _hitPresenter = null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IDisposable RegisterHitPresenter()
        {
            return NetTeamMarks.RegisterHitPresenter();
        }
    }
}
