using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.Units;
using UnityEngine;

namespace SulfurDamageNumbersMod
{
    internal static class DamageLogPatch
    {
        private static bool _applied;

        [ThreadStatic]
        private static bool _handlingPostfix;

        public static void Apply(Harmony harmony)
        {
            if (_applied) return;

            MethodBase target = TargetMethod();
            if (target == null)
            {
                Plugin.Log.LogError("SULFUR Damage Numbers failed: Unit.CreateDamageLog(float, float, DamageTypes, Hitmesh.Data, DamageSourceData) was not found.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(DamageLogPatch), nameof(Postfix)));
            _applied = true;
            Plugin.Log.LogInfo("SULFUR Damage Numbers patched " + target.DeclaringType.FullName + "." + target.Name + ".");
        }

        public static void Reset()
        {
            _applied = false;
            _handlingPostfix = false;
        }

        private static MethodBase TargetMethod()
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

        private static void Postfix(Unit __instance, float __0, float __1, DamageTypes __2, Hitmesh.Data __3, DamageSourceData __4)
        {
            if (__instance == null || !Plugin.Enabled.Value || _handlingPostfix) return;

            _handlingPostfix = true;
            try
            {
                float finalDamage = __0;
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

                float hitMultiplier = Mathf.Max(0f, __3.GetShapeMultiplier());

                if (Plugin.DebugLog.Value)
                    Plugin.Log.LogInfo("[DamageNumbers] accept amount=" + finalDamage + " multiplier=" + hitMultiplier + " target=" + SafeName(__instance) + " source=" + DescribeSource(__4));

                DamageNumberManager.RegisterDamage(__instance, __4, finalDamage, hitMultiplier);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[DamageNumbers] CreateDamageLog postfix failed: " + e);
            }
            finally
            {
                _handlingPostfix = false;
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

            reason = null;
            return true;
        }

        private static bool IsPlayerExecutedAttack(DamageSourceData sourceData, out string reason)
        {
            if (!sourceData.isPlayer)
            {
                reason = "sourceData.isPlayer is false";
                return false;
            }

            if (Plugin.RequireSourceUnitPlayer.Value)
            {
                if (sourceData.sourceUnit == null)
                {
                    reason = "sourceUnit is null";
                    return false;
                }

                if (!sourceData.sourceUnit.isPlayer)
                {
                    reason = "sourceUnit is not player";
                    return false;
                }
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
                   ", weapon=" + sourceWeapon +
                   ", unit=" + sourceUnit +
                   ", instanceId=" + sourceData.instanceId +
                   ", name=" + sourceData.name;
        }

        private static string SafeName(UnityEngine.Object obj)
        {
            return obj == null ? "null" : obj.name;
        }
    }
}
