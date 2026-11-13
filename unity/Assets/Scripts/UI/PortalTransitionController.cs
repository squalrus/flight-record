using UnityEngine;
using UnityEngine.UIElements;

namespace InfinitySpace
{
    // Attach to a GameObject with a UIDocument (Source Asset = PortalTransition.uxml) in
    // Main.unity, sorted behind the Sector Complete / Ship Management modal panels but above
    // the HUD. SectorTransitionController activates this GameObject for the whole portal
    // sequence so a starfield streaks past behind the stats/upgrade screens, giving the
    // impression the ship is flying through space while the player reads them. Runs on
    // unscaled time since the sequence happens while Time.timeScale == 0.
    [RequireComponent(typeof(UIDocument))]
    public class PortalTransitionController : MonoBehaviour
    {
        private static readonly Color StarColor = new Color(0.7f, 0.95f, 1f);
        private const float DefaultMaxRadius = 800f;

        [SerializeField] private int _starCount = 140;
        [SerializeField] private float _minSpeed = 60f;
        [SerializeField] private float _maxSpeed = 420f;
        [SerializeField] private float _accel = 220f;

        private struct Star
        {
            public Vector2 Direction;
            public float Distance;
            public float Speed;
        }

        private VisualElement _field;
        private Star[] _stars;
        private System.Random _rng;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _field = root.Q<VisualElement>("starfield");
            if (_field == null) return;

            _field.pickingMode = PickingMode.Ignore;
            _field.generateVisualContent += DrawStars;

            _rng = new System.Random();
            _stars = new Star[_starCount];
            for (int i = 0; i < _stars.Length; i++)
                _stars[i] = SpawnStar(spreadAcrossField: true);
        }

        private void Update()
        {
            if (_field == null) return;

            float dt = Time.unscaledDeltaTime;
            float maxRadius = MaxRadius();

            for (int i = 0; i < _stars.Length; i++)
            {
                var star = _stars[i];
                star.Speed = Mathf.Min(star.Speed + _accel * dt, _maxSpeed);
                star.Distance += star.Speed * dt;
                if (!(star.Distance <= maxRadius))
                    star = SpawnStar(spreadAcrossField: false);
                _stars[i] = star;
            }

            _field.MarkDirtyRepaint();
        }

        // spreadAcrossField seeds the star already partway through its outward flight (used
        // only on enable) so the field looks like it's mid-flight from the very first frame,
        // instead of every star starting clustered and motionless at the center.
        private Star SpawnStar(bool spreadAcrossField)
        {
            float angle = (float)(_rng.NextDouble() * Mathf.PI * 2f);
            float distance = spreadAcrossField ? (float)_rng.NextDouble() * MaxRadius() : 0f;
            float speed = spreadAcrossField
                ? Mathf.Lerp(_minSpeed, _maxSpeed, distance / MaxRadius())
                : _minSpeed;

            return new Star
            {
                Direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)),
                Distance = distance,
                Speed = speed
            };
        }

        // contentRect can report NaN (not just zero) before the panel's first layout pass,
        // which silently poisons every star's Distance forever once it leaks in (NaN
        // comparisons are always false, so the off-screen respawn check in Update() never
        // catches it either) -- guard explicitly rather than relying on a "< 1f" check alone.
        private float MaxRadius()
        {
            var rect = _field.contentRect;
            if (float.IsNaN(rect.width) || rect.width < 1f) return DefaultMaxRadius;
            return new Vector2(rect.width, rect.height).magnitude * 0.5f;
        }

        private void DrawStars(MeshGenerationContext ctx)
        {
            var rect = ctx.visualElement.contentRect;
            if (rect.width < 1f || _stars == null) return;

            var painter = ctx.painter2D;
            painter.fillColor = StarColor;
            Vector2 center = rect.center;
            float maxRadius = MaxRadius();

            foreach (var star in _stars)
            {
                Vector2 tip = center + star.Direction * star.Distance;
                float frac = star.Distance / maxRadius;
                float streak = Mathf.Lerp(1f, 14f, frac);
                Vector2 tail = tip - star.Direction * streak;

                float halfWidth = Mathf.Lerp(0.5f, 1.25f, frac);
                Vector2 perp = new Vector2(-star.Direction.y, star.Direction.x) * halfWidth;

                painter.BeginPath();
                painter.MoveTo(tail + perp);
                painter.LineTo(tip + perp);
                painter.LineTo(tip - perp);
                painter.LineTo(tail - perp);
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
