using PerfectRandom.Sulfur.Gameplay;
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
        // The game's DamageTracker cycles a fixed ring of instances and never stops them; the
        // Animator hides the text on its own. This mod still needs a reuse point, so a slot is
        // released once the clip has had more than enough time to finish.
        private const float RecycleAfterSeconds = 2.5f;

        private readonly GameObject _root;
        private readonly Transform _rootTransform;
        private readonly DamageTextInstance _instance;

        public bool IsActive { get; private set; }
        public float StartTime { get; private set; }

        public Vector3 CurrentWorldPosition
        {
            get { return _rootTransform.position; }
        }

        private VanillaDamageNumberView(GameObject root, DamageTextInstance instance)
        {
            _root = root;
            _rootTransform = root.transform;
            _instance = instance;
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

            root.SetActive(false);
            return new VanillaDamageNumberView(root, instance);
        }

        public void Play(int amount, Color color, Vector3 worldPosition, float scale, bool flyRight, bool critical)
        {
            _rootTransform.position = worldPosition;
            _rootTransform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

            // The Animator must be active before a trigger is set, otherwise the trigger is lost.
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

            if (now - StartTime >= RecycleAfterSeconds)
            {
                // Only the slot is released. The clone keeps whatever the finished clip left
                // behind, exactly like the game's own ring buffer.
                IsActive = false;
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
