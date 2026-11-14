using System.Collections.Generic;
using NUnit.Framework;

namespace FlightRecord.Audio.Tests
{
    public class MiniJsonTests
    {
        [Test]
        public void ParsesEveryJsonKind()
        {
            var d = (Dictionary<string, object>)MiniJson.Parse(
                "{\"a\": 1.5e2, \"b\": [true, false, null], \"c\": \"x\\\"\\u00e9\\n\", \"d\": {}, \"e\": -0.25}");
            Assert.AreEqual(150.0, d["a"]);
            CollectionAssert.AreEqual(new object[] { true, false, null }, (List<object>)d["b"]);
            Assert.AreEqual("x\"é\n", d["c"]);
            Assert.IsEmpty((Dictionary<string, object>)d["d"]);
            Assert.AreEqual(-0.25, d["e"]);
        }

        [TestCase("")]
        [TestCase("{")]
        [TestCase("{\"a\" 1}")]
        [TestCase("[1,]")]
        [TestCase("{\"a\": NaN}")]
        [TestCase("{} extra")]
        [TestCase("\"unterminated")]
        public void RejectsMalformedJson(string text)
        {
            Assert.Throws<System.FormatException>(() => MiniJson.Parse(text));
        }
    }

    public class ContractTests
    {
        // The features example from docs/flightrec/architecture.md, plus experiment 02's fields.
        const string Features =
            "{\"v\":1,\"type\":\"features\",\"t\":123.456,\"onset\":true,\"onset_strength\":0.82," +
            "\"bass\":0.61,\"mid\":0.34,\"treble\":0.12,\"rms\":0.40,\"tempo\":118.0,\"tempo_confidence\":0.7," +
            "\"onset_low\":true,\"onset_mid\":false,\"onset_high\":false,\"beat\":true,\"centroid_hz\":1200.5," +
            "\"key\":\"F# minor\",\"key_confidence\":0.4,\"chroma\":[0,0,0,0,0,0,1,0,0,0,0,0.5]}";

        [Test]
        public void ParsesFeatures()
        {
            var m = (FeaturesMessage)Contract.Parse(Features);
            Assert.AreEqual(1, m.V);
            Assert.AreEqual("features", m.Type);
            Assert.AreEqual(123.456, m.T, 1e-9);
            Assert.IsTrue(m.Onset);
            Assert.AreEqual(0.61f, m.Bass, 1e-6f);
            Assert.AreEqual(118f, m.Tempo.Value, 1e-6f);
            Assert.IsTrue(m.OnsetLow && m.Beat && !m.OnsetHigh);
            Assert.AreEqual("F# minor", m.Key);
            Assert.AreEqual(12, m.Chroma.Length);
        }

        [Test]
        public void OptionalFieldsDefaultAndUnknownFieldsAreKept()
        {
            var m = (FeaturesMessage)Contract.Parse(
                "{\"v\":1,\"type\":\"features\",\"t\":1,\"onset\":false,\"onset_strength\":0," +
                "\"bass\":0,\"mid\":0,\"treble\":0,\"rms\":0,\"tempo\":null,\"future_field\":[1,2]}");
            Assert.IsNull(m.Tempo);
            Assert.AreEqual(0f, m.TempoConfidence);
            Assert.IsNull(m.Chroma);
            Assert.IsTrue(m.Fields.ContainsKey("future_field"));
        }

        [Test]
        public void ParsesTheOtherTypes()
        {
            var track = (TrackIdentified)Contract.Parse(
                "{\"v\":1,\"type\":\"track_identified\",\"t\":132,\"artist\":\"A\",\"title\":\"T\"," +
                "\"album\":null,\"confidence\":0.9,\"track_seconds\":215.5,\"genres\":[\"Rock\"]}");
            Assert.AreEqual("A", track.Artist);
            Assert.IsNull(track.Album);
            Assert.AreEqual(215.5, track.TrackSeconds.Value, 1e-9);
            CollectionAssert.AreEqual(new[] { "Rock" }, track.Genres);

            var release = (ReleaseSelected)Contract.Parse(
                "{\"v\":1,\"type\":\"release_selected\",\"t\":133,\"discogs_id\":42,\"side\":\"A\"," +
                "\"tracks\":[{\"pos\":\"A1\",\"title\":\"One\",\"seconds\":215},{\"pos\":\"A2\",\"title\":\"Two\",\"seconds\":null}]}");
            Assert.AreEqual(42, release.DiscogsId);
            Assert.AreEqual(2, release.Tracks.Length);
            Assert.AreEqual(215.0, release.Tracks[0].Seconds.Value);
            Assert.IsNull(release.Tracks[1].Seconds);

            var side = (SideState)Contract.Parse(
                "{\"v\":1,\"type\":\"side_state\",\"t\":140,\"side\":\"A\",\"track_index\":1," +
                "\"side_elapsed\":410.2,\"side_expected\":null,\"side_confidence\":0.6}");
            Assert.AreEqual(1, side.TrackIndex);
            Assert.IsNull(side.SideExpected);

            Assert.AreEqual("run_out_groove", ((SideEnd)Contract.Parse(
                "{\"v\":1,\"type\":\"side_end\",\"t\":1325,\"cause\":\"run_out_groove\"}")).Cause);
            Assert.AreEqual("no_match", ((IdentificationFailed)Contract.Parse(
                "{\"v\":1,\"type\":\"identification_failed\",\"t\":150,\"reason\":\"no_match\"}")).Reason);
        }

        [Test]
        public void UnknownTypeParsesToNull()
        {
            Assert.IsNull(Contract.Parse("{\"v\":1,\"type\":\"from_the_future\",\"t\":1}"));
        }

        [Test]
        public void NewerVersionIsRejected()
        {
            Assert.Throws<UnsupportedVersionException>(() =>
                Contract.Parse("{\"v\":2,\"type\":\"side_end\",\"t\":1,\"cause\":\"timer\"}"));
        }

        [TestCase("{\"v\":1,\"type\":\"side_end\",\"t\":1}")] // missing required field
        [TestCase("{\"v\":1,\"type\":\"side_end\",\"t\":\"1\",\"cause\":\"x\"}")] // wrong type
        [TestCase("{\"type\":\"side_end\",\"t\":1,\"cause\":\"x\"}")] // no version
        [TestCase("{\"v\":1,\"type\":\"features\",\"t\":1,\"onset\":false,\"onset_strength\":0,\"bass\":1.5,\"mid\":0,\"treble\":0,\"rms\":0}")] // out of range
        [TestCase("{\"v\":1,\"type\":\"side_state\",\"t\":1,\"side\":\"A\",\"track_index\":0.5,\"side_elapsed\":0}")] // not an integer
        [TestCase("[1,2]")]
        [TestCase("not json")]
        public void InvalidMessagesAreContractErrors(string json)
        {
            Assert.Throws<ContractException>(() => Contract.Parse(json));
        }
    }
}
