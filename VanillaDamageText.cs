using System;
using System.Reflection;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.Units;
using PerfectRandom.Sulfur.Gameplay;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace SulfurDamageNumbersMod
{
    /// <summary>
    /// Integration boundary for the game's own target-dummy damage text.
    ///
    /// SULFUR ships the floating damage text as a serialized prefab reference on
    /// <c>PerfectRandom.Sulfur.Gameplay.DamageTracker</c>, which only exists on the dummy unit
    /// prefabs. There is no code-reachable asset for it, so the prefab and the damage-type colors
    /// are read from a dummy unit prefab loaded through the game's own unit addressable loader.
    ///
    /// Every reference to PerfectRandom.Sulfur.Gameplay types and to Addressables is kept inside
    /// this class and <see cref="VanillaDamageNumberView"/>. The rest of the mod only sees
    /// <see cref="IsReady"/>, <see cref="TextPrefab"/> and <see cref="GetDamageTypeColor"/>.
    /// </summary>
    internal static class VanillaDamageText
    {
        private enum LoadState
        {
            Idle = 0,
            Loading = 1,
            Ready = 2,
            Unavailable = 3
        }

        // The dummy units that may carry a DamageTracker. Whichever resolves first is used.
        // On 0.18.5 only the deployable TargetDummy (Unit_Enemy_Target_Dummy) has one; the
        // shooting-range TestDummy does not, so trying it first only wastes an asset load.
        // TestDummy is kept as a fallback in case a later build moves the component.
        private static readonly UnitId[] DummyUnitCandidates =
        {
            UnitIds.TargetDummy,
            UnitIds.TestDummy
        };

        private const float RetryInterval = 2f;

        private static LoadState _state;
        private static int _candidateIndex;
        private static float _nextAttemptTime;
        private static bool _unavailableLogged;

        private static AsyncOperationHandle<GameObject> _handle;
        private static bool _handleAcquired;

        private static GameObject _textPrefab;
        private static Color _colorElectric = Color.white;
        private static Color _colorExplosive = Color.white;
        private static Color _colorFire = Color.white;
        private static Color _colorFrost = Color.white;
        private static Color _colorHoly = Color.white;
        private static Color _colorPoison = Color.white;
        private static Color _colorPunish = Color.white;
        private static Color _colorShadow = Color.white;
        private static Color _colorWater = Color.white;

        /// <summary>True once the game's damage text prefab and colors are available.</summary>
        public static bool IsReady
        {
            get { return _state == LoadState.Ready && _textPrefab != null; }
        }

        public static GameObject TextPrefab
        {
            get { return _textPrefab; }
        }

        /// <summary>
        /// Drives the asynchronous lookup. Safe to call every frame; it does nothing once the
        /// prefab is resolved or once every candidate has failed.
        /// </summary>
        public static void Poll()
        {
            if (_state == LoadState.Unavailable)
                return;

            if (_state == LoadState.Ready)
            {
                // The game releases unit addressables on some environment changes. The retained
                // handle should keep this asset alive, but a destroyed prefab must never be used.
                if (_textPrefab != null)
                    return;

                Plugin.Log.LogWarning("[DamageNumbers] The game's damage text prefab was unloaded. Reloading it.");
                ResetLookup();
            }

            try
            {
                PollInternal();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[DamageNumbers] Vanilla damage text lookup failed: " + e.Message);
                ReleaseHandle();
                _candidateIndex++;
                _state = LoadState.Idle;
                _nextAttemptTime = Time.unscaledTime + RetryInterval;
                MarkUnavailableIfExhausted();
            }
        }

        private static void PollInternal()
        {
            if (_state == LoadState.Idle)
            {
                if (Time.unscaledTime < _nextAttemptTime)
                    return;

                if (!TryStartCurrentCandidate())
                {
                    _nextAttemptTime = Time.unscaledTime + RetryInterval;
                    return;
                }

                _state = LoadState.Loading;
            }

            if (!_handle.IsValid())
            {
                _state = LoadState.Idle;
                return;
            }

            if (!_handle.IsDone)
                return;

            if (_handle.Status != AsyncOperationStatus.Succeeded || _handle.Result == null)
            {
                FailCurrentCandidate("unit prefab could not be loaded");
                return;
            }

            DamageTracker tracker = _handle.Result.GetComponentInChildren<DamageTracker>(true);
            if (tracker == null)
            {
                FailCurrentCandidate("unit prefab has no DamageTracker");
                return;
            }

            GameObject prefab = ReadPrefabField(tracker);
            if (prefab == null)
            {
                FailCurrentCandidate("DamageTracker has no damage text prefab");
                return;
            }

            if (prefab.GetComponent<DamageTextInstance>() == null)
            {
                FailCurrentCandidate("damage text prefab has no DamageTextInstance");
                return;
            }

            ReadColors(tracker);
            _textPrefab = prefab;
            _state = LoadState.Ready;
            Plugin.Log.LogInfo("[DamageNumbers] Using the game's damage text prefab '" + prefab.name +
                               "' from unit prefab '" + _handle.Result.name + "'.");

            if (Plugin.DebugLog.Value)
            {
                Plugin.Log.LogInfo("[DamageNumbers] Damage type colors read from DamageTracker: " +
                                   "fire=" + _colorFire + ", poison=" + _colorPoison + ", frost=" + _colorFrost +
                                   ", electric=" + _colorElectric + ", explosive=" + _colorExplosive +
                                   ", holy=" + _colorHoly + ", shadow=" + _colorShadow + ", water=" + _colorWater + ".");
            }
        }

        private static bool TryStartCurrentCandidate()
        {
            if (_candidateIndex >= DummyUnitCandidates.Length)
            {
                MarkUnavailableIfExhausted();
                return false;
            }

            AsyncAssetLoading assetLoading = StaticInstance<AsyncAssetLoading>.Instance;
            if (assetLoading == null || assetLoading.unitDatabase == null)
                return false;

            UnitSO unitSo = DummyUnitCandidates[_candidateIndex].GetAsset();
            if (unitSo == null)
            {
                FailCurrentCandidate("unit asset is missing");
                return false;
            }

            // FetchAndLoadUnitLoader caches one handle per UnitSO, so this reuses the game's own
            // load when the dummy is already in use. Acquire adds a reference that keeps the asset
            // alive if the game later releases its own.
            _handle = unitSo.FetchAndLoadUnitLoader();
            if (!_handle.IsValid())
            {
                FailCurrentCandidate("unit loader handle is invalid");
                return false;
            }

            Addressables.ResourceManager.Acquire(_handle);
            _handleAcquired = true;
            return true;
        }

        private static void FailCurrentCandidate(string reason)
        {
            if (Plugin.DebugLog.Value && _candidateIndex < DummyUnitCandidates.Length)
                Plugin.Log.LogInfo("[DamageNumbers] Dummy candidate " + _candidateIndex + " rejected: " + reason + ".");

            ReleaseHandle();
            _candidateIndex++;
            _state = LoadState.Idle;
            _nextAttemptTime = Time.unscaledTime + RetryInterval;
            MarkUnavailableIfExhausted();
        }

        private static void MarkUnavailableIfExhausted()
        {
            if (_candidateIndex < DummyUnitCandidates.Length)
                return;

            _state = LoadState.Unavailable;
            if (_unavailableLogged)
                return;

            _unavailableLogged = true;
            Plugin.Log.LogWarning(
                "[DamageNumbers] The game's damage text prefab could not be found on any dummy unit. " +
                "Renderer=Vanilla falls back to ScreenSpace.");
        }

        private static GameObject ReadPrefabField(DamageTracker tracker)
        {
            FieldInfo field = AccessTools.Field(typeof(DamageTracker), "damageTextInstancePrefab");
            if (field == null)
                return null;

            return field.GetValue(tracker) as GameObject;
        }

        private static void ReadColors(DamageTracker tracker)
        {
            _colorElectric = ReadColor(tracker, "colorElectric");
            _colorExplosive = ReadColor(tracker, "colorExplosive");
            _colorFire = ReadColor(tracker, "colorFire");
            _colorFrost = ReadColor(tracker, "colorFrost");
            _colorHoly = ReadColor(tracker, "colorHoly");
            _colorPoison = ReadColor(tracker, "colorPoison");
            _colorPunish = ReadColor(tracker, "colorPunish");
            _colorShadow = ReadColor(tracker, "colorShadow");
            _colorWater = ReadColor(tracker, "colorWater");
        }

        private static Color ReadColor(DamageTracker tracker, string fieldName)
        {
            FieldInfo field = AccessTools.Field(typeof(DamageTracker), fieldName);
            if (field == null || field.FieldType != typeof(Color))
                return Color.white;

            Color color = (Color)field.GetValue(tracker);
            color.a = 1f;
            return color;
        }

        /// <summary>
        /// Damage-type color table copied from the game's own DamageTracker.DamageColor rule.
        /// Types the game does not map fall back to white, exactly like the game does; that
        /// includes Normal, which is why the tracker's colorNormal field is not read.
        /// </summary>
        public static Color GetDamageTypeColor(DamageTypes damageType)
        {
            switch (damageType)
            {
                case DamageTypes.Poison: return _colorPoison;
                case DamageTypes.Fire: return _colorFire;
                case DamageTypes.Holy: return _colorHoly;
                case DamageTypes.Electric: return _colorElectric;
                case DamageTypes.Explosive: return _colorExplosive;
                case DamageTypes.Frost: return _colorFrost;
                case DamageTypes.Critical: return Color.white;
                case DamageTypes.Shadow: return _colorShadow;
                case DamageTypes.Water: return _colorWater;
                default: return Color.white;
            }
        }

        public static void Shutdown()
        {
            ResetLookup();
            _candidateIndex = 0;
            _state = LoadState.Idle;
            _nextAttemptTime = 0f;
            _unavailableLogged = false;
        }

        private static void ResetLookup()
        {
            ReleaseHandle();
            _textPrefab = null;
            _state = LoadState.Idle;
            _candidateIndex = 0;
            _nextAttemptTime = 0f;
        }

        private static void ReleaseHandle()
        {
            if (!_handleAcquired)
            {
                _handle = default(AsyncOperationHandle<GameObject>);
                return;
            }

            _handleAcquired = false;
            try
            {
                if (_handle.IsValid())
                    Addressables.Release(_handle);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[DamageNumbers] Releasing the dummy unit handle failed: " + e.Message);
            }

            _handle = default(AsyncOperationHandle<GameObject>);
        }
    }
}
