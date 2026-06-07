using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using PerfectRandom.Sulfur.Core.Units;
using UnityEngine;

namespace SulfurDamageNumbersMod
{
    internal sealed class DamageNumberManager : MonoBehaviour
    {
        private static DamageNumberManager _instance;
        private readonly Dictionary<AggregateKey, AggregateEntry> _aggregates = new Dictionary<AggregateKey, AggregateEntry>();
        private readonly List<AggregateKey> _flushBuffer = new List<AggregateKey>();
        private readonly List<ScreenEntry> _screenEntries = new List<ScreenEntry>();
        private readonly Dictionary<PerHitSpreadKey, int> _perHitSpreadCounters = new Dictionary<PerHitSpreadKey, int>();
        private int _perHitSpreadFrame = -1;
        private GUIStyle _screenStyle;
        private GUIStyle _outlineStyle;

        public static void EnsureExists()
        {
            if (_instance != null) return;

            GameObject go = new GameObject("SULFUR Damage Number Manager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DamageNumberManager>();
        }

        public static void RegisterDamage(Unit target, DamageSourceData sourceData, float amount, float hitMultiplier)
        {
            EnsureExists();
            _instance.RegisterDamageInternal(target, sourceData, amount, hitMultiplier);
        }

        public static void SpawnScreenTestNumber()
        {
            EnsureExists();
            _instance.SpawnScreenFixed("999", new Vector2(Screen.width * 0.5f, Screen.height * 0.45f), DamageNumberColor.ForMultiplier(0f));
        }

        private void Update()
        {
            if (Plugin.DebugSpawnTestOnF8.Value && InputCompat.GetKeyDown(KeyCode.F8))
            {
                SpawnScreenTestNumber();
                if (Plugin.DebugLog.Value)
                    Plugin.Log.LogInfo("[DamageNumbers] F8 test number spawned.");
            }
        }

        private void RegisterDamageInternal(Unit target, DamageSourceData sourceData, float amount, float hitMultiplier)
        {
            Vector3 pos = GetDamagePosition(target);

            if (Plugin.Mode.Value == DisplayMode.PerHit)
            {
                PerHitSpreadOffset spread = GetNextPerHitSpread(target, sourceData);
                SpawnNumber(pos, amount, hitMultiplier, true, spread.WorldOffset, spread.ScreenOffset);
                return;
            }

            AggregateKey key = AggregateKey.Create(target, sourceData);
            AggregateEntry entry;
            if (!_aggregates.TryGetValue(key, out entry))
            {
                entry = new AggregateEntry
                {
                    Amount = 0f,
                    Position = pos,
                    Frame = Time.frameCount,
                    HighestMultiplier = 0f,
                    LastMultiplier = 0f,
                    WeightedMultiplierSum = 0f
                };
            }

            entry.Amount += amount;
            entry.Position = pos;
            entry.Frame = Time.frameCount;
            entry.LastMultiplier = hitMultiplier;
            entry.WeightedMultiplierSum += Mathf.Max(0f, hitMultiplier) * Mathf.Max(0f, amount);
            if (hitMultiplier > entry.HighestMultiplier)
            {
                entry.HighestMultiplier = hitMultiplier;
            }

            _aggregates[key] = entry;
        }

        private void LateUpdate()
        {
            if (_aggregates.Count == 0) return;

            _flushBuffer.Clear();
            foreach (KeyValuePair<AggregateKey, AggregateEntry> kv in _aggregates)
            {
                if (kv.Value.Frame <= Time.frameCount)
                    _flushBuffer.Add(kv.Key);
            }

            for (int i = 0; i < _flushBuffer.Count; i++)
            {
                AggregateKey key = _flushBuffer[i];
                AggregateEntry entry;
                if (!_aggregates.TryGetValue(key, out entry)) continue;

                SpawnNumber(entry.Position, entry.Amount, ResolveAggregateMultiplier(entry), false, Vector3.zero, Vector2.zero);
                _aggregates.Remove(key);
            }
        }

        private void OnGUI()
        {
            if (_screenEntries.Count == 0) return;
            EnsureGuiStyles();

            Camera cam = Camera.main;
            float now = Time.time;

            for (int i = _screenEntries.Count - 1; i >= 0; i--)
            {
                ScreenEntry entry = _screenEntries[i];
                float age = now - entry.StartTime;
                if (age >= entry.Lifetime)
                {
                    _screenEntries.RemoveAt(i);
                    continue;
                }

                Vector2 screenPos;
                if (entry.FixedScreen)
                {
                    screenPos = entry.ScreenPosition;
                }
                else
                {
                    if (cam == null) continue;
                    Vector3 world = entry.WorldPosition + Vector3.up * (age * 0.25f);
                    Vector3 sp = cam.WorldToScreenPoint(world);
                    if (sp.z <= 0f) continue;
                    screenPos = new Vector2(sp.x, Screen.height - sp.y);
                }

                screenPos += entry.ScreenJitter;
                screenPos.y -= age * Plugin.ScreenFloatSpeed.Value;

                float alpha = Mathf.Clamp01(1f - age / entry.Lifetime);
                Color textColor = entry.Color;
                textColor.a = alpha;
                Color outlineColor = new Color(0f, 0f, 0f, alpha);

                Rect r = new Rect(screenPos.x - 100f, screenPos.y - 32f, 200f, 64f);
                _outlineStyle.normal.textColor = outlineColor;
                _screenStyle.normal.textColor = textColor;

                DrawOutlinedLabel(r, entry.Text);
            }
        }

        private void DrawOutlinedLabel(Rect r, string text)
        {
            Rect o = r;
            o.x -= 2f; GUI.Label(o, text, _outlineStyle);
            o = r; o.x += 2f; GUI.Label(o, text, _outlineStyle);
            o = r; o.y -= 2f; GUI.Label(o, text, _outlineStyle);
            o = r; o.y += 2f; GUI.Label(o, text, _outlineStyle);
            GUI.Label(r, text, _screenStyle);
        }

        private void EnsureGuiStyles()
        {
            int size = Mathf.Max(8, Plugin.ScreenFontSize.Value);
            if (_screenStyle != null && _screenStyle.fontSize == size) return;

            _screenStyle = new GUIStyle(GUI.skin.label);
            _screenStyle.alignment = TextAnchor.MiddleCenter;
            _screenStyle.fontStyle = FontStyle.Bold;
            _screenStyle.fontSize = size;
            _screenStyle.clipping = TextClipping.Overflow;

            _outlineStyle = new GUIStyle(_screenStyle);
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
            {
                index = 0;
            }
            _perHitSpreadCounters[key] = index + 1;

            if (index <= 0)
            {
                return PerHitSpreadOffset.Zero;
            }

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


        private static void SpawnNumber(Vector3 position, float amount, float hitMultiplier, bool useJitter, Vector3 extraWorldJitter, Vector2 extraScreenJitter)
        {
            string text = FormatDamage(amount);
            Color color = DamageNumberColor.ForMultiplier(hitMultiplier);

            Vector3 worldJitter = extraWorldJitter;
            Vector2 screenJitter = extraScreenJitter;
            if (useJitter)
            {
                if (Plugin.RandomJitter.Value > 0f)
                {
                    Vector2 r = UnityEngine.Random.insideUnitCircle * Plugin.RandomJitter.Value;
                    worldJitter += new Vector3(r.x, 0f, r.y);
                }

                if (Plugin.ScreenJitter.Value > 0f)
                    screenJitter += UnityEngine.Random.insideUnitCircle * Plugin.ScreenJitter.Value;
            }

            if (Plugin.Renderer.Value == RenderMode.ScreenSpace || Plugin.Renderer.Value == RenderMode.Both)
                _instance.SpawnScreenWorld(text, position, screenJitter, color);

            if (Plugin.Renderer.Value == RenderMode.WorldTextMeshPro || Plugin.Renderer.Value == RenderMode.Both)
            {
                GameObject go = new GameObject("SULFUR Damage Number");
                DamageNumberView view = go.AddComponent<DamageNumberView>();
                view.Init(text, position, worldJitter, Plugin.Lifetime.Value, Plugin.FloatSpeed.Value, Plugin.WorldScale.Value, Plugin.FontSize.Value, color);
            }
        }

        private void SpawnScreenWorld(string text, Vector3 worldPosition, Vector2 jitter, Color color)
        {
            TrimOldScreenEntriesIfNeeded();
            _screenEntries.Add(new ScreenEntry
            {
                Text = text,
                WorldPosition = worldPosition,
                ScreenJitter = jitter,
                StartTime = Time.time,
                Lifetime = Mathf.Max(0.05f, Plugin.Lifetime.Value),
                FixedScreen = false,
                Color = color
            });
        }

        private void SpawnScreenFixed(string text, Vector2 screenPosition, Color color)
        {
            TrimOldScreenEntriesIfNeeded();
            _screenEntries.Add(new ScreenEntry
            {
                Text = text,
                ScreenPosition = screenPosition,
                ScreenJitter = Vector2.zero,
                StartTime = Time.time,
                Lifetime = Mathf.Max(0.05f, Plugin.Lifetime.Value),
                FixedScreen = true,
                Color = color
            });
        }

        private void TrimOldScreenEntriesIfNeeded()
        {
            int max = Mathf.Max(1, Plugin.MaxActiveNumbers.Value);
            while (_screenEntries.Count >= max)
            {
                _screenEntries.RemoveAt(0);
            }
        }

        private static string FormatDamage(float amount)
        {
            if (Plugin.ShowDecimals.Value)
            {
                float oneDecimal = Mathf.Round(amount * 10f) / 10f;
                float integer = Mathf.Round(oneDecimal);
                if (Mathf.Abs(oneDecimal - integer) <= 0.0001f)
                {
                    int intValue = Mathf.RoundToInt(integer);
                    if (intValue <= 0 && amount > 0f) intValue = 1;
                    return intValue.ToString(CultureInfo.InvariantCulture);
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

        private struct AggregateEntry
        {
            public float Amount;
            public Vector3 Position;
            public int Frame;
            public float HighestMultiplier;
            public float LastMultiplier;
            public float WeightedMultiplierSum;
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

        private sealed class ScreenEntry
        {
            public string Text;
            public Vector3 WorldPosition;
            public Vector2 ScreenPosition;
            public Vector2 ScreenJitter;
            public float StartTime;
            public float Lifetime;
            public bool FixedScreen;
            public Color Color;
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

        private static MethodInfo _getShapeMultiplierMethod;
        private static Type _cachedHitDataType;

        public static float ResolveHitMultiplier(object hitData)
        {
            if (hitData == null)
            {
                return 0f;
            }

            try
            {
                Type type = hitData.GetType();
                MethodInfo method = GetShapeMultiplierMethod(type);
                if (method == null)
                {
                    return 0f;
                }

                object result = method.Invoke(hitData, null);
                if (result is float)
                {
                    return Mathf.Max(0f, (float)result);
                }

                if (result is double)
                {
                    return Mathf.Max(0f, (float)(double)result);
                }
            }
            catch
            {
                return 0f;
            }

            return 0f;
        }

        public static Color ForMultiplier(float multiplier)
        {
            if (!Plugin.UseWeakspotColors.Value)
            {
                return NormalDamageColor;
            }

            if (multiplier <= 0f)
            {
                return NormalDamageColor;
            }

            if (Plugin.TreatOneMultiplierAsNormal.Value && Mathf.Abs(multiplier - 1f) <= 0.0001f)
            {
                return NormalDamageColor;
            }

            if (multiplier >= 1.5f)
            {
                return Color.red;
            }

            if (multiplier >= 1f)
            {
                return OrangeDamageColor;
            }

            if (multiplier >= 0.75f)
            {
                return Color.yellow;
            }

            return NormalDamageColor;
        }

        private static MethodInfo GetShapeMultiplierMethod(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (_cachedHitDataType == type)
            {
                return _getShapeMultiplierMethod;
            }

            _cachedHitDataType = type;
            _getShapeMultiplierMethod = type.GetMethod("GetShapeMultiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            return _getShapeMultiplierMethod;
        }
    }

    internal static class InputCompat
    {
        private static readonly MethodInfo GetKeyDownMethod = FindGetKeyDownMethod();

        public static bool GetKeyDown(KeyCode key)
        {
            if (GetKeyDownMethod == null) return false;

            try
            {
                object result = GetKeyDownMethod.Invoke(null, new object[] { key });
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo FindGetKeyDownMethod()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type inputType = assemblies[i].GetType("UnityEngine.Input", false);
                if (inputType == null) continue;

                MethodInfo method = inputType.GetMethod("GetKeyDown", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(KeyCode) }, null);
                if (method != null) return method;
            }

            return null;
        }
    }
}
