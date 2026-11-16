using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace RobRunTurkce
{
    public sealed class Entry
    {
        public string Tr;
        public string Hash; // çevrilirken kullanılan İngilizce metnin FNV-1a özeti
    }

    /// <summary>translations/tr.json dosyasını okur ve çevirilerin oyun biçimini bozmadığını denetler.</summary>
    public sealed class Translations
    {
        // tablo adı (ör. "StaticStrings") -> anahtar -> çeviri
        public readonly Dictionary<string, Dictionary<string, Entry>> Tables = new Dictionary<string, Dictionary<string, Entry>>();
        public string Version = "?";

        public static Translations LoadEmbedded()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("tr.json"))
            using (var r = new StreamReader(s, Encoding.UTF8))
                return Load(r.ReadToEnd());
        }

        public static Translations LoadFile(string path) => Load(File.ReadAllText(path, Encoding.UTF8));

        /// <summary>Gömülü çeviri ile GitHub'dan indirilmiş (doğrulanmış) çeviriden sürümü yüksek olanı.</summary>
        public static Translations LoadActive()
        {
            var embedded = LoadEmbedded();
            try
            {
                if (File.Exists(Updater.CachedTranslation))
                {
                    var cached = LoadFile(Updater.CachedTranslation);
                    if (Updater.ParseVersion(cached.Version) > Updater.ParseVersion(embedded.Version)) return cached;
                }
            }
            catch { } // bozuk önbellek: gömülü çeviri kullanılır
            return embedded;
        }

        public static Translations Load(string text)
        {
            var t = new Translations();
            var root = (Json.Obj)Json.Parse(text);
            foreach (var table in root)
            {
                if (table.Key.StartsWith("_"))
                {
                    if (table.Value is Json.Obj meta && meta["surum"] is string v) t.Version = v;
                    continue;
                }
                var d = new Dictionary<string, Entry>(StringComparer.Ordinal);
                foreach (var kv in (Json.Obj)table.Value)
                {
                    var o = (Json.Obj)kv.Value;
                    var tr = o["tr"] as string;
                    if (string.IsNullOrEmpty(tr)) continue; // boş = henüz çevrilmedi, İngilizce kalır
                    d[kv.Key] = new Entry { Tr = tr, Hash = o["h"] as string };
                }
                t.Tables[table.Key] = d;
            }
            return t;
        }

        /// <summary>Unity tablo adından ("StaticStrings_en-US") dil ekini atar.</summary>
        public static string BaseTableName(string unityName)
        {
            int i = unityName.LastIndexOf('_');
            return i > 0 ? unityName.Substring(0, i) : unityName;
        }

        public static string Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h.ToString("x8");
        }

        static readonly Regex Tag = new Regex(@"<[^<>]+>");
        static readonly Regex ArgRef = new Regex(@"\{(\d+)");
        static readonly Regex Palette = new Regex(@"\{:palette\(\w+\)\}");

        /// <summary>
        /// Çevirinin, oyunun metin biçimlendirmesini (renk etiketleri, {0} yer tutucuları,
        /// SmartFormat ifadeleri) bozmadığını denetler. Sorun varsa açıklama döner, yoksa null.
        /// </summary>
        public static string Check(string en, string tr)
        {
            if (Count(en, '{') != Count(tr, '{') || Count(en, '}') != Count(tr, '}'))
                return "süslü parantez sayısı farklı";
            if (!SameMultiset(Tag.Matches(en), Tag.Matches(tr), true))
                return "biçim etiketleri (<color> vb.) farklı";
            if (!SameSet(ArgRef.Matches(en), ArgRef.Matches(tr)))
                return "yer tutucular ({0} vb.) farklı";
            if (!SameMultiset(Palette.Matches(en), Palette.Matches(tr), false))
                return "renk paleti ifadeleri farklı";
            if (Count(en, '|') != Count(tr, '|'))
                return "'|' ayırıcı sayısı farklı";
            return null;
        }

        static int Count(string s, char c) { int n = 0; foreach (var x in s) if (x == c) n++; return n; }

        static bool SameSet(MatchCollection a, MatchCollection b)
            => new HashSet<string>(a.Cast<Match>().Select(m => m.Value)).SetEquals(b.Cast<Match>().Select(m => m.Value));

        static bool SameMultiset(MatchCollection a, MatchCollection b, bool tagsByName)
        {
            string N(string v) => tagsByName ? Regex.Replace(v.ToLowerInvariant(), @"^<(/?[a-z\-]+).*$", "<$1>") : v;
            var x = a.Cast<Match>().Select(m => N(m.Value)).OrderBy(v => v, StringComparer.Ordinal);
            var y = b.Cast<Match>().Select(m => N(m.Value)).OrderBy(v => v, StringComparer.Ordinal);
            return x.SequenceEqual(y);
        }

        /// <summary>Font yaması uygulanamazsa kullanılır: yazı tipinde olmayan harfleri en yakın harfe çevirir.</summary>
        public static string Transliterate(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case 'ı': sb.Append('i'); break;
                    case 'İ': sb.Append('I'); break;
                    case 'ş': sb.Append('s'); break;
                    case 'Ş': sb.Append('S'); break;
                    case 'ğ': sb.Append('g'); break;
                    case 'Ğ': sb.Append('G'); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
