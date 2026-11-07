using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.Units;
using UnityEngine;

namespace SulfurDamageNumbersMod
{
    /// <summary>
    /// Turns the game's damage pipeline into "the player dealt this much damage to this unit".
    ///
    /// Unit.ReceiveDamage logs the final amount through CreateDamageLog before it decides whether
    /// the damage is applied at all: protected NPCs, damage disabled in the settings, healing ally
    /// bullets and same-faction hits are logged and then cancelled. The log alone is therefore not
    /// the event. ReceiveDamage returns true exactly when it took health off the unit, so a hit is
    /// recorded when it is logged and only shown once the enclosing ReceiveDamage returns true.
    ///
    /// ReceiveDamage nests: an enchantment such as Crusader deals its own damage from the unit's
    /// onDamageRecieved callback, inside the outer call. Every call gets its own slot on a stack,
    /// so each log is paired with the call that made it. Hits are numbered in the order they are
    /// logged, which is the order the game dealt them, even though the inner call returns first.
    /// </summary>
    internal static class DamageLogPatch
    {
        private static bool _applied;

        private static readonly List<PendingHit> PendingStack = new List<PendingHit>();
        private static int _depth;
        private static int _nextSequence;

        public static void Apply(Harmony harmony)
        {
            if (_applied) return;

            MethodBase createDamageLog = FindCreateDamageLog();
            MethodBase receiveDamage = FindReceiveDamage();
            if (createDamageLog == null || receiveDamage == null)
            {
                Plugin.Log.LogError(
                    "SULFUR Damage Numbers failed: " +
                    (createDamageLog == null
                        ? "Unit.CreateDamageLog(float, float, DamageTypes, Hitmesh.Data, DamageSourceData)"
                        : "Unit.ReceiveDamage(float, DamageSourceData, Hitmesh.Data, Vector3?)") +
                    " was not found. No damage numbers will be shown.");
                return;
            }

            harmony.Patch(receiveDamage,
                prefix: new HarmonyMethod(typeof(DamageLogPatch), nameof(ReceiveDamagePrefix)),
                postfix: new HarmonyMethod(typeof(DamageLogPatch), nameof(ReceiveDamagePostfix)),
                finalizer: new HarmonyMethod(typeof(DamageLogPatch), nameof(ReceiveDamageFinalizer)));
            harmony.Patch(createDamageLog,
                postfix: new HarmonyMethod(typeof(DamageLogPatch), nameof(CreateDamageLogPostfix)));

            _applied = true;
            Plugin.Log.LogInfo("SULFUR Damage Numbers patched Unit.ReceiveDamage and Unit.CreateDamageLog.");
        }

        public static void Reset()
        {
            _applied = false;
            PendingStack.Clear();
            _depth = 0;
            _nextSequence = 0;
        }

        private static MethodBase FindCreateDamageLog()
        {
            return AccessTools.GetDeclaredMethods(typeof(Unit))
                .FirstOrDefault(m =>
                {
                    if (m.Name != "CreateDamageLog") return false;
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 5 &&
                           p[0].ParameterType == typeof(float) &&
                           p[1].ParameterType == typeof(float) &&
                           p[2].ParameterType == typeof(DamageTypes) &&
                           p[3].ParameterType == typeof(Hitmesh.Data) &&
                           p[4].ParameterType == typeof(DamageSourceData);
                });
        }

        private static MethodBase FindReceiveDamage()
        {
            // The base implementation. Npc and Breakable override it and call base.ReceiveDamage,
            // so patching the base body covers every unit type.
            return AccessTools.GetDeclaredMethods(typeof(Unit))
                .FirstOrDefault(m =>
                {
                    if (m.Name != "ReceiveDamage" || m.ReturnType != typeof(bool)) return false;
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 4 &&
                           p[0].ParameterType == typeof(float) &&
                           p[1].ParameterType == typeof(DamageSourceData) &&
                           p[2].ParameterType == typeof(Hitmesh.Data) &&
                           p[3].ParameterType == typeof(Vector3?);
                });
        }

