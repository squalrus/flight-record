using System;
using System.Collections.Generic;

namespace FlightRecord.Audio
{
    /// <summary>A message does not match the contract.</summary>
    public class ContractException : Exception
    {
        public ContractException(string message) : base(message) { }
    }

    /// <summary>The message's <c>v</c> is newer than this game knows; it must be rejected.</summary>
    public sealed class UnsupportedVersionException : ContractException
    {
        public UnsupportedVersionException(int version)
            : base($"schema version {version} is newer than {Contract.SchemaVersion}") { }
    }

    /// <summary>
    /// The message contract from docs/flightrec/architecture.md, read side. The Python
    /// reference implementation (flightrec/messages.py) is authoritative; this mirrors it.
    /// Unknown fields are ignored (kept in <see cref="ContractMessage.Fields"/>); a newer
    /// <c>v</c> is rejected; an unknown <c>type</c> parses to null so callers can skip it.
    /// </summary>
    public static class Contract
    {
        public const int SchemaVersion = 1;

        /// <summary>Parse one JSON message; null for an unknown type.</summary>
        /// <exception cref="ContractException">Malformed, or a newer schema version.</exception>
        public static ContractMessage Parse(string json)
        {
            object root;
            try { root = MiniJson.Parse(json); }
            catch (FormatException e) { throw new ContractException(e.Message); }
            if (!(root is Dictionary<string, object> d)) throw new ContractException("not a JSON object");

            var f = new Fields(d, "message");
            int v = f.Int("v");
            if (v > SchemaVersion) throw new UnsupportedVersionException(v);
            if (v < 1) throw new ContractException($"bad schema version {v}");
            string type = f.String("type");
            ContractMessage m = type switch
            {
                FeaturesMessage.TypeName => new FeaturesMessage(f),
                TrackIdentified.TypeName => new TrackIdentified(f),
                ReleaseSelected.TypeName => new ReleaseSelected(f),
                IdentificationFailed.TypeName => new IdentificationFailed(f),
                SideState.TypeName => new SideState(f),
                SideEnd.TypeName => new SideEnd(f),
                _ => null,
            };
            if (m == null) return null;
            m.V = v;
            m.Type = type;
            m.T = f.Double("t");
            m.Fields = d;
            return m;
        }
    }

    /// <summary>Base for every message.</summary>
    public abstract class ContractMessage
    {
        public int V { get; internal set; }
        public string Type { get; internal set; }

        /// <summary>Seconds on the sender's clock: for audio features, the audio time.</summary>
        public double T { get; internal set; }

        /// <summary>Every field as received, including ones this version does not define.</summary>
        public IReadOnlyDictionary<string, object> Fields { get; internal set; }
    }

    public sealed class FeaturesMessage : ContractMessage
    {
        public const string TypeName = "features";
        public readonly bool Onset;
        public readonly float OnsetStrength, Bass, Mid, Treble, Rms;
        public readonly float? Tempo;
        public readonly float TempoConfidence;
        public readonly bool OnsetLow, OnsetMid, OnsetHigh, Beat;
        public readonly float? CentroidHz;
        public readonly string Key;
        public readonly float KeyConfidence;
        public readonly float[] Chroma;

        internal FeaturesMessage(Fields f)
        {
            Onset = f.Bool("onset");
            OnsetStrength = f.Unit("onset_strength");
            Bass = f.Unit("bass");
            Mid = f.Unit("mid");
            Treble = f.Unit("treble");
            Rms = f.Unit("rms");
            Tempo = (float?)f.OptionalDouble("tempo", 0);
            TempoConfidence = f.OptionalUnit("tempo_confidence") ?? 0f;
            OnsetLow = f.OptionalBool("onset_low") ?? false;
            OnsetMid = f.OptionalBool("onset_mid") ?? false;
            OnsetHigh = f.OptionalBool("onset_high") ?? false;
            Beat = f.OptionalBool("beat") ?? false;
            CentroidHz = (float?)f.OptionalDouble("centroid_hz", 0);
            Key = f.OptionalString("key");
            KeyConfidence = f.OptionalUnit("key_confidence") ?? 0f;
            Chroma = f.OptionalUnitArray("chroma", 12);
        }
    }

    public sealed class TrackIdentified : ContractMessage
    {
        public const string TypeName = "track_identified";
        public readonly string Artist, Title, Album;
        public readonly float Confidence;
        public readonly double? TrackSeconds, OffsetSeconds;
        public readonly string RecordingId, Label, ReleaseDate;
        public readonly string[] Genres;

        internal TrackIdentified(Fields f)
        {
            Artist = f.String("artist");
            Title = f.String("title");
            Album = f.OptionalString("album");
            Confidence = f.OptionalUnit("confidence") ?? 0f;
            TrackSeconds = f.OptionalDouble("track_seconds", 0);
            OffsetSeconds = f.OptionalDouble("offset_seconds", 0);
            RecordingId = f.OptionalString("recording_id");
            Label = f.OptionalString("label");
            ReleaseDate = f.OptionalString("release_date");
            Genres = f.OptionalStrings("genres");
        }
    }

