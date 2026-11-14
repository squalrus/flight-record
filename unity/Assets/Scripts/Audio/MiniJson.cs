using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlightRecord.Audio
{
    /// <summary>
    /// A small strict JSON reader for the message contract. Objects become
    /// <c>Dictionary&lt;string, object&gt;</c>, arrays <c>List&lt;object&gt;</c>, numbers
    /// <c>double</c>, and <c>true</c> / <c>false</c> / <c>null</c> their C# values.
    /// <c>JsonUtility</c> cannot read nulls or unknown shapes, and a package dependency is
    /// not worth it for messages this simple. Safe to use from any thread.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var reader = new Reader(text);
            reader.SkipSpace();
            var value = reader.ReadValue(0);
            reader.SkipSpace();
            if (!reader.AtEnd) throw reader.Error("unexpected text after the value");
            return value;
        }

        sealed class Reader
        {
            const int MaxDepth = 64;
            readonly string _s;
            int _i;

            public Reader(string s) { _s = s; }

            public bool AtEnd => _i >= _s.Length;

            public FormatException Error(string what) =>
                new FormatException($"JSON: {what} at character {_i}");

            public void SkipSpace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r'))
                    _i++;
            }

            char Peek() => _i < _s.Length ? _s[_i] : '\0';

            void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error($"expected '{word}'");
                _i += word.Length;
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Error("nested too deeply");
                switch (Peek())
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        char c = Peek();
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error(AtEnd ? "unexpected end" : $"unexpected '{c}'");
                }
            }

            Dictionary<string, object> ReadObject(int depth)
            {
                var result = new Dictionary<string, object>();
                _i++; // {
                SkipSpace();
                if (Peek() == '}') { _i++; return result; }
                while (true)
                {
                    SkipSpace();
                    if (Peek() != '"') throw Error("expected a key");
                    string key = ReadString();
                    SkipSpace();
                    if (Peek() != ':') throw Error("expected ':'");
                    _i++;
                    SkipSpace();
                    result[key] = ReadValue(depth + 1);
                    SkipSpace();
                    char c = Peek();
                    _i++;
                    if (c == '}') return result;
                    if (c != ',') { _i--; throw Error("expected ',' or '}'"); }
                }
            }

            List<object> ReadArray(int depth)
            {
                var result = new List<object>();
                _i++; // [
                SkipSpace();
                if (Peek() == ']') { _i++; return result; }
                while (true)
                {
                    SkipSpace();
                    result.Add(ReadValue(depth + 1));
                    SkipSpace();
                    char c = Peek();
                    _i++;
                    if (c == ']') return result;
                    if (c != ',') { _i--; throw Error("expected ',' or ']'"); }
                }
            }

            string ReadString()
            {
                _i++; // opening quote
                StringBuilder sb = null;
                int start = _i;
                while (true)
                {
                    if (AtEnd) throw Error("unterminated string");
                    char c = _s[_i];
                    if (c == '"')
                    {
                        string tail = _s.Substring(start, _i - start);
                        _i++;
                        return sb == null ? tail : sb.Append(tail).ToString();
                    }
                    if (c < ' ') throw Error("control character in string");
                    if (c != '\\') { _i++; continue; }

                    sb ??= new StringBuilder();
                    sb.Append(_s, start, _i - start);
                    _i++;
                    if (AtEnd) throw Error("unterminated escape");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Error("short \\u escape");
                            if (!ushort.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out ushort code))
                                throw Error("bad \\u escape");
                            sb.Append((char)code); // surrogate pairs arrive as two escapes
                            _i += 4;
                            break;
                        default: throw Error($"bad escape '\\{e}'");
                    }
                    start = _i;
                }
            }

            double ReadNumber()
            {
                int start = _i;
                if (Peek() == '-') _i++;
                while (!AtEnd && "0123456789+-.eE".IndexOf(_s[_i]) >= 0) _i++;
                string token = _s.Substring(start, _i - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    || double.IsNaN(value) || double.IsInfinity(value))
                    throw Error($"bad number '{token}'");
                return value;
            }
        }
    }
}
