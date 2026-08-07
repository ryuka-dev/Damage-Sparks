using System;
using System.Collections.Generic;
using System.Globalization;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.Units;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SulfurDamageNumbersMod
{
    internal sealed class DamageNumberManager : MonoBehaviour
    {
        private const int HardMaxActiveNumbers = 256;
        private const int HardMaxNumbersPerFrame = 256;

        // Each vanilla number is a clone of the game's prefab and carries its own Animator and
        // 3D TextMeshPro, so this renderer is capped lower than the plain text renderers.
        private const int HardMaxActiveVanillaNumbers = 64;

        private static DamageNumberManager _instance;

        private readonly Dictionary<AggregateKey, AggregateEntry> _aggregates = new Dictionary<AggregateKey, AggregateEntry>();
        private readonly List<AggregateKey> _flushBuffer = new List<AggregateKey>();
        private readonly Dictionary<PerHitSpreadKey, int> _perHitSpreadCounters = new Dictionary<PerHitSpreadKey, int>();
        private readonly List<ScreenDamageNumberView> _screenPool = new List<ScreenDamageNumberView>();
        private readonly List<DamageNumberView> _worldPool = new List<DamageNumberView>();
        private readonly List<VanillaDamageNumberView> _vanillaPool = new List<VanillaDamageNumberView>();
        private readonly List<Vector2> _crowdingPositions = new List<Vector2>(HardMaxActiveNumbers);

        private bool _vanillaReady;
        private bool _vanillaBoundaryBroken;
        private int _vanillaFlyIndex;

        private int _perHitSpreadFrame = -1;
        private int _crowdingFrame = -1;
        private int _spawnBudgetFrame = -1;
        private int _spawnedThisFrame;
        private int _droppedThisFrame;
        private int _crowdingSkippedThisFrame;
        private float _lastBudgetLogTime = -999f;
        private bool _inputErrorLogged;
        private Camera _cachedCamera;
        private float _nextCameraLookupTime;
        private Material _screenSharedMaterial;
        private Material _worldSharedMaterial;
        private RenderMode _lastAppliedRenderMode;
        private bool _hasAppliedRenderMode;

        private RectTransform _screenRoot;
        private Transform _worldRoot;
        private Transform _vanillaRoot;

        public static void EnsureExists()
        {
            if (_instance != null) return;

            GameObject go = new GameObject("SULFUR Damage Number Manager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DamageNumberManager>();
        }

        public static void Shutdown()
        {
            if (_instance == null) return;

            DamageNumberManager instance = _instance;
            _instance = null;
            Destroy(instance.gameObject);
        }

        public static void RegisterDamage(Unit target, DamageSourceData sourceData, float amount, float hitMultiplier)
        {
            if (target == null) return;

            EnsureExists();
            if (_instance != null)
                _instance.RegisterDamageInternal(target, sourceData, amount, hitMultiplier);
        }

        public static void SpawnScreenTestNumber()
        {
            EnsureExists();
            if (_instance != null)
            {
                _instance.SpawnScreenFixed(
                    "999",
                    new Vector2(Screen.width * 0.5f, Screen.height * 0.45f),
                    DamageNumberColor.ForMultiplier(0f));
            }
        }

        private void Update()
        {
            if (!Plugin.DebugSpawnTestOnF8.Value) return;

            try
            {
                if (Input.GetKeyDown(KeyCode.F8))
                {
                    SpawnScreenTestNumber();
                    if (Plugin.DebugLog.Value)
                        Plugin.Log.LogInfo("[DamageNumbers] F8 test number spawned.");
                }
            }
            catch (Exception e)
            {
                if (!_inputErrorLogged)
                {
                    _inputErrorLogged = true;
                    Plugin.Log.LogWarning("[DamageNumbers] F8 debug input is unavailable: " + e.Message);
                }
            }
        }

        private void RegisterDamageInternal(Unit target, DamageSourceData sourceData, float amount, float hitMultiplier)
        {
            Vector3 position = GetDamagePosition(target);

            if (Plugin.Mode.Value == DisplayMode.PerHit)
            {
                PerHitSpreadOffset spread = GetNextPerHitSpread(target, sourceData);
                SpawnNumber(position, amount, hitMultiplier, sourceData.damageType, sourceData.isCritical,
                    true, spread.WorldOffset, spread.ScreenOffset, true);
                return;
            }

            AggregateKey key = AggregateKey.Create(target, sourceData);
            AggregateEntry entry;
            if (!_aggregates.TryGetValue(key, out entry))
            {
                entry = new AggregateEntry
                {
                    Amount = 0f,
                    Position = position,
                    Frame = Time.frameCount,
                    HighestMultiplier = 0f,
                    LastMultiplier = 0f,
                    WeightedMultiplierSum = 0f,
                    // AggregateKey already separates damage types, so every hit merged into one
                    // entry shares this value.
                    DamageType = sourceData.damageType,
                    AnyCritical = false
                };
            }

            entry.Amount += amount;
            entry.Position = position;
            entry.Frame = Time.frameCount;
            entry.LastMultiplier = hitMultiplier;
            entry.AnyCritical = entry.AnyCritical || sourceData.isCritical;
            entry.WeightedMultiplierSum += Mathf.Max(0f, hitMultiplier) * Mathf.Max(0f, amount);
            if (hitMultiplier > entry.HighestMultiplier)
                entry.HighestMultiplier = hitMultiplier;

            _aggregates[key] = entry;
        }

        private void LateUpdate()
        {
            UpdateVanillaAvailability();
            FlushAggregates();
            ApplyRendererSelectionIfChanged();

            int maxActive = GetMaxActiveNumbers();
            EnforcePoolLimit(_screenPool, maxActive);
            EnforcePoolLimit(_worldPool, maxActive);
            EnforcePoolLimit(_vanillaPool, GetMaxActiveVanillaNumbers());

            Camera camera = GetMainCamera();
            float now = Time.time;

            for (int i = 0; i < _screenPool.Count; i++)
            {
                ScreenDamageNumberView view = _screenPool[i];
                if (view.IsActive)
                    view.Tick(camera, now, Plugin.ScreenFloatSpeed.Value);
            }

            for (int i = 0; i < _worldPool.Count; i++)
            {
                DamageNumberView view = _worldPool[i];
                if (view.IsActive)
                    view.Tick(camera, now);
            }

            TickVanillaPool(camera, now);

            MaybeLogDroppedBudget();
        }

        /// <summary>
        /// Keeps the game's damage text lookup running while the vanilla renderer is selected and
        /// records whether it can be used this frame. Failures inside the vanilla integration
        /// boundary disable only that renderer; the other renderers keep working.
        /// </summary>
        private void UpdateVanillaAvailability()
        {
            if (_vanillaBoundaryBroken || Plugin.Renderer.Value != RenderMode.Vanilla)
            {
                _vanillaReady = false;
                return;
            }

            try
            {
                VanillaDamageText.Poll();
                _vanillaReady = VanillaDamageText.IsReady;
            }
            catch (Exception e)
            {
                BreakVanillaBoundary(e);
            }
        }

        private void TickVanillaPool(Camera camera, float now)
        {
            if (_vanillaPool.Count == 0) return;

            try
            {
                for (int i = 0; i < _vanillaPool.Count; i++)
                {
                    VanillaDamageNumberView view = _vanillaPool[i];
                    if (view.IsActive)
                        view.Tick(camera, now);
                }
            }
            catch (Exception e)
            {
                BreakVanillaBoundary(e);
            }
        }

        private void BreakVanillaBoundary(Exception e)
        {
            _vanillaReady = false;
            if (_vanillaBoundaryBroken) return;

            _vanillaBoundaryBroken = true;
            _vanillaPool.Clear();
            if (_vanillaRoot != null)
            {
                Destroy(_vanillaRoot.gameObject);
                _vanillaRoot = null;
            }

            Plugin.Log.LogWarning(
                "[DamageNumbers] The game's damage text renderer failed and was disabled; " +
                "Renderer=Vanilla falls back to ScreenSpace. " + e);
        }

        private void FlushAggregates()
        {
            if (_aggregates.Count == 0) return;

            _flushBuffer.Clear();
            foreach (KeyValuePair<AggregateKey, AggregateEntry> pair in _aggregates)
            {
                if (pair.Value.Frame <= Time.frameCount)
                    _flushBuffer.Add(pair.Key);
            }

            for (int i = 0; i < _flushBuffer.Count; i++)
            {
                AggregateKey key = _flushBuffer[i];
                AggregateEntry entry;
                if (!_aggregates.TryGetValue(key, out entry)) continue;

                // Aggregation has already happened. Crowding is only a visual post-filter;
                // it does not change the PerTargetTotal grouping rule.
                SpawnNumber(entry.Position, entry.Amount, ResolveAggregateMultiplier(entry), entry.DamageType, entry.AnyCritical,
                    false, Vector3.zero, Vector2.zero, true);
                _aggregates.Remove(key);
            }
        }

        private void SpawnNumber(Vector3 position, float amount, float hitMultiplier, DamageTypes damageType, bool critical, bool useJitter, Vector3 extraWorldJitter, Vector2 extraScreenJitter, bool applyCrowding)
        {
            EnsureFrameCounters();

            Vector3 worldJitter = extraWorldJitter;
            Vector2 screenJitter = extraScreenJitter;
            if (useJitter)
            {
                if (Plugin.RandomJitter.Value > 0f)
                {
                    Vector2 randomWorld = UnityEngine.Random.insideUnitCircle * Plugin.RandomJitter.Value;
                    worldJitter += new Vector3(randomWorld.x, 0f, randomWorld.y);
                }

                if (Plugin.ScreenJitter.Value > 0f)
                    screenJitter += UnityEngine.Random.insideUnitCircle * Plugin.ScreenJitter.Value;
            }

            Vector2 crowdingScreenPosition;
            bool hasCrowdingPosition = false;
            if (applyCrowding && Plugin.EnableCrowdingProtection.Value)
            {
                hasCrowdingPosition = TryGetCrowdingScreenPosition(position, screenJitter, out crowdingScreenPosition);
                if (hasCrowdingPosition && IsAreaCrowded(crowdingScreenPosition))
                {
                    _crowdingSkippedThisFrame++;
                    return;
                }
            }
            else
            {
                crowdingScreenPosition = Vector2.zero;
            }

            if (!TryConsumeSpawnBudget()) return;

            RenderMode renderMode = ResolveActiveRenderMode();

            if (renderMode == RenderMode.Vanilla)
            {
                SpawnVanilla(amount, hitMultiplier, damageType, critical, position + worldJitter);
            }
            else
            {
                string text = FormatDamage(amount);
                Color color = DamageNumberColor.ForMultiplier(hitMultiplier);

                if (renderMode == RenderMode.ScreenSpace || renderMode == RenderMode.Both)
                    SpawnScreenWorld(text, position, screenJitter, color);

                if (renderMode == RenderMode.WorldTextMeshPro || renderMode == RenderMode.Both)
                    SpawnWorld(text, position, worldJitter, color);
            }

            // Include accepted same-frame numbers immediately. Existing active positions are
            // rebuilt only once per frame, keeping this protection cheap under hit spam.
            if (hasCrowdingPosition)
                _crowdingPositions.Add(crowdingScreenPosition);
        }

        private void SpawnScreenWorld(string text, Vector3 worldPosition, Vector2 jitter, Color color)
        {
            ScreenDamageNumberView view = AcquireScreenView();
            if (view == null) return;

            view.PlayWorld(
                text,
                worldPosition,
                jitter,
                Mathf.Max(0.05f, Plugin.Lifetime.Value),
                Mathf.Max(8, Plugin.ScreenFontSize.Value),
                color);
        }

        private void SpawnScreenFixed(string text, Vector2 screenPosition, Color color)
        {
            ScreenDamageNumberView view = AcquireScreenView();
            if (view == null) return;

            view.PlayFixed(
                text,
                screenPosition,
                Mathf.Max(0.05f, Plugin.Lifetime.Value),
                Mathf.Max(8, Plugin.ScreenFontSize.Value),
                color);
        }

        private void SpawnWorld(string text, Vector3 worldPosition, Vector3 jitter, Color color)
        {
            DamageNumberView view = AcquireWorldView();
            if (view == null) return;

            view.Play(
                text,
                worldPosition,
                jitter,
                Mathf.Max(0.05f, Plugin.Lifetime.Value),
                Plugin.FloatSpeed.Value,
                Plugin.WorldScale.Value,
                Plugin.FontSize.Value,
                color);
        }

        private void SpawnVanilla(float amount, float hitMultiplier, DamageTypes damageType, bool critical, Vector3 worldPosition)
        {
            try
            {
                VanillaDamageNumberView view = AcquireVanillaView();
                if (view == null) return;

                Color color = Plugin.VanillaUseDamageTypeColors.Value
                    ? VanillaDamageText.GetDamageTypeColor(damageType)
                    : DamageNumberColor.ForMultiplier(hitMultiplier);

                // The game's damage text prefab renders whole numbers only, so ShowDecimals
                // has no effect in this renderer.
                int rounded = Mathf.Max(1, Mathf.RoundToInt(amount));

                // Matches the game's alternating fly-out direction.
                _vanillaFlyIndex++;
                bool flyRight = (_vanillaFlyIndex % 2) == 0;

                view.Play(rounded, color, worldPosition, Plugin.VanillaScale.Value, flyRight, critical);
            }
            catch (Exception e)
            {
                BreakVanillaBoundary(e);
            }
        }

        /// <summary>
        /// The vanilla renderer needs the game's damage text prefab. While it is still loading, or
        /// if this build does not provide it, damage numbers keep working through ScreenSpace
        /// instead of disappearing.
        /// </summary>
        private RenderMode ResolveActiveRenderMode()
        {
            RenderMode configured = Plugin.Renderer.Value;
            if (configured != RenderMode.Vanilla)
                return configured;

            return _vanillaReady ? RenderMode.Vanilla : RenderMode.ScreenSpace;
        }

        private VanillaDamageNumberView AcquireVanillaView()
        {
            GameObject textPrefab = VanillaDamageText.TextPrefab;
            if (textPrefab == null) return null;

            EnsureVanillaRoot();
            int maxActive = GetMaxActiveVanillaNumbers();

            VanillaDamageNumberView inactive = null;
            VanillaDamageNumberView oldest = null;
            int activeCount = 0;

            for (int i = 0; i < _vanillaPool.Count; i++)
            {
                VanillaDamageNumberView view = _vanillaPool[i];
                if (!view.IsActive)
                {
                    if (inactive == null) inactive = view;
                    continue;
                }

                activeCount++;
                if (oldest == null || view.StartTime < oldest.StartTime)
                    oldest = view;
            }

            if (activeCount < maxActive)
            {
                if (inactive != null) return inactive;

                VanillaDamageNumberView created = VanillaDamageNumberView.TryCreate(_vanillaRoot, textPrefab);
                if (created == null) return null;

                _vanillaPool.Add(created);
                return created;
            }

            if (oldest != null)
            {
                oldest.Stop();
                return oldest;
            }

            return inactive;
        }

        private ScreenDamageNumberView AcquireScreenView()
        {
            EnsureScreenRoot();
            int maxActive = GetMaxActiveNumbers();

            ScreenDamageNumberView inactive = null;
            ScreenDamageNumberView oldest = null;
            int activeCount = 0;

            for (int i = 0; i < _screenPool.Count; i++)
            {
                ScreenDamageNumberView view = _screenPool[i];
                if (!view.IsActive)
                {
                    if (inactive == null) inactive = view;
                    continue;
                }

                activeCount++;
                if (oldest == null || view.StartTime < oldest.StartTime)
                    oldest = view;
            }

            if (activeCount < maxActive)
            {
                if (inactive != null) return inactive;

                ScreenDamageNumberView created = new ScreenDamageNumberView(_screenRoot, _screenSharedMaterial);
                if (_screenSharedMaterial == null)
                {
                    // Cache the game's default TMP material for batching. The visible
                    // border is produced by Unity UI Outline, not a cloned font material.
                    _screenSharedMaterial = created.FontSharedMaterial;
                    created.SetSharedMaterial(_screenSharedMaterial);
                }
                _screenPool.Add(created);
                return created;
            }

            if (oldest != null)
            {
                oldest.Stop();
                return oldest;
            }

            return inactive;
        }

        private DamageNumberView AcquireWorldView()
        {
            EnsureWorldRoot();
            int maxActive = GetMaxActiveNumbers();

            DamageNumberView inactive = null;
            DamageNumberView oldest = null;
            int activeCount = 0;

            for (int i = 0; i < _worldPool.Count; i++)
            {
                DamageNumberView view = _worldPool[i];
                if (!view.IsActive)
                {
                    if (inactive == null) inactive = view;
                    continue;
                }

                activeCount++;
                if (oldest == null || view.StartTime < oldest.StartTime)
                    oldest = view;
            }

            if (activeCount < maxActive)
            {
                if (inactive != null) return inactive;

                DamageNumberView created = new DamageNumberView(_worldRoot, _worldSharedMaterial);
                if (_worldSharedMaterial == null)
                {
                    // Both the black backing text and the colored face share the
                    // game's original font material. No per-number material instances.
                    _worldSharedMaterial = created.FontSharedMaterial;
                    created.SetSharedMaterial(_worldSharedMaterial);
                }
                _worldPool.Add(created);
                return created;
            }

            if (oldest != null)
            {
                oldest.Stop();
                return oldest;
            }

            return inactive;
        }

        private Camera GetMainCamera()
        {
            if (_cachedCamera != null && _cachedCamera.isActiveAndEnabled)
                return _cachedCamera;

            float now = Time.unscaledTime;
            if (now < _nextCameraLookupTime)
                return null;

            _nextCameraLookupTime = now + 0.5f;
            _cachedCamera = Camera.main;
            return _cachedCamera;
        }

        private bool TryGetCrowdingScreenPosition(Vector3 worldPosition, Vector2 screenJitter, out Vector2 screenPosition)
        {
            Camera camera = GetMainCamera();
            PrepareCrowdingPositions(camera);

            if (camera == null)
            {
                screenPosition = Vector2.zero;
                return false;
            }

            Vector3 projected = camera.WorldToScreenPoint(worldPosition);
            if (projected.z <= 0f)
            {
                screenPosition = Vector2.zero;
                return false;
            }

            screenPosition = new Vector2(projected.x, projected.y) + screenJitter;
            return true;
        }

        private void PrepareCrowdingPositions(Camera camera)
        {
            int frame = Time.frameCount;
            if (_crowdingFrame == frame) return;

            _crowdingFrame = frame;
            _crowdingPositions.Clear();
            if (camera == null) return;

            RenderMode renderMode = ResolveActiveRenderMode();

            if (renderMode == RenderMode.Vanilla)
            {
                try
                {
                    for (int i = 0; i < _vanillaPool.Count; i++)
                    {
                        VanillaDamageNumberView view = _vanillaPool[i];
                        if (!view.IsActive) continue;

                        Vector3 vanillaProjected = camera.WorldToScreenPoint(view.CurrentWorldPosition);
                        if (vanillaProjected.z > 0f)
                            _crowdingPositions.Add(new Vector2(vanillaProjected.x, vanillaProjected.y));
                    }
                }
                catch (Exception e)
                {
                    BreakVanillaBoundary(e);
                }
                return;
            }

            bool useScreenViews = renderMode == RenderMode.ScreenSpace || renderMode == RenderMode.Both;

            if (useScreenViews)
            {
                for (int i = 0; i < _screenPool.Count; i++)
                {
                    ScreenDamageNumberView view = _screenPool[i];
                    if (view.IsActive && view.IsVisible)
                        _crowdingPositions.Add(view.CurrentScreenPosition);
                }
                return;
            }

            for (int i = 0; i < _worldPool.Count; i++)
            {
                DamageNumberView view = _worldPool[i];
                if (!view.IsActive) continue;

                Vector3 projected = camera.WorldToScreenPoint(view.CurrentWorldPosition);
                if (projected.z > 0f)
                    _crowdingPositions.Add(new Vector2(projected.x, projected.y));
            }
        }

        private bool IsAreaCrowded(Vector2 screenPosition)
        {
            float radius = Mathf.Clamp(Plugin.CrowdingRadius.Value, 8f, 120f);
            float radiusSquared = radius * radius;
            int maxNearby = Mathf.Clamp(Plugin.MaxNearbyNumbers.Value, 1, 32);
            int nearby = 0;

            for (int i = 0; i < _crowdingPositions.Count; i++)
            {
                Vector2 delta = _crowdingPositions[i] - screenPosition;
                if (delta.sqrMagnitude > radiusSquared) continue;

                nearby++;
                if (nearby >= maxNearby)
                    return true;
            }

            return false;
        }

        private void EnsureScreenRoot()
        {
            if (_screenRoot != null) return;

            GameObject canvasObject = new GameObject(
                "SULFUR Damage Numbers Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32760;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            _screenRoot = canvasObject.GetComponent<RectTransform>();
        }

        private void EnsureWorldRoot()
        {
            if (_worldRoot != null) return;

            GameObject rootObject = new GameObject("SULFUR Damage Numbers World Pool");
            rootObject.transform.SetParent(transform, false);
            _worldRoot = rootObject.transform;
        }

        private void EnsureVanillaRoot()
        {
            if (_vanillaRoot != null) return;

            GameObject rootObject = new GameObject("SULFUR Damage Numbers Vanilla Pool");
            rootObject.transform.SetParent(transform, false);
            _vanillaRoot = rootObject.transform;
        }

        private void ApplyRendererSelectionIfChanged()
        {
            RenderMode renderMode = ResolveActiveRenderMode();
            if (_hasAppliedRenderMode && renderMode == _lastAppliedRenderMode)
                return;

            _lastAppliedRenderMode = renderMode;
            _hasAppliedRenderMode = true;

            bool screenEnabled = renderMode == RenderMode.ScreenSpace || renderMode == RenderMode.Both;
            bool worldEnabled = renderMode == RenderMode.WorldTextMeshPro || renderMode == RenderMode.Both;
            bool vanillaEnabled = renderMode == RenderMode.Vanilla;

            // Only scan and stop a pool when the renderer selection actually changes.
            // SpawnNumber already respects the current mode, so disabled pools cannot
            // receive new active views afterward.
            if (!screenEnabled)
            {
                for (int i = 0; i < _screenPool.Count; i++)
                    _screenPool[i].Stop();
            }

            if (!worldEnabled)
            {
                for (int i = 0; i < _worldPool.Count; i++)
                    _worldPool[i].Stop();
            }

            if (!vanillaEnabled && _vanillaPool.Count > 0)
            {
                try
                {
                    for (int i = 0; i < _vanillaPool.Count; i++)
                        _vanillaPool[i].Stop();
                }
                catch (Exception e)
                {
                    BreakVanillaBoundary(e);
                }
            }
        }

        private static void EnforcePoolLimit<T>(List<T> pool, int maxActive) where T : class, IDamageNumberView
        {
            int activeCount = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i].IsActive)
                    activeCount++;
            }

            while (activeCount > maxActive)
            {
                T oldest = null;
                for (int i = 0; i < pool.Count; i++)
                {
                    T candidate = pool[i];
                    if (!candidate.IsActive) continue;
                    if (oldest == null || candidate.StartTime < oldest.StartTime)
                        oldest = candidate;
                }

                if (oldest == null) break;
                oldest.Stop();
                activeCount--;
            }
        }

        private void EnsureFrameCounters()
        {
            int frame = Time.frameCount;
            if (_spawnBudgetFrame == frame) return;

            _spawnBudgetFrame = frame;
            _spawnedThisFrame = 0;
            _droppedThisFrame = 0;
            _crowdingSkippedThisFrame = 0;
        }

        private bool TryConsumeSpawnBudget()
        {
            EnsureFrameCounters();

            int maxPerFrame = Mathf.Clamp(Plugin.MaxNumbersPerFrame.Value, 1, HardMaxNumbersPerFrame);
            if (_spawnedThisFrame >= maxPerFrame)
            {
                _droppedThisFrame++;
                return false;
            }

            _spawnedThisFrame++;
            return true;
        }

        private void MaybeLogDroppedBudget()
        {
            if (_spawnBudgetFrame != Time.frameCount) return;
            if (!Plugin.DebugLog.Value || (_droppedThisFrame <= 0 && _crowdingSkippedThisFrame <= 0)) return;

            float now = Time.unscaledTime;
            if (now - _lastBudgetLogTime < 1f) return;

            _lastBudgetLogTime = now;
            Plugin.Log.LogWarning(
                "[DamageNumbers] frame " + _spawnBudgetFrame +
                ": droppedByFrameBudget=" + _droppedThisFrame +
                ", skippedByCrowding=" + _crowdingSkippedThisFrame + ".");
        }

        private static int GetMaxActiveNumbers()
        {
            return Mathf.Clamp(Plugin.MaxActiveNumbers.Value, 1, HardMaxActiveNumbers);
        }

        private static int GetMaxActiveVanillaNumbers()
        {
            return Mathf.Clamp(Plugin.MaxActiveNumbers.Value, 1, HardMaxActiveVanillaNumbers);
        }

        private static Vector3 GetDamagePosition(Unit target)
        {
            Npc npc = target as Npc;
            if (npc != null && npc.overhead != null)
                return npc.overhead.position;

            return target.transform.position + Vector3.up * 2f;
        }

        private PerHitSpreadOffset GetNextPerHitSpread(Unit target, DamageSourceData sourceData)
        {
            if (_perHitSpreadFrame != Time.frameCount)
            {
                _perHitSpreadFrame = Time.frameCount;
                _perHitSpreadCounters.Clear();
            }

            PerHitSpreadKey key = PerHitSpreadKey.Create(target, sourceData);
            int index;
            if (!_perHitSpreadCounters.TryGetValue(key, out index))
                index = 0;

            _perHitSpreadCounters[key] = index + 1;
            if (index <= 0)
                return PerHitSpreadOffset.Zero;

            const float goldenAngle = 2.39996323f;
            float angle = index * goldenAngle;
            float distanceFactor = Mathf.Sqrt(index);
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            float screenDistance = Mathf.Min(42f, Mathf.Max(0f, Plugin.PerHitStackScreenSpread.Value) * distanceFactor);
            float worldDistance = Mathf.Min(0.36f, Mathf.Max(0f, Plugin.PerHitStackWorldSpread.Value) * distanceFactor);

            return new PerHitSpreadOffset
            {
                ScreenOffset = direction * screenDistance,
                WorldOffset = new Vector3(direction.x * worldDistance, 0f, direction.y * worldDistance)
            };
        }

        private static string FormatDamage(float amount)
        {
            if (Plugin.ShowDecimals.Value)
            {
                float oneDecimal = Mathf.Round(amount * 10f) / 10f;
                float integer = Mathf.Round(oneDecimal);
                if (Mathf.Abs(oneDecimal - integer) <= 0.0001f)
                {
                    int integerValue = Mathf.RoundToInt(integer);
                    if (integerValue <= 0 && amount > 0f) integerValue = 1;
                    return integerValue.ToString(CultureInfo.InvariantCulture);
                }

                return oneDecimal.ToString("0.0", CultureInfo.InvariantCulture);
            }

            int rounded = Mathf.RoundToInt(amount);
            if (rounded <= 0) rounded = 1;
            return rounded.ToString(CultureInfo.InvariantCulture);
        }

        private static float ResolveAggregateMultiplier(AggregateEntry entry)
        {
            switch (Plugin.AggregateColorMode.Value)
            {
                case MergedColorMode.LastHit:
                    return entry.LastMultiplier;
                case MergedColorMode.DamageWeightedAverage:
                    if (entry.Amount > 0.0001f)
                        return entry.WeightedMultiplierSum / entry.Amount;
                    return entry.HighestMultiplier;
                case MergedColorMode.HighestMultiplier:
                default:
                    return entry.HighestMultiplier;
            }
        }

        private void OnDestroy()
        {
            _aggregates.Clear();
            _flushBuffer.Clear();
            _perHitSpreadCounters.Clear();
            _crowdingPositions.Clear();
            _screenPool.Clear();
            _worldPool.Clear();
            _vanillaPool.Clear();
            _screenRoot = null;
            _worldRoot = null;
            _vanillaRoot = null;
            _cachedCamera = null;

            // These references point to the game's default TMP font materials.
            // They are not owned by this mod and must not be destroyed.
            _screenSharedMaterial = null;
            _worldSharedMaterial = null;

            if (_instance == this)
                _instance = null;
        }

        private struct AggregateEntry
        {
            public float Amount;
            public Vector3 Position;
            public int Frame;
            public float HighestMultiplier;
            public float LastMultiplier;
            public float WeightedMultiplierSum;
            public DamageTypes DamageType;
            public bool AnyCritical;
        }

        private struct PerHitSpreadOffset
        {
            public static readonly PerHitSpreadOffset Zero = new PerHitSpreadOffset
            {
                WorldOffset = Vector3.zero,
                ScreenOffset = Vector2.zero
            };

            public Vector3 WorldOffset;
            public Vector2 ScreenOffset;
        }
    }

    internal interface IDamageNumberView
    {
        bool IsActive { get; }
        float StartTime { get; }
        void Stop();
    }

    internal sealed class ScreenDamageNumberView : IDamageNumberView
    {
        // Four same-size diagonal copies create an even black border without
        // scaling the glyphs or changing character spacing.
        private static readonly Vector2[] OutlineOffsets =
        {
            new Vector2(-1.15f, -1.15f),
            new Vector2(-1.15f,  1.15f),
            new Vector2( 1.15f, -1.15f),
            new Vector2( 1.15f,  1.15f)
        };

        private readonly GameObject _gameObject;
        private readonly RectTransform _rectTransform;
        private readonly TextMeshProUGUI[] _outlineTexts;
        private readonly TextMeshProUGUI _text;

        private Vector3 _worldPosition;
        private Vector2 _screenPosition;
        private Vector2 _screenJitter;
        private float _lifetime;
        private bool _fixedScreen;

        public bool IsActive { get; private set; }
        public float StartTime { get; private set; }
        public bool IsVisible { get; private set; }
        public Vector2 CurrentScreenPosition { get; private set; }
        public Material FontSharedMaterial { get { return _text.fontSharedMaterial; } }

        public ScreenDamageNumberView(RectTransform parent, Material sharedMaterial)
        {
            _gameObject = new GameObject(
                "SULFUR Damage Number Screen",
                typeof(RectTransform));
            _gameObject.transform.SetParent(parent, false);

            _rectTransform = _gameObject.GetComponent<RectTransform>();
            _rectTransform.anchorMin = Vector2.zero;
            _rectTransform.anchorMax = Vector2.zero;
            _rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _rectTransform.sizeDelta = new Vector2(240f, 80f);

            // The game's runtime TMP material does not reliably display SDF outlines.
            // Do not enlarge a black duplicate: that changes glyph size and spacing.
            // Instead, draw four same-size pooled copies at tiny diagonal offsets,
            // then draw the colored face once on top.
            _outlineTexts = new TextMeshProUGUI[OutlineOffsets.Length];
            for (int i = 0; i < _outlineTexts.Length; i++)
                _outlineTexts[i] = CreateTextLayer("Outline" + i, OutlineOffsets[i]);

            _text = CreateTextLayer("Face", Vector2.zero);

            if (sharedMaterial != null)
                SetSharedMaterial(sharedMaterial);
            else if (_text.fontSharedMaterial != null)
                SetOutlineSharedMaterial(_text.fontSharedMaterial);

            Stop();
        }

        private TextMeshProUGUI CreateTextLayer(string name, Vector2 offset)
        {
            GameObject layer = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            layer.transform.SetParent(_rectTransform, false);

            RectTransform rect = layer.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;

            TextMeshProUGUI text = layer.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public void SetSharedMaterial(Material sharedMaterial)
        {
            if (sharedMaterial == null)
                return;

            _text.fontSharedMaterial = sharedMaterial;
            SetOutlineSharedMaterial(sharedMaterial);
        }

        private void SetOutlineSharedMaterial(Material sharedMaterial)
        {
            for (int i = 0; i < _outlineTexts.Length; i++)
                _outlineTexts[i].fontSharedMaterial = sharedMaterial;
        }

        public void PlayWorld(string value, Vector3 worldPosition, Vector2 jitter, float lifetime, int fontSize, Color color)
        {
            _worldPosition = worldPosition;
            _screenJitter = jitter;
            _fixedScreen = false;
            PlayCommon(value, lifetime, fontSize, color);
        }

        public void PlayFixed(string value, Vector2 screenPosition, float lifetime, int fontSize, Color color)
        {
            _screenPosition = screenPosition;
            _screenJitter = Vector2.zero;
            _fixedScreen = true;
            PlayCommon(value, lifetime, fontSize, color);
        }

        private void PlayCommon(string value, float lifetime, int fontSize, Color color)
        {
            StartTime = Time.time;
            _lifetime = Mathf.Max(0.05f, lifetime);

            color.a = 1f;
            _text.text = value;
            _text.fontSize = fontSize;
            _text.color = color;
            _text.canvasRenderer.SetAlpha(1f);

            for (int i = 0; i < _outlineTexts.Length; i++)
            {
                TextMeshProUGUI outline = _outlineTexts[i];
                outline.text = value;
                outline.fontSize = fontSize;
                outline.color = Color.black;
                outline.canvasRenderer.SetAlpha(1f);
                outline.enabled = true;
            }

            _text.enabled = true;
            _gameObject.SetActive(true);
            IsVisible = true;
            IsActive = true;
        }

        public void Tick(Camera camera, float now, float floatSpeed)
        {
            if (!IsActive) return;

            float age = now - StartTime;
            if (age >= _lifetime)
            {
                Stop();
                return;
            }

            Vector2 screenPosition;
            if (_fixedScreen)
            {
                screenPosition = _screenPosition;
            }
            else
            {
                if (camera == null)
                {
                    SetLayersEnabled(false);
                    IsVisible = false;
                    return;
                }

                Vector3 world = _worldPosition + Vector3.up * (age * 0.25f);
                Vector3 projected = camera.WorldToScreenPoint(world);
                if (projected.z <= 0f)
                {
                    SetLayersEnabled(false);
                    IsVisible = false;
                    return;
                }

                screenPosition = new Vector2(projected.x, projected.y);
            }

            SetLayersEnabled(true);
            IsVisible = true;
            screenPosition += _screenJitter;
            screenPosition.y += age * floatSpeed;
            CurrentScreenPosition = screenPosition;
            _rectTransform.anchoredPosition = screenPosition;

            // CanvasRenderer alpha fades the already-generated meshes without changing
            // TMP vertex colors every frame.
            float alpha = Mathf.Clamp01(1f - age / _lifetime);
            _text.canvasRenderer.SetAlpha(alpha);
            for (int i = 0; i < _outlineTexts.Length; i++)
                _outlineTexts[i].canvasRenderer.SetAlpha(alpha);
        }

        private void SetLayersEnabled(bool enabled)
        {
            _text.enabled = enabled;
            for (int i = 0; i < _outlineTexts.Length; i++)
                _outlineTexts[i].enabled = enabled;
        }

        public void Stop()
        {
            IsActive = false;
            IsVisible = false;
            if (_text != null)
            {
                _text.canvasRenderer.SetAlpha(0f);
                _text.enabled = false;
            }

            if (_outlineTexts != null)
            {
                for (int i = 0; i < _outlineTexts.Length; i++)
                {
                    TextMeshProUGUI outline = _outlineTexts[i];
                    if (outline == null)
                        continue;

                    outline.canvasRenderer.SetAlpha(0f);
                    outline.enabled = false;
                }
            }

            if (_gameObject != null)
                _gameObject.SetActive(false);
        }
    }

    internal struct PerHitSpreadKey : IEquatable<PerHitSpreadKey>
    {
        private int _targetId;
        private int _frame;
        private int _sourceId;

        public static PerHitSpreadKey Create(Unit target, DamageSourceData sourceData)
        {
            int sourceId = 0;
            if (sourceData.sourceWeapon != null)
                sourceId = sourceData.sourceWeapon.GetInstanceID();
            else if (sourceData.instanceId != 0)
                sourceId = sourceData.instanceId;
            else if (sourceData.sourceUnit != null)
                sourceId = sourceData.sourceUnit.GetInstanceID();

            return new PerHitSpreadKey
            {
                _targetId = target.GetInstanceID(),
                _frame = Time.frameCount,
                _sourceId = sourceId
            };
        }

        public bool Equals(PerHitSpreadKey other)
        {
            return _targetId == other._targetId &&
                   _frame == other._frame &&
                   _sourceId == other._sourceId;
        }

        public override bool Equals(object obj)
        {
            return obj is PerHitSpreadKey && Equals((PerHitSpreadKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _targetId;
                hash = (hash * 397) ^ _frame;
                hash = (hash * 397) ^ _sourceId;
                return hash;
            }
        }
    }

    internal struct AggregateKey : IEquatable<AggregateKey>
    {
        private int _targetId;
        private int _frame;
        private int _sourceId;
        private int _damageType;
        private bool _melee;

        public static AggregateKey Create(Unit target, DamageSourceData sourceData)
        {
            int sourceId = 0;
            if (sourceData.sourceWeapon != null)
                sourceId = sourceData.sourceWeapon.GetInstanceID();
            else if (sourceData.instanceId != 0)
                sourceId = sourceData.instanceId;
            else if (sourceData.sourceUnit != null)
                sourceId = sourceData.sourceUnit.GetInstanceID();

            return new AggregateKey
            {
                _targetId = target.GetInstanceID(),
                _frame = Time.frameCount,
                _sourceId = sourceId,
                _damageType = Convert.ToInt32(sourceData.damageType),
                _melee = sourceData.melee
            };
        }

        public bool Equals(AggregateKey other)
        {
            return _targetId == other._targetId &&
                   _frame == other._frame &&
                   _sourceId == other._sourceId &&
                   _damageType == other._damageType &&
                   _melee == other._melee;
        }

        public override bool Equals(object obj)
        {
            return obj is AggregateKey && Equals((AggregateKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _targetId;
                hash = (hash * 397) ^ _frame;
                hash = (hash * 397) ^ _sourceId;
                hash = (hash * 397) ^ _damageType;
                hash = (hash * 397) ^ (_melee ? 1 : 0);
                return hash;
            }
        }
    }

    internal static class DamageNumberColor
    {
        private static readonly Color NormalDamageColor = Color.white;
        private static readonly Color OrangeDamageColor = new Color(1f, 0.5f, 0f, 1f);

        public static Color ForMultiplier(float multiplier)
        {
            if (!Plugin.UseWeakspotColors.Value)
                return NormalDamageColor;

            if (multiplier <= 0f)
                return NormalDamageColor;

            if (Plugin.TreatOneMultiplierAsNormal.Value && Mathf.Abs(multiplier - 1f) <= 0.0001f)
                return NormalDamageColor;

            if (multiplier >= 1.5f)
                return Color.red;

            if (multiplier >= 1f)
                return OrangeDamageColor;

            if (multiplier >= 0.75f)
                return Color.yellow;

            return NormalDamageColor;
        }
    }
}
