using UnityEngine;

namespace InfinitySpace
{
    public static class TransformHelper
    {
        public static void LookAt2D(this Transform source, Transform target)
        {
            var direction = target.position - source.position;
            var angle = Mathf.Atan2(direction.x, direction.y) * -Mathf.Rad2Deg;
            source.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }
}
