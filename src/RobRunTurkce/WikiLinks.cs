using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace RobRunTurkce
{
    /// <summary>
    /// Rehber metinlerindeki adları (eşya, soygun, bina, harita noktası, cihaz, sevkiyat düğümü) tanır;
    /// metin içi bağlantılar ve "Nerede kullanılır" listeleri bundan üretilir.
    /// </summary>
    public sealed class WikiLinks
    {
        readonly Dictionary<string, object> byName = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // adın yazılışı
        readonly Regex rx;
        readonly Dictionary<object, HashSet<object>> mentionedBy = new Dictionary<object, HashSet<object>>();

        // Tek başına bağlantı olmaması gereken genel sözcükler
        static readonly HashSet<string> Stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "kasa", "kargo", "para", "anahtar", "kart", "çanta", "kutu", "dolap", "bina", "araba", "otel", "market", "nakit para", "cash",
            "safe", "cargo", "key", "bag", "box", "car", "hotel", "door", "kapı", "plan", "laptop", "dizüstü", "tablet", "kamera", "camera",
            "lenny", "larry", "elektronik", "electronics", // NPC adları ve dükkân türleri (verideki aynı adlı kayıtlara bağlanmasın)
        };

        public WikiLinks(Wiki w)
        {
            void Add(string name, object target)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                name = name.Trim();
                if (name.Length < 4 || Stop.Contains(name)) return;
                if (byName.TryGetValue(name, out var existing) && !ReferenceEquals(existing, target))
                {
                    // aynı ad birden çok şeye çıkıyorsa: eşya > cihaz > düğüm > diğerleri (kademelilerde en düşük kademe)
                    if (Rank(existing) <= Rank(target)) return;
                }
                byName[name] = target; canonical[name] = name;
            }
            int Rank(object o) => o is WItem ? 0 : o is WDevice ? 1 : o is WNode ? 2 : o is WRobbery ? 3 : o is WPoi ? 4 : 5;

            foreach (var i in w.Items)
            {
                // istatistik/NPC/yapı yuvası kayıtları metinde bağlantı olmasın
                if (i.Id.StartsWith("defense_block") || i.Id.StartsWith("builder_") || i.Id == "barter_trader" || (i.Category ?? "").Contains("NPC")) continue;
                // tek sözcüklü adlar yalnızca gerçek (fiyatı olan) eşyalarsa bağlantı olur
                bool real = i.Price > 0;
                if (real || (i.Name ?? "").Contains(" ")) Add(i.Name, i);
                if (real || (i.En ?? "").Contains(" ")) Add(i.En, i);
                var baseName = Regex.Replace(i.Name ?? "", @"\s*\(Kademe \d+\)$", "");
                if (baseName != i.Name && !byName.ContainsKey(baseName)) Add(baseName, i); // "Şifre Çözücü" → ilk kademe
            }
            if (w.Guides != null) foreach (var d in w.Guides.Devices) if (!w.ItemById.ContainsKey(d.Id ?? "")) Add(d.Name, d);
            foreach (var n in w.NodeById.Values) if (n.ItemId == null) { Add(n.Name, n); Add(n.En, n); }
            foreach (var r in w.Robberies) Add(r.Name, r);
            if (w.Map != null) foreach (var p in w.Map.Pois) { Add(p.Name, p); Add(p.En, p); }
            foreach (var b in w.Buildings) if (w.Map == null || !w.Map.Pois.Any(p => p.Name == b.Name)) { Add(b.Name, b); Add(b.En, b); }

            var names = byName.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape);
            rx = new Regex(@"(?<![\p{L}\p{N}])(" + string.Join("|", names) + @")(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

            // kim kimi anıyor (Nerede kullanılır)
            void Index(object owner, params string[] texts)
            {
                foreach (var t in texts) foreach (var target in Find(t).Select(m => m.target)) Mention(target, owner);
            }
            foreach (var r in w.Robberies) Index(r, new[] { r.Summary }.Concat(r.Tools).Concat(r.Steps).ToArray());
            foreach (var b in w.Barters)
            {
                foreach (var inp in b.Inputs) if (inp.ItemId != null && w.ItemById.TryGetValue(inp.ItemId, out var it)) Mention(it, b);
                Index(b, b.Usage);
            }
            if (w.Guides != null)
            {
                foreach (var s in w.Guides.Steps) Index(s, new[] { s.Title, s.Text }.Concat(s.Points).Concat(s.Tips).ToArray());
                foreach (var m in w.Guides.Money) Index(m, m.Title, m.Needs, m.How);
                foreach (var d in w.Guides.Devices) Index(d, d.HowToFill, d.WhenFull, string.Join(" ", d.Barters));
            }
            if (w.Map != null)
                foreach (var p in w.Map.Pois)
                {
                    foreach (var k in p.Keys) foreach (var r in new[] { k.Drill, k.Usb }) if (r?.ItemId != null && w.ItemById.TryGetValue(r.ItemId, out var it)) Mention(it, p);
                    foreach (var j in p.Jobs) foreach (var r in j.Need) if (r.ItemId != null && w.ItemById.TryGetValue(r.ItemId, out var it)) Mention(it, p);
                    Index(p, p.Desc);
                }
        }

        void Mention(object target, object owner)
        {
            if (ReferenceEquals(target, owner)) return;
            if (!mentionedBy.TryGetValue(target, out var set)) mentionedBy[target] = set = new HashSet<object>();
            set.Add(owner);
        }

        /// <summary>Bu şeyden söz eden kayıtlar (soygun, takas, rehber adımı, harita noktası…).</summary>
        public IEnumerable<object> MentionedBy(object target) => mentionedBy.TryGetValue(target, out var s) ? s : Enumerable.Empty<object>();

        /// <summary>Metindeki tanınan adlar (çakışmasız, soldan sağa).</summary>
        public List<(int index, int length, object target)> Find(string text)
        {
            var list = new List<(int, int, object)>();
            if (string.IsNullOrEmpty(text) || byName.Count == 0) return list;
            foreach (Match m in rx.Matches(text))
                if (byName.TryGetValue(m.Value, out var t))
                {
                    // tek sözcüklü adlarda büyük/küçük harf birebir tutmalı ("kırma" ≠ "Kırma")
                    if (!m.Value.Contains(" ") && canonical.TryGetValue(m.Value, out var c) && !string.Equals(c, m.Value, StringComparison.Ordinal)) continue;
                    list.Add((m.Index, m.Length, t));
                }
            return list;
        }

        public object Resolve(string name) => name != null && byName.TryGetValue(name.Trim(), out var t) ? t : null;
    }
}
