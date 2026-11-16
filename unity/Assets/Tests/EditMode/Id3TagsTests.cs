using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace FlightRecord.Audio.Tests
{
    public class Id3TagsTests
    {
        static byte[] Frame(string id, byte[] body, int version)
        {
            int n = body.Length;
            byte[] size = version == 4
                ? new[] { (byte)((n >> 21) & 0x7F), (byte)((n >> 14) & 0x7F), (byte)((n >> 7) & 0x7F), (byte)(n & 0x7F) }
                : new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n };
            return Encoding.ASCII.GetBytes(id).Concat(size).Concat(new byte[2]).Concat(body).ToArray();
        }

        static byte[] TextFrame(string id, string text, int version) =>
            Frame(id, new byte[] { 3 }.Concat(Encoding.UTF8.GetBytes(text)).ToArray(), version);

        static byte[] Tag(int version, params byte[][] frames)
        {
            var body = frames.SelectMany(f => f).ToArray();
            int n = body.Length;
            var head = new byte[] { (byte)'I', (byte)'D', (byte)'3', (byte)version, 0, 0,
                (byte)((n >> 21) & 0x7F), (byte)((n >> 14) & 0x7F), (byte)((n >> 7) & 0x7F), (byte)(n & 0x7F) };
            return head.Concat(body).ToArray();
        }

        static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9 };

        [TestCase(3)]
        [TestCase(4)]
        public void ReadsTextFramesAndThePicture(int version)
        {
            var apic = new byte[] { 0 }.Concat(Encoding.ASCII.GetBytes("image/jpeg")).Concat(new byte[] { 0, 3 })
                .Concat(Encoding.ASCII.GetBytes("cover")).Concat(new byte[] { 0 }).Concat(Jpeg).ToArray();
            var t = Id3Tags.Parse(Tag(version,
                TextFrame("TIT2", "CLS.NFO", version),
                TextFrame("TPE1", "MASTER BOOT RECORD", version),
                TextFrame("TALB", "C:\\>COPY *.* A: /V", version),
                TextFrame("TCON", "(52)Electronic", version),
                TextFrame("TRCK", "5", version),
                Frame("APIC", apic, version)));
            Assert.AreEqual("CLS.NFO", t.Title);
            Assert.AreEqual("MASTER BOOT RECORD", t.Artist);
            Assert.AreEqual("C:\\>COPY *.* A: /V", t.Album);
            Assert.AreEqual("Electronic", t.Genre);
            Assert.AreEqual("5", t.TrackNumber);
            Assert.AreEqual("image/jpeg", t.PictureMime);
            CollectionAssert.AreEqual(Jpeg, t.Picture);
        }

        [Test]
        public void Utf16TextAndDescription()
        {
            var title = new byte[] { 1, 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Señal")).ToArray();
            var apic = new byte[] { 1 }.Concat(Encoding.ASCII.GetBytes("image/png")).Concat(new byte[] { 0, 3 })
                .Concat(new byte[] { 0xFF, 0xFE }).Concat(Encoding.Unicode.GetBytes("d")).Concat(new byte[] { 0, 0 })
                .Concat(Jpeg).ToArray();
            var t = Id3Tags.Parse(Tag(3, Frame("TIT2", title, 3), Frame("APIC", apic, 3)));
            Assert.AreEqual("Señal", t.Title);
            CollectionAssert.AreEqual(Jpeg, t.Picture);
        }

        [Test]
        public void MalformedOrMissingGivesEmptyTags()
        {
            Assert.IsNull(Id3Tags.Parse(null).Title);
            Assert.IsNull(Id3Tags.Parse(new byte[] { 0xFF, 0xFB, 0, 0 }).Title);
            var truncated = Tag(3, TextFrame("TIT2", "x", 3)).Take(14).ToArray();
            Assert.IsNull(Id3Tags.Parse(truncated).Title);
        }

        [Test]
        public void GenreRuleMatchesTagsPy()
        {
            Assert.AreEqual("Synthwave", Id3Tags.GenreText("Synthwave"));
            Assert.AreEqual("Rock", Id3Tags.GenreText("(17)Rock"));
            Assert.IsNull(Id3Tags.GenreText("(17)"));
            Assert.IsNull(Id3Tags.GenreText("17"));
            Assert.IsNull(Id3Tags.GenreText(""));
        }
    }
}