        private static void ReceiveDamagePrefix(out int __state)
        {
            // __state is this call's slot. The postfix and the finalizer cut the stack back to it,
            // so an exception further down can never leave a stale slot behind.
            __state = _depth;
            if (_depth == PendingStack.Count)
                PendingStack.Add(default(PendingHit));
            else
                PendingStack[_depth] = default(PendingHit);
            _depth++;
        }

        private static void ReceiveDamagePostfix(bool __result, int __state)
        {
            if (__state < 0 || __state >= _depth)
                return;

            PendingHit hit = PendingStack[__state];
            PendingStack[__state] = default(PendingHit);
            _depth = __state;

            if (!hit.HasHit) return;

            if (!__result)
            {
                if (Plugin.DebugLog.Value)
                    Plugin.Log.LogInfo("[DamageNumbers] drop: the game did not apply the logged damage amount=" + hit.Amount + " target=" + SafeName(hit.Target));
                return;
            }

            // The unit may have been destroyed by the hit itself; the position is read inside.
            if (hit.Target == null) return;

            try
            {
                DamageNumberManager.RegisterDamage(hit.Target, hit.Source, hit.Amount, hit.Multiplier, hit.Sequence);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[DamageNumbers] ReceiveDamage postfix failed: " + e);
            }
        }

        private static Exception ReceiveDamageFinalizer(Exception __exception, int __state)
        {
            // The postfix does not run when ReceiveDamage throws. Drop this call's slot and
            // everything above it, and let the exception continue unchanged.
            if (__state >= 0 && __state < _depth)
            {
                for (int i = __state; i < _depth; i++)
                    PendingStack[i] = default(PendingHit);
                _depth = __state;
            }

            return __exception;
        }

        private static void CreateDamageLogPostfix(Unit __instance, float __0, Hitmesh.Data __3, DamageSourceData __4)
        {
            if (__instance == null || !Plugin.Enabled.Value) return;

            try
            {
                if (_depth <= 0)
                {
                    // CreateDamageLog has a single caller, Unit.ReceiveDamage. Another caller would
                    // have no ReceiveDamage result to confirm it with.
                    if (Plugin.DebugLog.Value)
                        Plugin.Log.LogInfo("[DamageNumbers] ignore damage log outside ReceiveDamage target=" + SafeName(__instance));
                    return;
                }

                float finalDamage = __0;
                if (float.IsNaN(finalDamage) || float.IsInfinity(finalDamage))
                {
                    if (Plugin.DebugLog.Value)
                        Plugin.Log.LogInfo("[DamageNumbers] reject non-finite damage amount=" + finalDamage + " target=" + SafeName(__instance));
                    return;
                }

                if (finalDamage < Plugin.MinDamageToShow.Value)
                {
                    if (Plugin.DebugLog.Value)
                        Plugin.Log.LogInfo("[DamageNumbers] reject low damage amount=" + finalDamage + " target=" + SafeName(__instance));
                    return;
                }

                string rejectReason;
                if (!IsAllowedTarget(__instance, out rejectReason))
                {
                    if (Plugin.DebugLog.Value)
                        Plugin.Log.LogInfo("[DamageNumbers] reject target: " + rejectReason + " target=" + SafeName(__instance));
                    return;
                }

                if (!IsPlayerExecutedAttack(__4, out rejectReason))
                {
                    if (Plugin.DebugLog.Value)
                        Plugin.Log.LogInfo("[DamageNumbers] reject source: " + rejectReason + " target=" + SafeName(__instance) + " source=" + DescribeSource(__4));
                    return;
                }

                // GetShapeMultiplier returns 1.0 for Parts.None, the same value a real head shot
                // produces. Parts.None means the damage carried no hitbox information at all -
                // status damage over time and explosion damage both pass Hitmesh.Data.Default -
                // so coloring it as a weakpoint hit would be wrong. Report it as "no multiplier
                // available" instead, which the color table already renders white.
                float hitMultiplier = __3.shapeId.part == HitboxColliders.Parts.None
                    ? 0f
                    : Mathf.Max(0f, __3.GetShapeMultiplier());

                if (Plugin.DebugLog.Value)
                    Plugin.Log.LogInfo("[DamageNumbers] accept amount=" + finalDamage + " multiplier=" + hitMultiplier + " part=" + __3.shapeId.part + " damageType=" + __4.damageType + " critical=" + __4.isCritical + " target=" + SafeName(__instance) + " source=" + DescribeSource(__4));

                PendingStack[_depth - 1] = new PendingHit
                {
                    HasHit = true,
                    Target = __instance,
                    Source = __4,
                    Amount = finalDamage,
                    Multiplier = hitMultiplier,
                    Sequence = ++_nextSequence
                };
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[DamageNumbers] CreateDamageLog postfix failed: " + e);
            }
        }

