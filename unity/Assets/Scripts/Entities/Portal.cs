using TMPro;
using UnityEngine;

namespace FlightRecord
{
    // Spawned by Sector once enemies are in place. Stays closed/greyed until the
    // sector's completion threshold is met, then opens (green, glowing, warbling)
    // and lets the player fly through to advance. Discovery (for HUD/radar) is
    // driven by Renderer.isVisible rather than a manual screen-space check.
    //
    // _glyph: a child "Text - TextMeshPro" (3D, not UGUI) object with Font Asset
    // set to "Infinity-Icons-HU SDF" and text set to "?" (U+003F — exit glyph).
    [RequireComponent(typeof(Collider2D))]
    public class Portal : MonoBehaviour
    {
        private static readonly Color ClosedColor = new Color(0.4f, 0.4f, 0.45f);
        private static readonly Color OpenColor = new Color(0.212f, 1f, 0.620f);

        [SerializeField] private TextMeshPro _glyph;
        [SerializeField] private float _warbleSpeed = 6f;
        [SerializeField] private float _warbleScale = 0.35f;
        [SerializeField] private float _glowPulseSpeed = 3f;

        private Sector _sector;
        private Renderer _glyphRenderer;
        private Vector3 _baseScale;

        public bool IsDiscovered { get; private set; }
        public bool IsOpen { get; private set; }
        public bool WasScanned { get; private set; }
        public Vector3 Position => transform.position;

        public void Initialize(Sector sector)
        {
            _sector = sector;
        }

        // Called when the player purchases the Portal Locator scan in Ship Management.
        public void ForceDiscover()
        {
            IsDiscovered = true;
            WasScanned = true;
        }

        private void Awake()
        {
            _baseScale = transform.localScale;
            _glyphRenderer = _glyph != null ? _glyph.GetComponent<Renderer>() : null;
            GetComponent<Collider2D>().isTrigger = true;
        }

        private void Update()
        {
            if (!IsDiscovered && _glyphRenderer != null && _glyphRenderer.isVisible)
                IsDiscovered = true;

            IsOpen = _sector != null && _sector.IsCleared;

            if (_glyph == null) return;

            if (IsOpen)
            {
                // Squash/stretch on perpendicular axes, 90° out of phase, so the
                // glyph alternates between wide-and-short and tall-and-thin.
                float wave = Mathf.Sin(Time.time * _warbleSpeed);
                float stretchX = 1f + wave * _warbleScale;
                float stretchY = 1f - wave * _warbleScale;
                transform.localScale = new Vector3(_baseScale.x * stretchX, _baseScale.y * stretchY, _baseScale.z);

                float glowPhase = 0.5f + 0.5f * Mathf.Sin(Time.time * _glowPulseSpeed);
                _glyph.color = Color.Lerp(OpenColor, Color.white, glowPhase * 0.4f);
            }
            else
            {
                transform.localScale = _baseScale;
                _glyph.color = ClosedColor;
            }
        }

        private void OnTriggerEnter2D(Collider2D collider)
        {
            if (!IsOpen) return;
            if (collider.gameObject.name != "Player") return;

            FindFirstObjectByType<SectorTransitionController>()?.BeginTransition(_sector);
        }
    }
}
