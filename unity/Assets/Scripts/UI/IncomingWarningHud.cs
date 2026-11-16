using UnityEngine;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // Warns where the next enemy group comes in: a marker on the far right edge of the screen
    // that flashes on the beat (GameAudio.BeatPulse), or blinks steadily with no music, and
    // grows stronger as the spawn nears. Entries from the right are marked at the height
    // they enter, spanning the formation; entries from the top or bottom at that end of the
    // right edge, with chevrons pointing the way they will come. Reads the upcoming entry
    // from FormationSpawner.Next. Added to the HUD's UIDocument at runtime by
    // VectorHudController.
    [RequireComponent(typeof(UIDocument))]
    public class IncomingWarningHud : MonoBehaviour
    {
        // Seconds before a spawn that the warning shows.
        [SerializeField, Min(0.1f)] private float _leadSeconds = 2.5f;

        private static readonly Color Hostile = new Color(1f, 0.353f, 0.353f);
        private const float BlinkHz = 2.5f;     // no music: steady blink
        private const float MinBandPx = 48f;    // a single enemy still gets a visible marker
        private const float EndBandFraction = 0.18f; // top/bottom entries: this much of the edge

        private VisualElement _layer;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            var hud = root.Q(className: "hud") ?? root;
            _layer = new VisualElement { name = "incoming-warning", pickingMode = PickingMode.Ignore };
            _layer.style.position = Position.Absolute;
            _layer.style.left = 0;
            _layer.style.right = 0;
            _layer.style.top = 0;
            _layer.style.bottom = 0;
            _layer.generateVisualContent += Draw;
            hud.Insert(0, _layer); // behind the other HUD elements
        }

        private void OnDisable() => _layer?.RemoveFromHierarchy();

        private void Update() => _layer?.MarkDirtyRepaint();

        private float Flash()
        {
            if (GameAudio.HasBeat) return GameAudio.BeatPulse;
            return Mathf.Repeat(Time.unscaledTime * BlinkHz, 1f) < 0.5f ? 1f : 0f;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var spawners = FormationSpawner.Active;
            if (spawners.Count == 0) return;
            var rect = _layer.contentRect;
            if (rect.width <= 0 || rect.height <= 0) return;

            float flash = Flash();
            foreach (var spawner in spawners)
            {
                if (!(spawner.Next is FormationSpawner.Incoming next)) continue;
                float left = next.SpawnTime - Time.time;
                if (left < 0f || left > _leadSeconds) continue;
                float nearness = 1f - left / _leadSeconds; // 0 when it appears, 1 at the spawn
                float alpha = Mathf.Lerp(0.35f, 1f, nearness) * Mathf.Lerp(0.3f, 1f, flash);
                DrawMarker(ctx.painter2D, rect, next, alpha, flash);
            }
        }

        private static void DrawMarker(Painter2D p, Rect rect, FormationSpawner.Incoming next, float alpha, float flash)
        {
            float h = rect.height, w = rect.width;
            float top, bottom;
            switch (next.Edge)
            {
                case FormationSpawner.EntryEdge.Top:
                    top = 0; bottom = h * EndBandFraction; break;
                case FormationSpawner.EntryEdge.Bottom:
                    top = h * (1 - EndBandFraction); bottom = h; break;
                default:
                    top = (1f - Mathf.Clamp(next.YMax, -1f, 1f)) * 0.5f * h;
                    bottom = (1f - Mathf.Clamp(next.YMin, -1f, 1f)) * 0.5f * h;
                    if (bottom - top < MinBandPx)
                    {
                        float mid = (top + bottom) * 0.5f;
                        top = Mathf.Clamp(mid - MinBandPx * 0.5f, 0, h - MinBandPx);
                        bottom = top + MinBandPx;
                    }
                    break;
            }

            // A soft glow, then the bar on the very edge.
            float glow = 26f + 10f * flash;
            p.fillColor = new Color(Hostile.r, Hostile.g, Hostile.b, 0.18f * alpha);
            Rect(p, w - glow, top, glow, bottom - top);
            p.fillColor = new Color(Hostile.r, Hostile.g, Hostile.b, alpha);
            Rect(p, w - 5f, top, 5f, bottom - top);

            // Chevrons: pointing left for the right edge, down from the top, up from the bottom.
            p.strokeColor = new Color(1f, 0.8f, 0.8f, alpha);
            p.lineWidth = 3f;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            float cy = (top + bottom) * 0.5f, s = 9f;
            for (int i = 0; i < 2; i++)
            {
                p.BeginPath();
                switch (next.Edge)
                {
                    case FormationSpawner.EntryEdge.Top: // stacked, pointing down
                    {
                        float cx = w - 22f, y = cy - 8f + i * 10f;
                        p.MoveTo(new Vector2(cx - s, y));
                        p.LineTo(new Vector2(cx, y + s * 0.6f));
                        p.LineTo(new Vector2(cx + s, y));
                        break;
                    }
                    case FormationSpawner.EntryEdge.Bottom: // stacked, pointing up
                    {
                        float cx = w - 22f, y = cy + 8f - i * 10f;
                        p.MoveTo(new Vector2(cx - s, y));
                        p.LineTo(new Vector2(cx, y - s * 0.6f));
                        p.LineTo(new Vector2(cx + s, y));
                        break;
                    }
                    default: // side by side, pointing left
                    {
                        float cx = w - 20f - i * 14f;
                        p.MoveTo(new Vector2(cx + s * 0.5f, cy - s));
                        p.LineTo(new Vector2(cx - s * 0.5f, cy));
                        p.LineTo(new Vector2(cx + s * 0.5f, cy + s));
                        break;
                    }
                }
                p.Stroke();
            }
        }

        private static void Rect(Painter2D p, float x, float y, float w, float h)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
            p.Fill();
        }
    }
}
