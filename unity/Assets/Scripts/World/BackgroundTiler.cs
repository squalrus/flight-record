using UnityEngine;

namespace FlightRecord
{
    [DefaultExecutionOrder(100)]
    public class BackgroundTiler : MonoBehaviour
    {
        [SerializeField] GameObject _tilePrefab;

        private Transform[] _tiles;
        private float _tileWidth;
        private float _tileHeight;
        private Vector2 _lastAnchor;
        private Camera _camera;

        private void Start()
        {
            _camera = Camera.main;

            _tiles = new Transform[9];
            for (int i = 0; i < 9; i++)
                _tiles[i] = Instantiate(_tilePrefab, Vector3.zero, Quaternion.identity, transform).transform;

            var bounds = _tiles[0].GetComponent<SpriteRenderer>().bounds;
            _tileWidth = bounds.size.x;
            _tileHeight = bounds.size.y;

            _lastAnchor = Vector2.positiveInfinity;
            RepositionTiles();
        }

        private void LateUpdate() => RepositionTiles();

        private void RepositionTiles()
        {
            Vector2 camPos = _camera.transform.position;
            var anchor = new Vector2(
                Mathf.Round(camPos.x / _tileWidth) * _tileWidth,
                Mathf.Round(camPos.y / _tileHeight) * _tileHeight
            );

            if (anchor == _lastAnchor) return;
            _lastAnchor = anchor;

            int k = 0;
            float z = transform.position.z;
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    _tiles[k++].position = new Vector3(anchor.x + x * _tileWidth, anchor.y + y * _tileHeight, z);
        }
    }
}
