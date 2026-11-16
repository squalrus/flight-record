using System;
using FlightRecord.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // "Now playing" in the HUD's bottom-left corner: cover art, title, artist, genre, and
    // time left on the track, the side, and the album. Added to the HUD's UIDocument at
    // runtime by VectorHudController (styles in HUD.uss, .now-playing). Hidden while there is
    // nothing to show, so the game without audio looks as before.
    //
    // Sources, best first: identification (track_identified, release_selected from the
    // sidecar's recognition and Discogs lookup), then the tags and cover art of a file the
    // game plays (GameAudio.FileTags). Cover art: the file's own, else the cover_path the
    // sidecar sent (Discogs' image, cached on this machine). Side and album time come from
    // side_state, else from the Discogs track list around the identified track; a lone file
    // has no album track list, so its album time is unknown.
    [RequireComponent(typeof(UIDocument))]
    public class NowPlayingHud : MonoBehaviour
    {
        private VisualElement _panel, _art, _disc;
        private Label _title, _artist, _genre, _track, _side, _album;
        private Texture2D _cover;
        private object _coverFrom;
        private float _discAngle;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            var hud = root.Q(className: "hud") ?? root;

            _panel = new VisualElement { name = "now-playing", pickingMode = PickingMode.Ignore };
            _panel.AddToClassList("now-playing");

            _art = new VisualElement { pickingMode = PickingMode.Ignore };
            _art.AddToClassList("now-playing__art");
            _disc = new VisualElement { pickingMode = PickingMode.Ignore };
            _disc.AddToClassList("now-playing__disc");
            _disc.generateVisualContent += DrawDisc;
            _art.Add(_disc);
            _panel.Add(_art);

            var info = new VisualElement { pickingMode = PickingMode.Ignore };
            info.AddToClassList("now-playing__info");
            info.Add(NewLabel("NOW PLAYING", "now-playing__label"));
            info.Add(_title = NewLabel("", "now-playing__title"));
            info.Add(_artist = NewLabel("", "now-playing__artist"));
            info.Add(_genre = NewLabel("", "now-playing__genre"));
            var times = new VisualElement { pickingMode = PickingMode.Ignore };
            times.AddToClassList("now-playing__times");
            times.Add(_track = NewLabel("", "now-playing__time"));
            times.Add(_side = NewLabel("", "now-playing__time"));
            times.Add(_album = NewLabel("", "now-playing__time"));
            info.Add(times);
            _panel.Add(info);

            hud.Add(_panel);
            _panel.style.display = DisplayStyle.None;
        }

        private void OnDisable()
        {
            _panel?.RemoveFromHierarchy();
            if (_cover != null) Destroy(_cover);
            _cover = null;
            _coverFrom = null;
        }

        private static Label NewLabel(string text, string cls)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            return l;
        }

        private void Update()
        {
            var audio = GameAudio.Instance;
            var tags = audio != null ? audio.FileTags : null;
            var track = audio != null ? audio.Track : null;
            bool show = audio != null && (tags != null || track != null || audio.LastReleased != null);
            _panel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            double now = AudioClock.Now;
            double? t = audio.AudioTime(now);

            if (track == null && tags == null)
            {
                // Live input before a match: say what identification is doing.
                bool missed = audio.Unidentified != null;
                _title.text = missed ? "UNKNOWN TRACK" : "IDENTIFYING\u2026";
                _artist.text = missed ? NoMatchText(audio.Unidentified) : "listening to the input";
            }
            else
            {
                _title.text = (track?.Title ?? tags?.Title ?? "Unknown track").ToUpperInvariant();
                _artist.text = track?.Artist ?? tags?.Artist ?? "Unknown artist";
            }
            string album = track?.Album ?? audio.Release?.Album ?? tags?.Album;
            if (audio.Release?.Year is int year && album != null) album += $" ({year})";
            string genre = Genre(audio, tags);
            _genre.text = (genre ?? "genre unknown") + (album != null ? $"  ·  {album}" : "");

            double? trackLeft = TrackLeft(audio, t, now);
            double? sideLeft = SideLeft(audio, t)
                ?? (audio.Playback != null ? trackLeft : null) // a file is one side
                ?? SideLeftFromTracks(audio, trackLeft, out _);
            double? albumLeft = AlbumLeft(audio, sideLeft);
            _track.text = "TRACK " + Remaining(trackLeft);
            _side.text = "SIDE " + Remaining(sideLeft);
            _album.text = "ALBUM " + Remaining(albumLeft);

            UpdateArt(tags, audio.Release?.CoverPath ?? track?.CoverPath);
            if (_cover == null)
            {
                // The placeholder record turns while audio plays (33 1/3 rpm would blur; slower reads better).
                if (t != null) _discAngle = (_discAngle + Time.unscaledDeltaTime * 90f) % 360f;
                _disc.MarkDirtyRepaint();
            }
        }

        private static string Genre(GameAudio audio, Id3Tags tags)
        {
            var g = audio.Track?.Genres;
            if (g != null && g.Length > 0) return string.Join(", ", g);
            var r = audio.Release;
            if (r?.Styles != null && r.Styles.Length > 0) return string.Join(", ", r.Styles);
            if (r?.Genres != null && r.Genres.Length > 0) return string.Join(", ", r.Genres);
            return tags?.Genre;
        }

        private static string NoMatchText(IdentificationFailed miss) =>
            miss.Reason == "no_match" ? "no match yet, retrying" : $"identification {miss.Reason.Replace('_', ' ')}, retrying";

        private static double? TrackLeft(GameAudio audio, double? t, double now)
        {
            var p = audio.Playback;
            if (p != null && p.Ready && p.Position(now) is double pos) return p.Length - pos;
            var tr = audio.Track;
            if (tr == null || !(t is double tt) || !(tr.PositionAt(tt) is double into)) return null;
            double? length = tr.TrackSeconds ?? CurrentReleaseTrack(audio)?.Seconds;
            return length - into;
        }

        // The identified track in the Discogs track list, matched by title; null if absent.
        private static ReleaseSelected.Track? CurrentReleaseTrack(GameAudio audio)
        {
            int i = CurrentTrackIndex(audio);
            return i >= 0 ? audio.Release.Tracks[i] : (ReleaseSelected.Track?)null;
        }

        private static int CurrentTrackIndex(GameAudio audio)
        {
            var r = audio.Release;
            var tr = audio.Track;
            if (r?.Tracks == null || tr == null) return -1;
            string want = Normalize(tr.Title);
            for (int i = 0; i < r.Tracks.Length; i++)
                if (Normalize(r.Tracks[i].Title) == want) return i;
            for (int i = 0; i < r.Tracks.Length; i++) // "Title (Remastered)" vs "Title"
            {
                string have = Normalize(r.Tracks[i].Title);
                if (have.Length > 0 && want.Length > 0 && (have.StartsWith(want) || want.StartsWith(have))) return i;
            }
            return -1;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        // Rest of this track plus the later tracks on the same side, from the Discogs list.
        private static double? SideLeftFromTracks(GameAudio audio, double? trackLeft, out string side)
        {
            side = null;
            int i = CurrentTrackIndex(audio);
            if (i < 0 || !(trackLeft is double left)) return null;
            var tracks = audio.Release.Tracks;
            side = SideOf(tracks[i].Pos);
            double total = left;
            for (int j = i + 1; j < tracks.Length; j++)
            {
                if (!string.Equals(SideOf(tracks[j].Pos), side, StringComparison.OrdinalIgnoreCase)) continue;
                if (tracks[j].Seconds is double s) total += s;
                else return null;
            }
            return total;
        }

        private static double? SideLeft(GameAudio audio, double? t)
        {
            var s = audio.Side;
            if (s?.SideExpected is double expected && t is double tt)
                return expected - (s.SideElapsed + (tt - s.T));
            return null;
        }

        // Rest of this side plus every track on later sides (A1.. then B1..), when Discogs gave durations.
        private static double? AlbumLeft(GameAudio audio, double? sideLeft)
        {
            var r = audio.Release;
            if (r?.Tracks == null || r.Tracks.Length == 0 || sideLeft == null) return null;
            int current = CurrentTrackIndex(audio);
            string side = audio.Side?.Side ?? (current >= 0 ? SideOf(r.Tracks[current].Pos) : null) ?? r.Side;
            if (string.IsNullOrEmpty(side)) return null;
            double later = 0;
            foreach (var tr in r.Tracks)
            {
                if (string.IsNullOrEmpty(tr.Pos) || string.Compare(SideOf(tr.Pos), side, StringComparison.OrdinalIgnoreCase) <= 0)
                    continue;
                if (tr.Seconds is double s) later += s;
                else return null; // a missing duration makes the total unknowable
            }
            return sideLeft + later;
        }

        private static string SideOf(string pos)
        {
            int i = 0;
            while (i < pos.Length && char.IsLetter(pos[i])) i++;
            return pos.Substring(0, i);
        }

        private static string Remaining(double? seconds)
        {
            if (!(seconds is double s)) return "--:--";
            s = Math.Max(0, s);
            int total = (int)Math.Ceiling(s);
            return total >= 3600 ? $"-{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" : $"-{total / 60}:{total % 60:00}";
        }

        // The file's own cover first, else an image file the sidecar pointed at.
        private void UpdateArt(Id3Tags tags, string coverPath)
        {
            object from = tags?.Picture != null ? (object)tags : coverPath;
            if (Equals(from, _coverFrom)) return;
            _coverFrom = from;
            if (_cover != null) Destroy(_cover);
            _cover = null;
            byte[] bytes = tags?.Picture;
            if (bytes == null && !string.IsNullOrEmpty(coverPath))
            {
                try { bytes = System.IO.File.ReadAllBytes(coverPath); }
                catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                {
                    Debug.LogWarning($"[NowPlaying] cover not readable: {e.Message}");
                }
            }
            if (bytes != null)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(bytes)) _cover = tex;
                else Destroy(tex);
            }
            _art.style.backgroundImage = _cover != null ? new StyleBackground(_cover) : new StyleBackground(StyleKeyword.None);
            _disc.style.display = _cover != null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // A stylized record: grooves, a label, and a highlight that turns.
        private void DrawDisc(MeshGenerationContext ctx)
        {
            var r = _disc.contentRect;
            var c = r.center;
            float radius = Mathf.Min(r.width, r.height) * 0.5f - 1f;
            if (radius <= 0) return;
            var p = ctx.painter2D;
            var cyan = new Color(0.498f, 0.914f, 1f);

            p.fillColor = new Color(0.03f, 0.05f, 0.08f, 0.95f);
            p.BeginPath();
            p.Arc(c, radius, 0, 360);
            p.Fill();

            p.lineWidth = 1f;
            for (float g = 0.45f; g < 0.98f; g += 0.13f)
            {
                p.strokeColor = new Color(cyan.r, cyan.g, cyan.b, 0.18f);
                p.BeginPath();
                p.Arc(c, radius * g, 0, 360);
                p.Stroke();
            }

            p.lineWidth = 2f;
            p.strokeColor = new Color(cyan.r, cyan.g, cyan.b, 0.55f);
            p.BeginPath();
            p.Arc(c, radius * 0.8f, _discAngle, _discAngle + 40f);
            p.Stroke();

            p.fillColor = new Color(cyan.r, cyan.g, cyan.b, 0.8f);
            p.BeginPath();
            p.Arc(c, radius * 0.3f, 0, 360);
            p.Fill();
            p.fillColor = new Color(0.03f, 0.05f, 0.08f, 1f);
            p.BeginPath();
            p.Arc(c, radius * 0.06f, 0, 360);
            p.Fill();
        }
    }
}
