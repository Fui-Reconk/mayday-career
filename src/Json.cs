using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MaydayCareer
{
    /// <summary>
    /// 极简 JSON 读写。刻意不引入依赖：BepInEx 的运行时里没有 System.Text.Json，
    /// 而存档结构是扁平的，手写 200 行比拖一个序列化库更可靠。
    /// </summary>
    internal static class Json
    {
        // ---------- 写 ----------

        public static void Escape(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static string Num(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        // ---------- 读 ----------

        public static object Parse(string text)
        {
            int i = 0;
            var v = ParseValue(text, ref i);
            return v;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of json");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': i += 4; return true;
                case 'f': i += 5; return false;
                case 'n': i += 4; return null;
                default: return ParseNumber(s, ref i);
            }
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                string k = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("expected ':'");
                i++;
                d[k] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                throw new FormatException("expected ',' or '}'");
            }
            return d;
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                throw new FormatException("expected ',' or ']'");
            }
            return list;
        }

        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("expected string");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
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
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: throw new FormatException("bad escape");
                }
            }
            throw new FormatException("unterminated string");
        }

        static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "-+.eE0123456789".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        // ---------- 便捷取值 ----------

        public static int GetInt(Dictionary<string, object> d, string k, int def)
            => d != null && d.TryGetValue(k, out var v) && v is double dv ? (int)Math.Round(dv) : def;

        public static float GetFloat(Dictionary<string, object> d, string k, float def)
            => d != null && d.TryGetValue(k, out var v) && v is double dv ? (float)dv : def;

        public static string GetStr(Dictionary<string, object> d, string k, string def)
            => d != null && d.TryGetValue(k, out var v) && v is string sv ? sv : def;

        public static List<string> GetStrList(Dictionary<string, object> d, string k)
        {
            var outList = new List<string>();
            if (d != null && d.TryGetValue(k, out var v) && v is List<object> arr)
                foreach (var o in arr)
                    if (o is string s) outList.Add(s);
            return outList;
        }

        /// <summary>数字数组，用于 seatRows 这种小整数列表。</summary>
        public static List<int> GetIntList(Dictionary<string, object> d, string k)
        {
            var outList = new List<int>();
            if (d != null && d.TryGetValue(k, out var v) && v is List<object> arr)
                foreach (var o in arr)
                    if (o is double dv) outList.Add((int)Math.Round(dv));
            return outList;
        }

        /// <summary>对象数组，用于 items 这种记录列表。</summary>
        public static List<Dictionary<string, object>> GetObjList(Dictionary<string, object> d, string k)
        {
            var outList = new List<Dictionary<string, object>>();
            if (d != null && d.TryGetValue(k, out var v) && v is List<object> arr)
                foreach (var o in arr)
                    if (o is Dictionary<string, object> od) outList.Add(od);
            return outList;
        }
    }
}