        private static bool IsAllowedTarget(Unit target, out string reason)
        {
            if (target.isPlayer)
            {
                reason = "target is player";
                return false;
            }

            if (Plugin.RequireNpcTarget.Value && !(target is Npc))
            {
                reason = "target is not Npc";
                return false;
            }

            if (VanillaDamageText.UnitShowsOwnDamageText(target))
            {
                reason = "the game draws its own damage text for this unit (target dummy)";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool IsPlayerExecutedAttack(DamageSourceData sourceData, out string reason)
        {
            bool sourceUnitIsPlayer = sourceData.sourceUnit != null && sourceData.sourceUnit.isPlayer;

            // Status damage over time is built by PeriodicModifier.CreateDamageSourceData, which
            // marks it with states.isPeriodicModifier and records only the unit that applied the
            // status: no weapon, no melee flag and no isPlayer flag, so sourceUnit is the only
            // player marker it has.
            bool statusOverTime = sourceData.states.isPeriodicModifier;

            if (!sourceData.isPlayer && !sourceUnitIsPlayer)
            {
                reason = "sourceData.isPlayer is false and sourceUnit is not the player";
                return false;
            }

            if (statusOverTime && !Plugin.ShowStatusEffectDamage.Value)
            {
                reason = "player-applied status damage is disabled";
                return false;
            }

            if (Plugin.RequireSourceUnitPlayer.Value && !sourceUnitIsPlayer)
            {
                reason = sourceData.sourceUnit == null ? "sourceUnit is null" : "sourceUnit is not player";
                return false;
            }

            if (sourceData.sourceWeapon != null)
            {
                reason = null;
                return true;
            }

            if (sourceData.melee)
            {
                reason = null;
                return true;
            }

            if (statusOverTime)
            {
                reason = null;
                return true;
            }

            if (Plugin.ShowPlayerOwnedSecondaryEffects.Value && !sourceData.states.notCreatedByPlayer)
            {
                reason = null;
                return true;
            }

            reason = "no weapon/melee/player-owned secondary marker";
            return false;
        }

        private static string DescribeSource(DamageSourceData sourceData)
        {
            string sourceUnit = sourceData.sourceUnit == null ? "null" : SafeName(sourceData.sourceUnit);
            string sourceWeapon = sourceData.sourceWeapon == null ? "null" : sourceData.sourceWeapon.name;
            return "isPlayer=" + sourceData.isPlayer +
                   ", melee=" + sourceData.melee +
                   ", periodic=" + sourceData.states.isPeriodicModifier +
                   ", weapon=" + sourceWeapon +
                   ", unit=" + sourceUnit +
                   ", instanceId=" + sourceData.instanceId +
                   ", name=" + sourceData.name;
        }

        private static string SafeName(UnityEngine.Object obj)
        {
            return obj == null ? "null" : obj.name;
        }

        private struct PendingHit
        {
            public bool HasHit;
            public Unit Target;
            public DamageSourceData Source;
            public float Amount;
            public float Multiplier;
            public int Sequence;
        }
    }
}