    public sealed class ReleaseSelected : ContractMessage
    {
        public const string TypeName = "release_selected";
        public readonly int DiscogsId;
        public readonly string Side, Album;
        public readonly Track[] Tracks;
        public readonly int? Year;
        public readonly string[] Genres, Styles;

        public readonly struct Track
        {
            public readonly string Pos, Title;
            public readonly double? Seconds;

            public Track(string pos, string title, double? seconds)
            {
                Pos = pos;
                Title = title;
                Seconds = seconds;
            }
        }

        internal ReleaseSelected(Fields f)
        {
            DiscogsId = f.Int("discogs_id", 0);
            Side = f.String("side");
            Album = f.OptionalString("album");
            Year = f.OptionalInt("year", 0);
            Genres = f.OptionalStrings("genres");
            Styles = f.OptionalStrings("styles");
            var tracks = new List<Track>();
            foreach (var item in f.OptionalList("tracks") ?? new List<object>())
            {
                if (!(item is Dictionary<string, object> t)) throw new ContractException("tracks: not an object");
                var tf = new Fields(t, "track");
                tracks.Add(new Track(tf.String("pos"), tf.String("title"), tf.OptionalDouble("seconds", 0)));
            }
            Tracks = tracks.ToArray();
        }
    }

    public sealed class IdentificationFailed : ContractMessage
    {
        public const string TypeName = "identification_failed";
        public readonly string Reason;

        internal IdentificationFailed(Fields f) { Reason = f.String("reason"); }
    }

    public sealed class SideState : ContractMessage
    {
        public const string TypeName = "side_state";
        public readonly string Side;
        public readonly int TrackIndex;
        public readonly double SideElapsed;
        public readonly double? SideExpected;
        public readonly float SideConfidence;

        internal SideState(Fields f)
        {
            Side = f.String("side");
            TrackIndex = f.Int("track_index", 0);
            SideElapsed = f.Double("side_elapsed", 0);
            SideExpected = f.OptionalDouble("side_expected", 0);
            SideConfidence = f.OptionalUnit("side_confidence") ?? 0f;
        }
    }

    public sealed class SideEnd : ContractMessage
    {
        public const string TypeName = "side_end";
        public readonly string Cause;

        internal SideEnd(Fields f) { Cause = f.String("cause"); }
    }

    /// <summary>Typed, validated access to one JSON object's fields.</summary>
    internal readonly struct Fields
    {
        readonly Dictionary<string, object> _d;
        readonly string _where;

        public Fields(Dictionary<string, object> d, string where)
        {
            _d = d;
            _where = where;
        }

        ContractException Bad(string name, string what) => new ContractException($"{_where}.{name}: {what}");

        object Required(string name)
        {
            if (!_d.TryGetValue(name, out var value) || value == null) throw Bad(name, "missing");
            return value;
        }

        bool Present(string name, out object value) => _d.TryGetValue(name, out value) && value != null;

        public string String(string name) =>
            Required(name) is string s ? s : throw Bad(name, "not a string");

        public bool Bool(string name) => Required(name) is bool b ? b : throw Bad(name, "not a boolean");

        public double Double(string name, double min = double.NegativeInfinity) =>
            CheckMin(name, Required(name) is double x ? x : throw Bad(name, "not a number"), min);

        public int Int(string name, int min = int.MinValue)
        {
            double x = Double(name, min);
            if (x != Math.Floor(x) || x > int.MaxValue) throw Bad(name, "not an integer");
            return (int)x;
        }

        public float Unit(string name) => CheckUnit(name, Double(name));

        public string OptionalString(string name) =>
            !Present(name, out var v) ? null : v is string s ? s : throw Bad(name, "not a string");

        public bool? OptionalBool(string name) =>
            !Present(name, out var v) ? (bool?)null : v is bool b ? b : throw Bad(name, "not a boolean");

        public double? OptionalDouble(string name, double min = double.NegativeInfinity) =>
            !Present(name, out var v) ? (double?)null
            : v is double x ? CheckMin(name, x, min) : throw Bad(name, "not a number");

        public int? OptionalInt(string name, int min = int.MinValue) =>
            Present(name, out _) ? Int(name, min) : (int?)null;

        public float? OptionalUnit(string name)
        {
            double? x = OptionalDouble(name);
            return x.HasValue ? CheckUnit(name, x.Value) : (float?)null;
        }

        public List<object> OptionalList(string name) =>
            !Present(name, out var v) ? null : v is List<object> l ? l : throw Bad(name, "not a list");

        public string[] OptionalStrings(string name)
        {
            var list = OptionalList(name);
            if (list == null) return null;
            var result = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
                result[i] = list[i] as string ?? throw Bad(name, "not a list of strings");
            return result;
        }

        public float[] OptionalUnitArray(string name, int length)
        {
            var list = OptionalList(name);
            if (list == null) return null;
            if (list.Count != length) throw Bad(name, $"needs {length} values");
            var result = new float[length];
            for (int i = 0; i < length; i++)
                result[i] = list[i] is double x ? CheckUnit(name, x) : throw Bad(name, "not a number");
            return result;
        }

        double CheckMin(string name, double x, double min) => x >= min ? x : throw Bad(name, $"below {min}");

        float CheckUnit(string name, double x) =>
            x >= 0 && x <= 1 ? (float)x : throw Bad(name, "outside 0..1");
    }
}
