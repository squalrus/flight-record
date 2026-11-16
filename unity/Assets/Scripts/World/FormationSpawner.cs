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
    //
    // While waiting, it publishes the next entry (Next): when it will spawn and where it
    // comes in, so the HUD can warn the players (IncomingWarningHud).
    public class FormationSpawner : MonoBehaviour
    {
        public enum EntryEdge { Right, Top, Bottom }

        // The next group to spawn, in view-relative units: Y from -1 (bottom of the view) to
        // 1 (top), X likewise from left to right; YMin..YMax spans the formation's slots.
        public struct Incoming
        {
            public float SpawnTime; // Time.time
            public EntryEdge Edge;
            public float X, YMin, YMax;
        }

        private static readonly List<FormationSpawner> _active = new List<FormationSpawner>();
        public static IReadOnlyList<FormationSpawner> Active => _active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _active.Clear();

        public Incoming? Next { get; private set; }

        private void OnEnable() => _active.Add(this);
        private void OnDisable() => _active.Remove(this);

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
            if (Entries.Count > 0) Next = Preview(Entries[0], Time.time + StartDelay);
            yield return new WaitForSeconds(StartDelay);

            do
            {
                for (var i = 0; i < Entries.Count; i++)
                {
                    var entry = Entries[i];
                    Next = null;
                    SpawnGroup(entry.Prefab, entry.Formation, entry.Path, entry.MirrorY, transform);

                    var following = i + 1 < Entries.Count ? Entries[i + 1] : Loop ? Entries[0] : null;
                    if (following != null) Next = Preview(following, Time.time + entry.GapAfter);
                    // At least a frame per entry, so a list of zero gaps can't spin forever.
                    yield return entry.GapAfter > 0f ? new WaitForSeconds(entry.GapAfter) : null;
                }
                if (Entries.Count == 0) yield break;
            } while (Loop);
            Next = null;
        }

        // Where an entry will come in, from the same path and formation data SpawnGroup uses.
        private static Incoming? Preview(Entry entry, float spawnTime)
        {
            if (entry.Prefab == null || entry.Path == null || !Playfield.TryGetView(out _, out var halfSize))
                return null;
            var track = entry.Path.Build(halfSize, entry.MirrorY);
            float yMin = track.StartY, yMax = track.StartY;
            if (entry.Formation != null)
            {
                foreach (var slot in entry.Formation.Slots)
                {
                    float y = track.StartY + (entry.MirrorY ? -slot.Offset.y : slot.Offset.y);
                    yMin = Mathf.Min(yMin, y);
                    yMax = Mathf.Max(yMax, y);
                }
            }
            var edge = track.StartY > halfSize.y ? EntryEdge.Top
                : track.StartY < -halfSize.y ? EntryEdge.Bottom
                : EntryEdge.Right;
            return new Incoming
            {
                SpawnTime = spawnTime,
                Edge = edge,
                X = track.StartX / halfSize.x,
                YMin = yMin / halfSize.y,
                YMax = yMax / halfSize.y,
            };
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
