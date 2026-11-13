using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace InfinitySpace
{
    // Attach to a GameObject with a UIDocument (Source Asset = MissionBriefing.uxml) in
    // Main.unity, active from scene load. Shows "MISSION: <Sector.MissionText>" briefly,
    // then fades out and hides. Reads the objective string from Sector rather than
    // hardcoding it here, so Sector Type Variants (see BACKLOG.md) can later report a
    // different per-sector objective without this display logic changing.
    [RequireComponent(typeof(UIDocument))]
    public class MissionBriefingController : MonoBehaviour
    {
        [SerializeField] private float _holdSeconds = 2.5f;
        [SerializeField] private float _fadeSeconds = 0.6f;

        private VisualElement _root;
        private Label _label;
        private Label _descriptionLabel;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _root = root.Q<VisualElement>("mission-briefing");
            _label = root.Q<Label>("mission-briefing-text");
            _descriptionLabel = root.Q<Label>("mission-briefing-description");
            if (_root == null || _label == null) return;

            var sector = FindFirstObjectByType<Sector>();
            _label.text = sector != null ? sector.MissionText : "CLEAR THE HOSTILES";
            if (_descriptionLabel != null)
                _descriptionLabel.text = sector != null ? sector.MissionDescription : "Clear the threats and find the portal.";

            _root.style.opacity = 0f;
            StartCoroutine(PlayBriefing());
        }

        private IEnumerator PlayBriefing()
        {
            yield return Fade(0f, 1f, 0.4f);
            yield return new WaitForSeconds(_holdSeconds);
            yield return Fade(1f, 0f, _fadeSeconds);
            _root.style.display = DisplayStyle.None;
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
