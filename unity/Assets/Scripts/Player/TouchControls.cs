using UnityEngine;

namespace InfinitySpace
{
    public class TouchControls : MonoBehaviour
    {
        [SerializeField] private RectTransform _movePanel;
        [SerializeField] private RectTransform _firePanel;

        private static readonly Vector2 LeftMin  = new Vector2(0f,  0f);
        private static readonly Vector2 LeftMax  = new Vector2(0.5f, 1f);
        private static readonly Vector2 RightMin = new Vector2(0.5f, 0f);
        private static readonly Vector2 RightMax = new Vector2(1f,  1f);

        private void Start()
        {
            bool flipped = GameControl.control != null && GameControl.control.ControlsFlipped;
            ApplyLayout(flipped);
        }

        public void SetFlipped(bool flipped)
        {
            if (GameControl.control != null)
                GameControl.control.ControlsFlipped = flipped;
            ApplyLayout(flipped);
        }

        private void ApplyLayout(bool flipped)
        {
            if (flipped)
            {
                SetAnchors(_movePanel, RightMin, RightMax);
                SetAnchors(_firePanel, LeftMin,  LeftMax);
            }
            else
            {
                SetAnchors(_movePanel, LeftMin,  LeftMax);
                SetAnchors(_firePanel, RightMin, RightMax);
            }
        }

        private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
