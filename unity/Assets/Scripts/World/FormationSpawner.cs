using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FlightRecord
{
    // Interim enemy source for backlog 4.12, until hand-authored waves (4.2) replace it:
    // spawns each entry (enemy prefab x formation x path) in turn, waiting GapAfter seconds
    // between entries, and starts over at the end when Loop is on. Everything it spawns
    // comes from data (EnemyPath and EnemyFormation assets), so adding a path or formation
    // to try is an asset and a list entry, not code.
    //
    // SpawnGroup is the piece 4.2's waves are meant to reuse.
    public class FormationSpawner : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public GameObject Prefab;
            [Tooltip("Empty spawns a single enemy.")]
            public EnemyFormation Formation;
            public EnemyPath Path;
            [Tooltip("Flip the path and formation top to bottom (an entry from the top becomes one from the bottom).")]
            public bool MirrorY;
            [Tooltip("Seconds to wait after spawning this entry before the next one.")]
            [Min(0f)] public float GapAfter = 4f;
        }

        [Min(0f)] public float StartDelay = 2f;
        public bool Loop = true;
        public List<Entry> Entries = new List<Entry>();

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(StartDelay);

            do
            {
                foreach (var entry in Entries)
                {
                    SpawnGroup(entry.Prefab, entry.Formation, entry.Path, entry.MirrorY, transform);
                    // At least a frame per entry, so a list of zero gaps can't spin forever.
                    yield return entry.GapAfter > 0f ? new WaitForSeconds(entry.GapAfter) : null;
                }
                if (Entries.Count == 0) yield break;
            } while (Loop);
        }

        // Spawns one enemy per formation slot (one enemy without a formation), each placed
        // at the start of the path plus its slot offset, relative to the current view.
        // Returns the spawned enemies (empty if anything needed is missing).
        public static List<Enemy> SpawnGroup(GameObject prefab, EnemyFormation formation, EnemyPath path,
            bool mirrorY, Transform parent = null)
        {
            var spawned = new List<Enemy>();
            if (prefab == null || path == null || !Playfield.TryGetView(out var center, out var halfSize))
                return spawned;

            // One track for the whole group: PathTrack is read-only once built.
            var track = path.Build(halfSize, mirrorY);
            var start = center + new Vector2(track.StartX, track.StartY);

            if (formation == null || formation.Slots.Count == 0)
            {
                Spawn(prefab, track, start, Vector2.zero, 0f, parent, spawned);
                return spawned;
            }

            foreach (var slot in formation.Slots)
            {
                var offset = mirrorY ? new Vector2(slot.Offset.x, -slot.Offset.y) : slot.Offset;
                Spawn(prefab, track, start, offset, slot.Delay, parent, spawned);
            }
            IgnoreCollisionsWithinGroup(spawned);
            return spawned;
        }

        // Every layer collides with every other, so slots that wait stacked at the path's
        // start (a snake) or fly close together would shove each other off the path.
        private static void IgnoreCollisionsWithinGroup(List<Enemy> group)
        {
            var colliders = new List<Collider2D>();
            foreach (var enemy in group) colliders.AddRange(enemy.GetComponentsInChildren<Collider2D>());

            for (var i = 0; i < colliders.Count; i++)
            for (var j = i + 1; j < colliders.Count; j++)
                Physics2D.IgnoreCollision(colliders[i], colliders[j]);
        }

        private static void Spawn(GameObject prefab, Gameplay.PathTrack track, Vector2 start, Vector2 offset,
            float delay, Transform parent, List<Enemy> spawned)
        {
            var instance = Instantiate(prefab, start + offset, Quaternion.identity, parent);
            var enemy = instance.GetComponent<Enemy>();
            if (enemy == null) return;

            enemy.FollowPath(track, offset, delay);
            spawned.Add(enemy);
        }
    }
}
