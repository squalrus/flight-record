using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FlightRecord.Audio
{
    /// <summary>
    /// Title, artist, album, genre, track number, and cover art from an MP3's ID3v2.3/2.4 tag,
    /// so a file the game plays can show what it is without the sidecar or identification
    /// (D-012). Mirrors <c>flightrec/tags.py</c> (same filename fallback and genre rule) and
    /// adds the attached picture. Anything missing is null.
    /// </summary>
    public sealed class Id3Tags
    {
        public string Title, Artist, Album, Genre, TrackNumber;

        /// <summary>The cover picture's encoded bytes (JPEG or PNG), or null.</summary>
        public byte[] Picture;
        public string PictureMime;

        /// <summary>Reads the tag at the start of <paramref name="path"/>; empty tags if none or unreadable.</summary>
        public static Id3Tags Read(string path)
        {
            Id3Tags tags;
            try
            {
                tags = Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                    ? Parse(ReadTagBytes(path)) : new Id3Tags();
            }
            catch (IOException) { tags = new Id3Tags(); }
            catch (UnauthorizedAccessException) { tags = new Id3Tags(); }
            tags.FillFromFileName(Path.GetFileNameWithoutExtension(path));
            return tags;
        }

        static byte[] ReadTagBytes(string path)
        {
            using var fs = File.OpenRead(path);
            var head = new byte[10];
            if (fs.Read(head, 0, 10) < 10 || head[0] != 'I' || head[1] != 'D' || head[2] != '3') return null;
            int size = Syncsafe(head, 6);
            var all = new byte[10 + size];
            Array.Copy(head, all, 10);
            int read = 0;
            while (read < size)
            {
                int n = fs.Read(all, 10 + read, size - read);
                if (n <= 0) break;
                read += n;
            }
            return all;
        }

        /// <summary>Parses a whole ID3v2 tag (header included); null or anything malformed gives empty tags.</summary>
        public static Id3Tags Parse(byte[] tag)
        {
            var t = new Id3Tags();
            if (tag == null || tag.Length < 10 || tag[0] != 'I' || tag[1] != 'D' || tag[2] != '3') return t;
            int version = tag[3];
            if (version != 3 && version != 4) return t; // v2.2 uses three-letter frames; rare today
            int end = Math.Min(tag.Length, 10 + Syncsafe(tag, 6));
            int i = 10;
            if ((tag[5] & 0x40) != 0 && i + 4 <= end) // extended header: skip it
                i += version == 4 ? Syncsafe(tag, i) : 4 + BigEndian(tag, i);

            while (i + 10 <= end)
            {
                string id = Encoding.ASCII.GetString(tag, i, 4);
                if (!IsFrameId(id)) break;
                int size = version == 4 ? Syncsafe(tag, i + 4) : BigEndian(tag, i + 4);
                int body = i + 10;
                if (size <= 0 || body + size > end) break;
                switch (id)
                {
                    case "TIT2": t.Title ??= Text(tag, body, size); break;
                    case "TPE1": t.Artist ??= Text(tag, body, size); break;
                    case "TALB": t.Album ??= Text(tag, body, size); break;
                    case "TCON": t.Genre ??= GenreText(Text(tag, body, size)); break;
                    case "TRCK": t.TrackNumber ??= Text(tag, body, size); break;
                    case "APIC": if (t.Picture == null) ReadPicture(tag, body, size, t); break;
                }
                i = body + size;
            }
            return t;
        }

        /// <summary>"(17)Rock" and "Rock" give "Rock"; a bare ID3v1 number gives null (as tags.py).</summary>
        public static string GenreText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string text = Regex.Replace(value.Trim(), @"^(\(\d+\))+", "").Trim();
            return text.Length == 0 || Regex.IsMatch(text, @"^\d+$") ? null : text;
        }

        /// <summary>The "Artist - Album - NN Title" pattern many stores use, when the tag lacks artist or title.</summary>
        void FillFromFileName(string stem)
        {
            if (Artist != null && Title != null) return;
            var parts = stem.Split(new[] { " - " }, StringSplitOptions.None);
            if (parts.Length < 2)
            {
                Title ??= stem;
                return;
            }
            Artist ??= parts[0].Trim();
            Title ??= Regex.Replace(parts[parts.Length - 1], @"^\d+\s+", "").Trim();
            if (parts.Length >= 3) Album ??= parts[1].Trim();
        }

        static void ReadPicture(byte[] tag, int body, int size, Id3Tags t)
        {
            int end = body + size, i = body;
            int encoding = tag[i++];
            int mimeEnd = Array.IndexOf(tag, (byte)0, i, end - i);
            if (mimeEnd < 0) return;
            string mime = Encoding.ASCII.GetString(tag, i, mimeEnd - i);
            i = mimeEnd + 1 + 1; // the terminator, then the picture type
            i = SkipEncodedString(tag, i, end, encoding); // the description
            if (i >= end) return;
            var data = new byte[end - i];
            Array.Copy(tag, i, data, 0, data.Length);
            t.Picture = data;
            t.PictureMime = mime.Length > 0 ? mime : "image/jpeg";
        }

        static int SkipEncodedString(byte[] b, int i, int end, int encoding)
        {
            bool wide = encoding == 1 || encoding == 2;
            if (!wide)
            {
                int z = Array.IndexOf(b, (byte)0, i, Math.Max(0, end - i));
                return z < 0 ? end : z + 1;
            }
            for (; i + 1 < end; i += 2)
                if (b[i] == 0 && b[i + 1] == 0) return i + 2;
            return end;
        }

        static string Text(byte[] b, int body, int size)
        {
            if (size < 2) return null;
            Encoding enc;
            switch (b[body])
            {
                case 1: enc = Encoding.Unicode; break; // UTF-16 with BOM; GetString honours neither, so strip below
                case 2: enc = Encoding.BigEndianUnicode; break;
                case 3: enc = Encoding.UTF8; break;
                default: enc = Encoding.GetEncoding("ISO-8859-1"); break;
            }
            int start = body + 1, count = size - 1;
            if (b[body] == 1 && count >= 2)
            {
                if (b[start] == 0xFE && b[start + 1] == 0xFF) enc = Encoding.BigEndianUnicode;
                if ((b[start] == 0xFF && b[start + 1] == 0xFE) || (b[start] == 0xFE && b[start + 1] == 0xFF)) { start += 2; count -= 2; }
            }
            string s = enc.GetString(b, start, count);
            int nul = s.IndexOf('\0');
            if (nul >= 0) s = s.Substring(0, nul); // the first of several values
            s = s.Trim();
            return s.Length > 0 ? s : null;
        }

        static bool IsFrameId(string id)
        {
            foreach (char c in id)
                if (!(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9')) return false;
            return true;
        }

        static int Syncsafe(byte[] b, int i) =>
            (b[i] & 0x7F) << 21 | (b[i + 1] & 0x7F) << 14 | (b[i + 2] & 0x7F) << 7 | (b[i + 3] & 0x7F);

        static int BigEndian(byte[] b, int i) => b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3];
    }
}
