using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // Attach to a GameObject with a UIDocument (Source Asset = ResourceAlert.uxml) in
    // Main.unity, active from scene load. Same show/hold/fade rhythm as
    // MissionBriefingController, but reusable: Player calls Flash() every time the player fires
    // with no ammo, tries to thrust with no fuel, or either resource (including health) drops
    // to the last 5%, instead of showing once on scene load. Exposes a static Instance instead
    // of a serialized reference because Player lives in a prefab and can't hold a hard reference
    // to a scene-only object.
    [RequireComponent(typeof(UIDocument))]
    public class ResourceAlertController : MonoBehaviour
    {
        public static ResourceAlertController Instance;

        [SerializeField] private float _holdSeconds = 1.2f;
        [SerializeField] private float _fadeSeconds = 0.4f;

        private VisualElement _root;
        private Label _label;
        private Coroutine _playing;

        private void OnEnable()
        {
            Instance = this;

            var root = GetComponent<UIDocument>().rootVisualElement;
            _root = root.Q<VisualElement>("resource-alert");
            _label = root.Q<Label>("resource-alert-text");
            if (_root != null) _root.style.opacity = 0f;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        public void Flash(string message, bool critical = false)
        {
            if (_root == null || _label == null) return;

            _label.text = message;
            _label.EnableInClassList("resource-alert__text--critical", critical);

            if (_playing != null) StopCoroutine(_playing);
            _playing = StartCoroutine(PlayAlert());
        }

        private IEnumerator PlayAlert()
        {
            yield return Fade(_root.resolvedStyle.opacity, 1f, 0.15f);
            yield return new WaitForSeconds(_holdSeconds);
            yield return Fade(1f, 0f, _fadeSeconds);
            _playing = null;
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                _root.style.opacity = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }
            _root.style.opacity = to;
        }
    }
}
