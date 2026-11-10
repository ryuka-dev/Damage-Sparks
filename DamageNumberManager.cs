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

        private static readonly Comparison<AggregateEntry> AggregateBySequence =
            (a, b) => a.FirstSequence.CompareTo(b.FirstSequence);

        private static readonly Comparison<PerHitEntry> PerHitBySequence =
            (a, b) => a.Sequence.CompareTo(b.Sequence);

        // Hits are queued when the game confirms them and shown in LateUpdate, in the order the
        // game dealt them (DamageLogPatch numbers them). A nested hit such as Crusader's Holy
        // damage is confirmed before the weapon hit that caused it, so arrival order is not
        // dealing order.
        private readonly Dictionary<AggregateKey, AggregateEntry> _aggregates = new Dictionary<AggregateKey, AggregateEntry>();
        private readonly List<AggregateEntry> _flushEntries = new List<AggregateEntry>();
        private readonly List<PerHitEntry> _perHitQueue = new List<PerHitEntry>();
        private readonly Dictionary<PerHitSpreadKey, int> _perHitSpreadCounters = new Dictionary<PerHitSpreadKey, int>();

        // PerTargetTotal keeps different damage types on the same target apart (an enchantment
        // such as Crusader adds a Holy hit in the same frame as the weapon hit). All of them start
        // at the target's overhead point, so within one flush every number actually shown on a
        // target takes the next row, and every row after the first is drawn one text line higher.
        private readonly Dictionary<int, int> _shownRowsPerTarget = new Dictionary<int, int>();

        // One text line, measured from the first view each renderer creates: screen pixels per
        // font point, world-text local units per font point, vanilla world units at scale 1.
        private float _screenLinePerPoint;
        private float _worldLinePerPoint;
        private float _vanillaLineAtUnitScale;

        private Canvas _screenCanvas;
        private bool _clearedForLoading;
        private readonly List<ScreenDamageNumberView> _screenPool = new List<ScreenDamageNumberView>();
        private readonly List<DamageNumberView> _worldPool = new List<DamageNumberView>();
        private readonly List<VanillaDamageNumberView> _vanillaPool = new List<VanillaDamageNumberView>();
        private readonly List<Vector2> _crowdingPositions = new List<Vector2>(HardMaxActiveNumbers);

        private bool _vanillaReady;
        private bool _vanillaBoundaryBroken;
        private int _vanillaFlyIndex;

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

        /// <param name="sequence">Order in which the game dealt the hit (DamageLogPatch numbers them).</param>
        /// <param name="bonus">A bonus multiplier the amount already includes, shown beside it; 0 for none.</param>
        public static void RegisterDamage(Unit target, DamageSourceData sourceData, float amount, float hitMultiplier, float bonus, int sequence)
        {
            if (target == null) return;

            EnsureExists();
            if (_instance != null)
                _instance.RegisterDamageInternal(target, sourceData, amount, hitMultiplier, bonus, sequence);
        }

        /// <param name="target">The unit that was hit, or null when it no longer exists locally.</param>
        /// <param name="fallbackPosition">Places the number when there is no unit to place it above.</param>
        /// <param name="sourceId">Identity of the attack source, as in <see cref="DamageHit.SourceIdOf"/>; 0 for none.</param>
        /// <param name="bonus">A bonus multiplier the amount already includes, shown beside it; 0 for none.</param>
        /// <param name="dealtFrame">The frame the hit was dealt in, which same-frame merging goes by.</param>
        public static void RegisterResolvedDamage(Unit target, Vector3? fallbackPosition, int sourceId, bool melee, DamageTypes damageType, bool critical, float amount, float hitMultiplier, float bonus, int dealtFrame, int sequence)
        {
            if (target == null && !fallbackPosition.HasValue) return;

            EnsureExists();
            if (_instance == null) return;

            _instance.RegisterHit(new DamageHit
            {
                // No unit left to identify: the hits dealt in one frame still count as one target, so a
                // shotgun blast on a unit that has since been destroyed stays one number.
                TargetId = target != null ? target.GetInstanceID() : unchecked(int.MinValue + dealtFrame),
                Frame = dealtFrame,
                Position = target != null ? GetDamagePosition(target) : fallbackPosition.Value,
                SourceId = sourceId,
                Melee = melee,
                DamageType = damageType,
                Critical = critical,
                Amount = amount,
                Multiplier = hitMultiplier,
                Bonus = bonus,
                Sequence = sequence
            });
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

        private void RegisterDamageInternal(Unit target, DamageSourceData sourceData, float amount, float hitMultiplier, float bonus, int sequence)
        {
            RegisterHit(new DamageHit
            {
                TargetId = target.GetInstanceID(),
                Frame = Time.frameCount,
                Position = GetDamagePosition(target),
                SourceId = DamageHit.SourceIdOf(sourceData),
                Melee = sourceData.melee,
                DamageType = sourceData.damageType,
                Critical = sourceData.isCritical,
                Amount = amount,
                Multiplier = hitMultiplier,
                Bonus = bonus,
                Sequence = sequence
            });
        }

        private void RegisterHit(DamageHit hit)
        {
            if (Plugin.Mode.Value == DisplayMode.PerHit)
            {
                _perHitQueue.Add(new PerHitEntry
                {
                    Position = hit.Position,
                    Amount = hit.Amount,
                    Multiplier = hit.Multiplier,
                    Bonus = hit.Bonus,
                    DamageType = hit.DamageType,
                    Critical = hit.Critical,
                    SpreadKey = PerHitSpreadKey.Create(hit),
                    Sequence = hit.Sequence
                });
                return;
            }

            AggregateKey key = AggregateKey.Create(hit);
            AggregateEntry entry;
            if (!_aggregates.TryGetValue(key, out entry))
            {
                entry = new AggregateEntry
                {
                    Amount = 0f,
                    Position = hit.Position,
                    HighestMultiplier = 0f,
                    LastMultiplier = 0f,
                    WeightedMultiplierSum = 0f,
                    HighestBonus = 0f,
                    // AggregateKey already separates damage types, so every hit merged into one
                    // entry shares this value.
                    DamageType = hit.DamageType,
                    AnyCritical = false,
                    TargetId = hit.TargetId,
                    FirstSequence = hit.Sequence
                };
            }

            entry.Amount += hit.Amount;
            entry.Position = hit.Position;
            entry.LastMultiplier = hit.Multiplier;
            entry.AnyCritical = entry.AnyCritical || hit.Critical;
            entry.WeightedMultiplierSum += Mathf.Max(0f, hit.Multiplier) * Mathf.Max(0f, hit.Amount);
            if (hit.Multiplier > entry.HighestMultiplier)
                entry.HighestMultiplier = hit.Multiplier;
            // One number carries one bonus label. Hits merged into it can differ when a bonus starts or
            // ends between the pellets of one shot; the label then names the highest one, the same choice
            // MergedColorMode.HighestMultiplier makes for the color.
            if (hit.Bonus > entry.HighestBonus)
                entry.HighestBonus = hit.Bonus;
            if (hit.Sequence < entry.FirstSequence)
                entry.FirstSequence = hit.Sequence;

            _aggregates[key] = entry;
        }

        private void LateUpdate()
        {
            // Shutdown destroys this object at the end of the frame. Until then it must not show
            // anything or report to an integration that it still can.
            if (_instance != this) return;

            UpdateVanillaAvailability();

            // The game's damage text prefab takes a whole number and nothing else, so the vanilla
            // renderer has nowhere to put a bonus multiplier. This follows the renderer actually in
            // use: while Vanilla is selected but falls back to ScreenSpace, the bonus is drawn.
            DamageNumbersIntegration.SetShowsHitBonus(
                Plugin.Enabled.Value && ResolveActiveRenderMode() != RenderMode.Vanilla);

            GameplayPhase phase = GameStateProbe.Read();
            if (phase == GameplayPhase.Loading)
            {
                // A level is being replaced. Numbers belong to the level they were dealt in, so
                // nothing in flight or still queued survives into the next one. The views are
                // stopped once; the queues are emptied on every loading frame, because a unit can
                // still be damaged while the old level is torn down.
                if (!_clearedForLoading)
                {
                    _clearedForLoading = true;
                    ClearAllNumbers();
                }
                else
                {
                    _aggregates.Clear();
                    _perHitQueue.Clear();
                }
                return;
            }

            _clearedForLoading = false;

            // The screen canvas draws above every game UI. Show it only while the player is in
            // control. Numbers age on scaled time: the pause menu, the inventory and dialogues set
            // GameState.Paused, which stops time, so those numbers reappear with their remaining
            // lifetime. Cinematics leave time running, so numbers hidden by one usually expire.
            // World and vanilla numbers are scene objects behind the game's UI and are not hidden.
            SetScreenCanvasVisible(phase == GameplayPhase.Running);

            FlushPending();
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

        /// <summary>
        /// Shows everything queued since the last flush, in the order the game dealt it.
        /// Aggregation has already happened when this runs. Crowding and the frame budget are only
        /// visual post-filters; they do not change the PerTargetTotal grouping rule.
        /// </summary>
        private void FlushPending()
        {
            if (_perHitQueue.Count == 0 && _aggregates.Count == 0) return;

            _shownRowsPerTarget.Clear();

            // Both queues are drained whatever the current mode is, so a mode change between a hit
            // and its flush neither loses nor delays that hit.
            if (_perHitQueue.Count > 0)
            {
                _perHitQueue.Sort(PerHitBySequence);
                _perHitSpreadCounters.Clear();

                for (int i = 0; i < _perHitQueue.Count; i++)
                {
                    PerHitEntry hit = _perHitQueue[i];
                    PerHitSpreadOffset spread = GetNextPerHitSpread(hit.SpreadKey);
                    SpawnNumber(hit.Position, hit.Amount, hit.Multiplier, hit.Bonus, hit.DamageType, hit.Critical,
                        true, spread.WorldOffset, spread.ScreenOffset, 0, true);
                }

                _perHitQueue.Clear();
            }

            if (_aggregates.Count > 0)
            {
                _flushEntries.Clear();
                foreach (KeyValuePair<AggregateKey, AggregateEntry> pair in _aggregates)
                    _flushEntries.Add(pair.Value);
                _aggregates.Clear();

                _flushEntries.Sort(AggregateBySequence);

                for (int i = 0; i < _flushEntries.Count; i++)
                {
                    AggregateEntry entry = _flushEntries[i];

                    // Rows count only the numbers actually shown, so a number dropped by crowding
                    // or the frame budget never leaves a gap under the ones above it.
                    int row;
                    _shownRowsPerTarget.TryGetValue(entry.TargetId, out row);

                    if (SpawnNumber(entry.Position, entry.Amount, ResolveAggregateMultiplier(entry), entry.HighestBonus, entry.DamageType, entry.AnyCritical,
                            false, Vector3.zero, Vector2.zero, row, true))
                    {
                        _shownRowsPerTarget[entry.TargetId] = row + 1;
                    }
                }

                _flushEntries.Clear();
            }
        }

        /// <param name="stackRow">
        /// 0 for the normal position. A higher row is drawn that many text lines above it, in the
        /// line height of the renderer that draws it.
        /// </param>
        /// <param name="bonus">A bonus multiplier to draw beside the number; 0 for none. The vanilla renderer cannot draw it.</param>
        /// <returns>True when the number was shown, false when crowding or the frame budget dropped it.</returns>
        private bool SpawnNumber(Vector3 position, float amount, float hitMultiplier, float bonus, DamageTypes damageType, bool critical, bool useJitter, Vector3 extraWorldJitter, Vector2 extraScreenJitter, int stackRow, bool applyCrowding)
        {
            EnsureFrameCounters();

            RenderMode renderMode = ResolveActiveRenderMode();
            bool drawsScreen = renderMode == RenderMode.ScreenSpace || renderMode == RenderMode.Both;
            bool drawsWorld = renderMode == RenderMode.WorldTextMeshPro || renderMode == RenderMode.Both;
            bool drawsVanilla = renderMode == RenderMode.Vanilla;

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

            Vector3 worldRowOffset = Vector3.zero;
            Vector3 vanillaRowOffset = Vector3.zero;
            if (stackRow > 0)
            {
                if (drawsScreen)
                    screenJitter.y += stackRow * GetScreenLineHeight();
                if (drawsWorld)
                    worldRowOffset = Vector3.up * (stackRow * GetWorldLineHeight());
                if (drawsVanilla)
                    vanillaRowOffset = Vector3.up * (stackRow * GetVanillaLineHeight());
            }

            Vector2 crowdingScreenPosition;
            bool hasCrowdingPosition = false;
            if (applyCrowding && Plugin.EnableCrowdingProtection.Value)
            {
                // Test the spot the number will actually occupy. The screen renderer draws at the
                // projected point plus its screen offset; the world and vanilla renderers draw at
                // the offset world point. With Both, crowding follows the screen views.
                hasCrowdingPosition = drawsScreen
                    ? TryGetCrowdingScreenPosition(position, screenJitter, out crowdingScreenPosition)
                    : TryGetCrowdingScreenPosition(position + worldJitter + (drawsVanilla ? vanillaRowOffset : worldRowOffset), Vector2.zero, out crowdingScreenPosition);
                if (hasCrowdingPosition && IsAreaCrowded(crowdingScreenPosition))
                {
                    _crowdingSkippedThisFrame++;
                    return false;
                }
            }
            else
            {
                crowdingScreenPosition = Vector2.zero;
            }

            // Reading the vanilla views for crowding can disable that renderer. A number already
            // laid out for it is dropped rather than drawn by a renderer that just failed.
            if (drawsVanilla && !_vanillaReady) return false;

            if (!TryConsumeSpawnBudget()) return false;

            bool drawn;
            if (drawsVanilla)
            {
                drawn = SpawnVanilla(amount, hitMultiplier, damageType, critical, position + worldJitter + vanillaRowOffset);
            }
            else
            {
                string text = DamageNumberText.WithBonus(FormatDamage(amount), bonus);
                Color color = DamageNumberColor.ForMultiplier(hitMultiplier);

                drawn = false;
                if (drawsScreen)
                    drawn |= SpawnScreenWorld(text, position, screenJitter, color);

                if (drawsWorld)
                    drawn |= SpawnWorld(text, position, worldJitter + worldRowOffset, color);
            }

            // Include accepted same-frame numbers immediately. Existing active positions are
            // rebuilt only once per frame, keeping this protection cheap under hit spam.
            if (drawn && hasCrowdingPosition)
                _crowdingPositions.Add(crowdingScreenPosition);

            return drawn;
        }

        private float GetScreenLineHeight()
        {
            if (_screenLinePerPoint <= 0f)
            {
                // Measured from the first screen view. A view is created on demand so the first
                // stacked number of a session already has the right height.
                ScreenDamageNumberView probe = _screenPool.Count > 0 ? _screenPool[0] : CreateScreenView();
                if (probe != null)
                    _screenLinePerPoint = probe.LineHeightPerPoint;
            }

            return _screenLinePerPoint * Mathf.Max(8, Plugin.ScreenFontSize.Value);
        }

        private float GetWorldLineHeight()
        {
            if (_worldLinePerPoint <= 0f)
            {
                DamageNumberView probe = _worldPool.Count > 0 ? _worldPool[0] : CreateWorldView();
                if (probe != null)
                    _worldLinePerPoint = probe.LineHeightPerPoint;
            }

            // The world views sit under a root with scale 1 and use WorldScale as their own scale.
            return _worldLinePerPoint * Plugin.FontSize.Value * Plugin.WorldScale.Value;
        }

        private float GetVanillaLineHeight()
        {
            if (_vanillaLineAtUnitScale <= 0f && _vanillaPool.Count > 0)
            {
                try
                {
                    _vanillaLineAtUnitScale = _vanillaPool[0].LineHeightAtUnitScale;
                }
                catch (Exception e)
                {
                    BreakVanillaBoundary(e);
                }
            }

            // 0 when no clone could be measured: stacked vanilla numbers then share the base
            // point and only the game's alternating fly-out direction separates them.
            return _vanillaLineAtUnitScale * Mathf.Max(0.01f, Plugin.VanillaScale.Value);
        }

        private bool SpawnScreenWorld(string text, Vector3 worldPosition, Vector2 jitter, Color color)
        {
            ScreenDamageNumberView view = AcquireScreenView();
            if (view == null) return false;

            view.PlayWorld(
                text,
                worldPosition,
                jitter,
                Mathf.Max(0.05f, Plugin.Lifetime.Value),
                Mathf.Max(8, Plugin.ScreenFontSize.Value),
                color);
            return true;
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

        private bool SpawnWorld(string text, Vector3 worldPosition, Vector3 jitter, Color color)
        {
            DamageNumberView view = AcquireWorldView();
            if (view == null) return false;

            view.Play(
                text,
                worldPosition,
                jitter,
                Mathf.Max(0.05f, Plugin.Lifetime.Value),
                Plugin.FloatSpeed.Value,
                Plugin.WorldScale.Value,
                Plugin.FontSize.Value,
                color);
            return true;
        }

        private bool SpawnVanilla(float amount, float hitMultiplier, DamageTypes damageType, bool critical, Vector3 worldPosition)
        {
            try
            {
                VanillaDamageNumberView view = AcquireVanillaView();
                if (view == null) return false;

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
                return true;
            }
            catch (Exception e)
            {
                BreakVanillaBoundary(e);
                return false;
            }
        }

        private void SetScreenCanvasVisible(bool visible)
        {
            if (_screenCanvas != null && _screenCanvas.enabled != visible)
                _screenCanvas.enabled = visible;
        }

        /// <summary>Stops every number on screen and drops everything not shown yet.</summary>
        private void ClearAllNumbers()
        {
            _aggregates.Clear();
            _perHitQueue.Clear();
            _flushEntries.Clear();

            for (int i = 0; i < _screenPool.Count; i++)
                _screenPool[i].Stop();

            for (int i = 0; i < _worldPool.Count; i++)
                _worldPool[i].Stop();

            if (_vanillaPool.Count > 0)
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
            if (_vanillaBoundaryBroken) return null;

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
                return CreateScreenView();
            }

            if (oldest != null)
            {
                oldest.Stop();
                return oldest;
            }

            return inactive;
        }

        /// <summary>Adds one stopped screen view to the pool.</summary>
        private ScreenDamageNumberView CreateScreenView()
        {
            EnsureScreenRoot();

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

        /// <summary>Adds one stopped world view to the pool.</summary>
        private DamageNumberView CreateWorldView()
        {
            EnsureWorldRoot();

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
                return CreateWorldView();
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
            _screenCanvas = canvas;

            // LateUpdate keeps this in step with the game state; a canvas created in between
            // starts out the way that check would leave it.
            canvas.enabled = GameStateProbe.Read() == GameplayPhase.Running;
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

        /// <summary>
        /// The deterministic spread for the next PerHit number with this key. The key holds the
        /// target, the frame the hit was dealt in and the attack source; FlushPending resets the
        /// counters for every batch.
        /// </summary>
        private PerHitSpreadOffset GetNextPerHitSpread(PerHitSpreadKey key)
        {
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
            _flushEntries.Clear();
            _perHitQueue.Clear();
            _shownRowsPerTarget.Clear();
            _perHitSpreadCounters.Clear();
            _crowdingPositions.Clear();
            _screenPool.Clear();
            _worldPool.Clear();
            _vanillaPool.Clear();
            _screenRoot = null;
            _screenCanvas = null;
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
            public float HighestMultiplier;
            public float LastMultiplier;
            public float WeightedMultiplierSum;
            public float HighestBonus;
            public DamageTypes DamageType;
            public bool AnyCritical;
            public int TargetId;
            public int FirstSequence;
        }

        private struct PerHitEntry
        {
            public Vector3 Position;
            public float Amount;
            public float Multiplier;
            public float Bonus;
            public DamageTypes DamageType;
            public bool Critical;
            public PerHitSpreadKey SpreadKey;
            public int Sequence;
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

        /// <summary>
        /// Height of one line of this font in canvas pixels per font point (the canvas uses a
        /// constant pixel scale of 1). 0 when it could not be measured.
        /// </summary>
        public float LineHeightPerPoint { get; private set; }

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

            // Measured while the new layer is still active, with the font its Awake assigned.
            LineHeightPerPoint = DamageNumberViews.MeasureLineHeightPerPoint(_text);

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

    /// <summary>One hit to show, however it was resolved.</summary>
    internal struct DamageHit
    {
        public int TargetId;
        public int Frame;
        public Vector3 Position;
        public int SourceId;
        public bool Melee;
        public DamageTypes DamageType;
        public bool Critical;
        public float Amount;
        public float Multiplier;

        /// <summary>A bonus multiplier the amount already includes, shown beside it; 0 for none.</summary>
        public float Bonus;
        public int Sequence;

        public static int SourceIdOf(DamageSourceData sourceData)
        {
            if (sourceData.sourceWeapon != null)
                return sourceData.sourceWeapon.GetInstanceID();
            if (sourceData.instanceId != 0)
                return sourceData.instanceId;
            if (sourceData.sourceUnit != null)
                return sourceData.sourceUnit.GetInstanceID();
            return 0;
        }
    }

    internal struct PerHitSpreadKey : IEquatable<PerHitSpreadKey>
    {
        private int _targetId;
        private int _frame;
        private int _sourceId;

        public static PerHitSpreadKey Create(DamageHit hit)
        {
            return new PerHitSpreadKey
            {
                _targetId = hit.TargetId,
                _frame = hit.Frame,
                _sourceId = hit.SourceId
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

        public static AggregateKey Create(DamageHit hit)
        {
            return new AggregateKey
            {
                _targetId = hit.TargetId,
                _frame = hit.Frame,
                _sourceId = hit.SourceId,
                _damageType = Convert.ToInt32(hit.DamageType),
                _melee = hit.Melee
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

    /// <summary>The text of a number in the two renderers this mod draws itself.</summary>
    internal static class DamageNumberText
    {
        // The label is a fraction of the number's size and sits on the number's baseline, so the two line
        // up along the bottom. The gap is measured in the number's own size.
        private const string Gap = "<space=0.25em>";
        private const string LabelOpen = "<size=55%>";
        private const string LabelClose = "</size>";

        /// <summary>
        /// Adds a small "x1.5" after the number, a short gap away. The views center their text, and a label on one
        /// side alone would push the number off the point it belongs to, so the same label goes in front
        /// as well, fully transparent: it takes up the same width and draws nothing.
        /// </summary>
        public static string WithBonus(string number, float bonus)
        {
            if (bonus <= 0f)
                return number;

            // A bonus takes one of a few values, so the label of the last one is usually the one needed.
            if (bonus != _labelBonus)
            {
                _labelBonus = bonus;
                _label = LabelOpen + MultiplySign + bonus.ToString("0.##", CultureInfo.InvariantCulture) + LabelClose;
                _hiddenLabel = "<alpha=#00>" + _label + "<alpha=#FF>" + Gap;
                _label = Gap + _label;
            }

            return _hiddenLabel + number + _label;
        }

        private static float _labelBonus;
        private static string _label;
        private static string _hiddenLabel;
        private static string _multiplySign;

        /// <summary>
        /// The multiplication sign, or a plain "x" when the font cannot draw it. Both renderers leave
        /// their texts on TextMeshPro's default font, and the game's has no such glyph on 1.0.6.
        /// Fallback fonts do not count: a glyph from one is drawn by a separate sub-mesh, which the
        /// screen renderer's fade does not reach.
        /// </summary>
        private static string MultiplySign
        {
            get
            {
                if (_multiplySign == null)
                {
                    TMP_FontAsset font = TMP_Settings.defaultFontAsset;
                    _multiplySign = font != null && font.HasCharacter('×', false) ? "×" : "x";
                }

                return _multiplySign;
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
