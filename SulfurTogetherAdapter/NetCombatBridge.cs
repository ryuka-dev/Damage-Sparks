using System;
using System.Collections.Generic;
using SULFURTogether.Api;
using UnityEngine;

namespace SulfurDamageNumbersMod.SulfurTogether
{
    /// <summary>
    /// Translates SULFUR Together's combat API into Damage Sparks' own terms; nothing else in the mod sees a
    /// SULFURTogether.Api type.
    ///
    /// On a co-op client the game never resolves the player's hit on a host-owned enemy, so the host's result is
    /// the only record of it. On the host the game does run the other player's hit, and that one must not count
    /// as the host's own.
    ///
    /// The mod merges the hits of one frame, but the results of one shotgun blast arrive over several frames.
    /// Hits are therefore grouped by the frame the client forwarded them in, which is the frame they were dealt
    /// in, and the group is handed over in one go, tagged with that frame, when its last result is in. Every
    /// forwarded hit resolves exactly once (at worst as Lost, after two seconds), so a group always completes.
    ///
    /// This assembly stays loadable on a SULFUR Together build without the API, so no API type may appear in a
    /// field, including compiler-generated ones. The handlers are instance methods because the compiler caches a
    /// delegate for a static method group in a generated static field typed with the API's argument class.
    /// </summary>
    internal sealed class NetCombatBridge : IDisposable
    {
        private const int SupportedApiVersion = 1;

        private readonly Dictionary<int, HitGroup> _groupBySequence = new Dictionary<int, HitGroup>();
        private readonly Dictionary<int, HitGroup> _groupByFrame = new Dictionary<int, HitGroup>();

        public static NetCombatBridge TryCreate()
        {
            if (NetCombat.ApiVersion < SupportedApiVersion)
                return null;

            NetCombatBridge bridge = new NetCombatBridge();
            NetCombat.LocalHitForwarded += bridge.OnLocalHitForwarded;
            NetCombat.LocalHitResolved += bridge.OnLocalHitResolved;
            DamageNumbersIntegration.SetRemoteHitScope(bridge.IsResolvingRemoteHit);
            return bridge;
        }

        public void Dispose()
        {
            NetCombat.LocalHitForwarded -= OnLocalHitForwarded;
            NetCombat.LocalHitResolved -= OnLocalHitResolved;
            DamageNumbersIntegration.SetRemoteHitScope(null);
            _groupBySequence.Clear();
            _groupByFrame.Clear();
        }

        private bool IsResolvingRemoteHit()
        {
            return NetCombat.IsResolvingRemoteHit;
        }

        private void OnLocalHitForwarded(LocalHitForwardedArgs hit)
        {
            int frame = Time.frameCount;

            HitGroup group;
            if (!_groupByFrame.TryGetValue(frame, out group))
            {
                group = new HitGroup(frame);
                _groupByFrame[frame] = group;
            }

            group.Unresolved++;
            _groupBySequence[hit.RequestSeq] = group;
        }

        private void OnLocalHitResolved(LocalHitResolvedArgs result)
        {
            HitGroup group;
            if (_groupBySequence.TryGetValue(result.RequestSeq, out group))
                _groupBySequence.Remove(result.RequestSeq);
            else
                group = null;

            if (result.Outcome == HitOutcome.Applied)
                Collect(result, group);

            if (group == null || --group.Unresolved > 0)
                return;

            // The last result for this frame's hits. Showing them together, in the order they were dealt, is what
            // lets the mod merge them under its usual same-frame rule.
            _groupByFrame.Remove(group.Frame);
            for (int i = 0; i < group.Hits.Count; i++)
                DamageNumbersIntegration.ShowResolvedHit(group.Hits[i]);
        }

        private static void Collect(LocalHitResolvedArgs result, HitGroup group)
        {
            Report(group, new ResolvedHit
            {
                Target = result.Target,
                Position = result.HitPosition,
                Weapon = result.Weapon,
                Melee = result.Melee,
                Damage = result.Damage,
                DamageType = result.DamageType,
                HitMultiplier = result.HitMultiplier,
                IsCritical = result.IsCritical,
                DealtFrame = group != null ? group.Frame : 0
            });

            // Damage the game dealt from inside this hit, such as Crusader's Holy damage. It carries no body part
            // of its own, so it is colored like any other damage without one.
            IReadOnlyList<SecondaryHit> secondary = result.Secondary;
            for (int i = 0; i < secondary.Count; i++)
            {
                SecondaryHit extra = secondary[i];
                Report(group, new ResolvedHit
                {
                    Target = result.Target,
                    Position = result.HitPosition,
                    Weapon = result.Weapon,
                    Melee = result.Melee,
                    Damage = extra.Damage,
                    DamageType = extra.DamageType,
                    HitMultiplier = 0f,
                    IsCritical = extra.IsCritical,
                    DealtFrame = group != null ? group.Frame : 0
                });
            }
        }

        private static void Report(HitGroup group, ResolvedHit hit)
        {
            // No group means the hit was forwarded before this bridge was listening.
            if (group != null)
                group.Hits.Add(hit);
            else
                DamageNumbersIntegration.ShowResolvedHit(hit);
        }

        private sealed class HitGroup
        {
            public readonly int Frame;
            public readonly List<ResolvedHit> Hits = new List<ResolvedHit>();
            public int Unresolved;

            public HitGroup(int frame)
            {
                Frame = frame;
            }
        }
    }
}
