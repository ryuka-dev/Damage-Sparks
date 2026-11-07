using PerfectRandom.Sulfur.Gameplay;
using TMPro;
using UnityEngine;

namespace SulfurDamageNumbersMod
{
    /// <summary>
    /// One pooled clone of the game's own damage text prefab.
    ///
    /// The prefab owns its look, motion and fade through its Animator, so this view only places
    /// the clone, keeps it facing the camera the way the game's DamageTracker does, and decides
    /// when the slot may be reused. It never drives the animation itself.
    /// </summary>
    internal sealed class VanillaDamageNumberView : IDamageNumberView
    {
        // Used when the clone's clips cannot be read. Longer than any clip the prefab has shipped.
        private const float FallbackClipSeconds = 2.5f;

        // Slack after the longest clip before the slot is released and the clone switched off.
        private const float ClipEndMarginSeconds = 0.1f;

        private readonly GameObject _root;
        private readonly Transform _rootTransform;
        private readonly DamageTextInstance _instance;
        private readonly Transform _textTransform;
        private readonly float _activeSeconds;

        public bool IsActive { get; private set; }
        public float StartTime { get; private set; }

        /// <summary>
        /// Where the text is drawn right now. The Animator moves the text inside the clone, away
        /// from the point the number was placed at.
        /// </summary>
        public Vector3 CurrentWorldPosition
        {
            get { return _textTransform != null ? _textTransform.position : _rootTransform.position; }
        }

        /// <summary>
        /// World height of one text line at VanillaScale 1, read from the fresh clone before its
        /// Animator has moved or scaled anything. 0 when it could not be read.
        /// </summary>
        public float LineHeightAtUnitScale { get; private set; }

        private VanillaDamageNumberView(GameObject root, DamageTextInstance instance, Transform textTransform, float lineHeightAtUnitScale, float activeSeconds)
        {
            _root = root;
            _rootTransform = root.transform;
            _instance = instance;
            _textTransform = textTransform;
            LineHeightAtUnitScale = lineHeightAtUnitScale;
            _activeSeconds = activeSeconds;
        }

        public static VanillaDamageNumberView TryCreate(Transform parent, GameObject textPrefab)
        {
            if (parent == null || textPrefab == null)
                return null;

            GameObject root = new GameObject("SULFUR Vanilla Damage Number");
            root.transform.SetParent(parent, false);

            GameObject clone = Object.Instantiate(textPrefab, root.transform);
            DamageTextInstance instance = clone.GetComponent<DamageTextInstance>();
            if (instance == null)
            {
                Object.Destroy(root);
                return null;
            }

            // The clone sits on the pivot so the prefab's own animation is the only thing that
            // moves the text; Show() is therefore called without a start position, which is what
            // the game's own call does when a hit has no collision point. The authored rotation
            // and scale are left alone, exactly as the game's DamageTracker instantiates them.
            clone.transform.localPosition = Vector3.zero;

            TextMeshPro text = clone.GetComponentInChildren<TextMeshPro>(true);
            float lineHeight = MeasureLineHeight(text);
            float activeSeconds = LongestClipSeconds(clone) + ClipEndMarginSeconds;

            root.SetActive(false);
            return new VanillaDamageNumberView(root, instance, text != null ? text.transform : null, lineHeight, activeSeconds);
        }

        private static float MeasureLineHeight(TextMeshPro text)
        {
            if (text == null)
                return 0f;

            float height = text.GetPreferredValues("0").y * Mathf.Abs(text.transform.lossyScale.y);
            return float.IsNaN(height) || float.IsInfinity(height) || height < 0f ? 0f : height;
        }

        /// <summary>
        /// The longest clip the prefab's Animator can play. A number is on screen for one clip,
        /// so its slot is busy for that long and free afterwards.
        /// </summary>
        private static float LongestClipSeconds(GameObject clone)
        {
            Animator animator = clone.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null)
                return FallbackClipSeconds;

            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            float longest = 0f;
            if (clips != null)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] != null && clips[i].length > longest)
                        longest = clips[i].length;
                }
            }

            return longest > 0f ? longest : FallbackClipSeconds;
        }

        public void Play(int amount, Color color, Vector3 worldPosition, float scale, bool flyRight, bool critical)
        {
            _rootTransform.position = worldPosition;
            _rootTransform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

            // The Animator must be active before a trigger is set, otherwise the trigger is lost.
            // A clone that was switched off starts again from the controller's default state.
            _root.SetActive(true);
            _instance.Show(amount, color, null, flyRight, critical);

            StartTime = Time.time;
            IsActive = true;
        }

        public void Tick(Camera camera, float now)
        {
            if (!IsActive)
                return;

            if (camera != null)
            {
                // Matches the game's DamageTracker, which points the text holder at the viewer.
                _rootTransform.LookAt(camera.transform.position);
            }

            if (now - StartTime >= _activeSeconds)
            {
                // The clip has finished. Switch the clone off, as the game's DamageTracker does
                // with its own instances, so no finished number lingers or keeps animating.
                Stop();
            }
        }

        public void Stop()
        {
            IsActive = false;
            if (_root != null)
                _root.SetActive(false);
        }
    }
}
