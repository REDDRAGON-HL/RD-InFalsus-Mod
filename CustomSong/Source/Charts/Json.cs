using System;
using System.Collections.Generic;
using System.Globalization;

namespace CustomSong
{
    internal static class Json
    {
        internal static object Parse(string text, out string reason)
        {
            reason = null;
            int at = 0;

            try
            {
                object value = Value(text, ref at);
                Space(text, ref at);
                if (at != text.Length) throw new FormatException("trailing text");
                return value;
            }
            catch (Exception e)
            {
                reason = $"at {at}: {e.Message}";
                return null;
            }
        }

        internal static Dictionary<string, object> Object(object value) =>
            value as Dictionary<string, object>;
        internal static List<object> Array(object value) => value as List<object>;

        internal static double Number(Dictionary<string, object> at, string key, double fallback = 0.0) =>
            at != null && at.TryGetValue(key, out object value) && value is double number ? number : fallback;

        internal static bool Has(Dictionary<string, object> at, string key) =>
            at != null && at.ContainsKey(key);

        internal static object Field(Dictionary<string, object> at, string key) =>
            at != null && at.TryGetValue(key, out object value) ? value : null;

        internal static double Scalar(object value) => value switch
        {
            double number => number,
            string text => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                ? d
                : 0.0,
            _ => 0.0,
        };

        private static object Value(string text, ref int at)
        {
            Space(text, ref at);
            if (at >= text.Length) throw new FormatException("unexpected end");
            char c = text[at];
            switch (c)
            {
                case '{': return Map(text, ref at);
                case '[': return List(text, ref at);
                case '"': return String(text, ref at);
                case 't': Word(text, ref at, "true"); return true;
                case 'f': Word(text, ref at, "false"); return false;
                case 'n': Word(text, ref at, "null"); return null;
                default: return Number(text, ref at);
            }
        }

        private static Dictionary<string, object> Map(string text, ref int at)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            at++;
            Space(text, ref at);

            if (at < text.Length && text[at] == '}')
            {
                at++;
                return map;
            }

            while (true)
            {
                Space(text, ref at);
                string key = String(text, ref at);
                Space(text, ref at);
                if (at >= text.Length || text[at] != ':') throw new FormatException("expected ':'");
                at++;
                map[key] = Value(text, ref at);
                Space(text, ref at);
                if (at >= text.Length) throw new FormatException("unterminated object");
                if (text[at] == ',') { at++; continue; }
                if (text[at] == '}') { at++; return map; }
                throw new FormatException("expected ',' or '}'");
            }
        }

        private static List<object> List(string text, ref int at)
        {
            var list = new List<object>();
            at++;
            Space(text, ref at);

            if (at < text.Length && text[at] == ']')
            {
                at++;
                return list;
            }

            while (true)
            {
                list.Add(Value(text, ref at));
                Space(text, ref at);
                if (at >= text.Length) throw new FormatException("unterminated array");
                if (text[at] == ',') { at++; continue; }
                if (text[at] == ']') { at++; return list; }
                throw new FormatException("expected ',' or ']'");
            }
        }

        private static string String(string text, ref int at)
        {
            if (at >= text.Length || text[at] != '"') throw new FormatException("expected a string");
            at++;
            var built = new System.Text.StringBuilder();
            while (true)
            {
                if (at >= text.Length) throw new FormatException("unterminated string");
                char c = text[at++];
                if (c == '"') return built.ToString();
                if (c != '\\') { built.Append(c); continue; }

                if (at >= text.Length) throw new FormatException("unterminated escape");
                char e = text[at++];
                switch (e)
                {
                    case 'n': built.Append('\n'); break;
                    case 't': built.Append('\t'); break;
                    case 'r': built.Append('\r'); break;
                    case 'b': built.Append('\b'); break;
                    case 'f': built.Append('\f'); break;
                    case 'u':
                        if (at + 4 > text.Length) throw new FormatException("bad \\u escape");
                        built.Append((char)int.Parse(text.Substring(at, 4), NumberStyles.HexNumber,
                                                     CultureInfo.InvariantCulture));
                        at += 4;
                        break;
                    default: built.Append(e); break;
                }
            }
        }

        private static double Number(string text, ref int at)
        {
            int start = at;
            while (at < text.Length && (char.IsDigit(text[at]) || "+-.eE".IndexOf(text[at]) >= 0)) at++;
            if (at == start) throw new FormatException("expected a number");

            return double.Parse(text.Substring(start, at - start), NumberStyles.Float,
                                CultureInfo.InvariantCulture);
        }

        private static void Word(string text, ref int at, string word)
        {
            if (string.CompareOrdinal(text, at, word, 0, word.Length) != 0)
                throw new FormatException("expected " + word);
            at += word.Length;
        }

        private static void Space(string text, ref int at)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
        }
    }
}
