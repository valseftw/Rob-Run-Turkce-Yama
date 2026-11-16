using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RobRunTurkce
{
    /// <summary>
    /// Küçük, bağımlılıksız JSON okuyucu/yazıcı. Sadece bu projenin dosyaları için yeterlidir
    /// (nesne, dizi, metin, sayı, true/false/null). Nesneler sıralı (ekleme sırası korunur).
    /// </summary>
    public static class Json
    {
        public sealed class Obj : List<KeyValuePair<string, object>>
        {
            public object this[string key]
            {
                get { foreach (var kv in this) if (kv.Key == key) return kv.Value; return null; }
                set
                {
                    for (int i = 0; i < Count; i++)
                        if (this[i].Key == key) { this[i] = new KeyValuePair<string, object>(key, value); return; }
                    Add(new KeyValuePair<string, object>(key, value));
                }
            }
        }

        public static object Parse(string s)
        {
            int i = 0;
            var v = ReadValue(s, ref i);
            SkipWs(s, ref i);
            if (i != s.Length) throw Err(s, i, "fazladan karakter");
            return v;
        }

        static Exception Err(string s, int i, string msg)
        {
            int line = 1; for (int k = 0; k < i && k < s.Length; k++) if (s[k] == '\n') line++;
            return new FormatException($"JSON hatası ({line}. satır): {msg}");
        }

        static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object ReadValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw Err(s, i, "beklenmeyen dosya sonu");
            char c = s[i];
            if (c == '{')
            {
                var o = new Obj(); i++; SkipWs(s, ref i);
                if (s[i] == '}') { i++; return o; }
                while (true)
                {
                    SkipWs(s, ref i);
                    if (s[i] != '"') throw Err(s, i, "anahtar bekleniyordu");
                    var k = ReadString(s, ref i); SkipWs(s, ref i);
                    if (s[i] != ':') throw Err(s, i, "':' bekleniyordu");
                    i++;
                    o.Add(new KeyValuePair<string, object>(k, ReadValue(s, ref i)));
                    SkipWs(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return o; }
                    throw Err(s, i, "',' veya '}' bekleniyordu");
                }
            }
            if (c == '[')
            {
                var a = new List<object>(); i++; SkipWs(s, ref i);
                if (s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(ReadValue(s, ref i)); SkipWs(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return a; }
                    throw Err(s, i, "',' veya ']' bekleniyordu");
                }
            }
            if (c == '"') return ReadString(s, ref i);
            if (s.Length - i >= 4 && s.Substring(i, 4) == "true") { i += 4; return true; }
            if (s.Length - i >= 5 && s.Substring(i, 5) == "false") { i += 5; return false; }
            if (s.Length - i >= 4 && s.Substring(i, 4) == "null") { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw Err(s, i, "geçersiz değer");
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }

        static string ReadString(string s, ref int i)
        {
            i++; var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw Err(s, i, "kapanmamış metin");
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
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: throw Err(s, i, "geçersiz kaçış dizisi");
                }
            }
        }

        public static string Quote(string v)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in v)
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
            return sb.Append('"').ToString();
        }
    }
}
