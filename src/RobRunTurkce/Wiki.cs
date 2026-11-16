using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RobRunTurkce
{
    /// <summary>
    /// Rehber verisi (wiki/wiki.json): ROB RUN Türkçe Wiki'den üretilmiş eşya, silah, Larry ağacı,
    /// takas, bina ve soygun bilgileri. Exe'ye gömülüdür; GitHub'dan imzalı yeni sürümü gelirse o kullanılır.
    /// </summary>
    public sealed class Wiki
    {
        public string Version = "?", GameVersion;
        public readonly List<WItem> Items = new List<WItem>();
        public readonly List<WWeapon> Weapons = new List<WWeapon>();
        public readonly List<WBranch> Branches = new List<WBranch>();
        public readonly List<WBarter> Barters = new List<WBarter>();
        public readonly List<WBuilding> Buildings = new List<WBuilding>();
        public readonly List<WRobbery> Robberies = new List<WRobbery>();
        public readonly Dictionary<string, WItem> ItemById = new Dictionary<string, WItem>(StringComparer.Ordinal);
        public readonly Dictionary<string, WNode> NodeById = new Dictionary<string, WNode>(StringComparer.Ordinal);

        public static string CachedPath => Path.Combine(Updater.DataRoot, "rehber", "wiki.json");

        public static Wiki LoadEmbedded()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("wiki.json"))
            {
                if (s == null) return new Wiki();
                using (var r = new StreamReader(s, Encoding.UTF8)) return Load(r.ReadToEnd());
            }
        }

        /// <summary>Gömülü veri ile GitHub'dan indirilmiş (doğrulanmış) veriden sürümü yüksek olanı.</summary>
        public static Wiki LoadActive()
        {
            Wiki embedded;
            try { embedded = LoadEmbedded(); } catch { embedded = new Wiki(); }
            try
            {
                if (File.Exists(CachedPath))
                {
                    var cached = Load(File.ReadAllText(CachedPath, Encoding.UTF8));
                    if (Updater.ParseVersion(cached.Version) > Updater.ParseVersion(embedded.Version)) return cached;
                }
            }
            catch { }
            return embedded;
        }

        // ------------------------------------------------------------------ JSON yardımcıları
        static string S(Json.Obj o, string k) => o?[k] as string;
        static double? N(Json.Obj o, string k) => o?[k] is double d ? d : (double?)null;
        static long? L(Json.Obj o, string k) => o?[k] is double d ? (long)d : (long?)null;
        static IEnumerable<Json.Obj> Objs(Json.Obj o, string k) => (o?[k] as List<object>)?.OfType<Json.Obj>() ?? Enumerable.Empty<Json.Obj>();
        /// <summary>Dizi ya da tek metin olabilir (eski dönüştürücü çıktılarıyla uyumlu).</summary>
        static List<string> Strs(Json.Obj o, string k)
        {
            var v = o?[k];
            if (v is List<object> l) return l.OfType<string>().Where(x => x.Length > 0).ToList();
            if (v is string s && s.Length > 0) return new List<string> { s };
            return new List<string>();
        }

        public static Wiki Load(string text)
        {
            var root = (Json.Obj)Json.Parse(text);
            var w = new Wiki();
            if (root["_bilgi"] is Json.Obj meta) { w.Version = S(meta, "surum") ?? "?"; w.GameVersion = S(meta, "oyunSurumu"); }

            foreach (var o in Objs(root, "esyalar"))
            {
                var it = new WItem
                {
                    Id = S(o, "id"), Name = S(o, "ad"), En = S(o, "en"), Category = S(o, "kategori"), Rarity = S(o, "nadirlik"), Color = S(o, "renk"),
                    Icon = S(o, "simge"), Price = L(o, "fiyat"), Desc = S(o, "aciklama"), Where = Strs(o, "nerede"), Obtain = Strs(o, "elde"),
                    Fence = Objs(o, "tefeci").Select(x => ((int)(N(x, "kademe") ?? 0), L(x, "fiyat") ?? 0)).ToList(),
                    Hints = Objs(o, "ipuclari").Select(x => (S(x, "yer"), (int)(N(x, "adet") ?? 0))).ToList(),
                };
                if (it.Id == null || it.Name == null) continue;
                w.Items.Add(it); w.ItemById[it.Id] = it;
            }
            foreach (var o in Objs(root, "silahlar"))
                w.Weapons.Add(new WWeapon
                {
                    Id = S(o, "id"), Name = S(o, "ad"), Family = S(o, "aile"), Mk = S(o, "mk"), Class = S(o, "sinif"), FireMode = S(o, "atis"),
                    Rarity = S(o, "nadirlik"), Color = S(o, "renk"), Icon = S(o, "simge"), Price = L(o, "fiyat"),
                    Damage = N(o, "hasar"), Head = N(o, "kafa"), Rpm = N(o, "rpm"), Mag = N(o, "sarjor"), Range = N(o, "menzil"), Dps = N(o, "dps"),
                    Reload = N(o, "doldurma"), FastReload = N(o, "hizliDoldurma"), Aim = N(o, "nisan"), Draw = N(o, "cekme"), Where = Strs(o, "nerede"),
                });
            foreach (var o in Objs(root, "agac"))
            {
                var b = new WBranch { Id = S(o, "id"), Name = S(o, "ad"), Sub = S(o, "alt"), Art = S(o, "gorsel") };
                foreach (var n in Objs(o, "dugumler"))
                {
                    var node = new WNode
                    {
                        Id = S(n, "id"), Name = S(n, "ad"), En = S(n, "en"), Desc = S(n, "aciklama"), Stage = (int)(N(n, "asama") ?? 0),
                        Requires = Strs(n, "gerek"), Unlocks = Strs(n, "acar"), Research = L(n, "arastirma"), Price = L(n, "fiyat"), Years = (int)(N(n, "yil") ?? 0),
                        Rarity = S(n, "nadirlik"), Color = S(n, "renk"), Icon = S(n, "simge"), ItemId = S(n, "esya"), Branch = b,
                    };
                    b.Nodes.Add(node); if (node.Id != null) w.NodeById[node.Id] = node;
                }
                w.Branches.Add(b);
            }
            foreach (var o in Objs(root, "takaslar"))
                w.Barters.Add(new WBarter
                {
                    Id = S(o, "id"), Name = S(o, "ad"), Icon = S(o, "simge"), ItemId = S(o, "esya"), Npc = S(o, "npc"), Usage = S(o, "kullanim"),
                    Inputs = Objs(o, "girdiler").Select(x => new WInput { Name = S(x, "ad"), Qty = L(x, "adet") ?? 1, Icon = S(x, "simge"), ItemId = S(x, "esya") }).ToList(),
                });
            foreach (var o in Objs(root, "binalar"))
                w.Buildings.Add(new WBuilding { Id = S(o, "id"), Name = S(o, "ad"), En = S(o, "en"), Type = S(o, "tur"), Desc = S(o, "aciklama"), ItemIds = Strs(o, "esyalar") });
            foreach (var o in Objs(root, "soygunlar"))
                w.Robberies.Add(new WRobbery
                {
                    Id = S(o, "id"), Name = S(o, "ad"), Group = S(o, "grup"), Place = S(o, "yer"), Status = S(o, "durum"), StatusType = S(o, "durumTuru"),
                    Icon = S(o, "simge"), Tools = Strs(o, "aletler"), Summary = S(o, "ozet"), Steps = Strs(o, "adimlar"),
                });
            if (w.Items.Count == 0) throw new InvalidDataException("Rehber verisinde eşya yok.");
            return w;
        }

        // ------------------------------------------------------------------ simgeler (exe'ye gömülü)
        static readonly Dictionary<string, ImageSource> iconCache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        public static ImageSource Icon(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (iconCache.TryGetValue(name, out var img)) return img;
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("wiki/" + name))
                {
                    if (s != null)
                    {
                        var b = new BitmapImage();
                        b.BeginInit(); b.StreamSource = s; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
                        img = b;
                    }
                }
            }
            catch { img = null; }
            iconCache[name] = img;
            return img;
        }

        public static string Money(long? v) => v == null ? null : "$" + v.Value.ToString("N0", CultureInfo.GetCultureInfo("en-US"));
    }

    public sealed class WItem
    {
        public string Id, Name, En, Category, Rarity, Color, Icon, Desc;
        public long? Price;
        public List<(int tier, long price)> Fence = new List<(int, long)>();
        public List<(string place, int count)> Hints = new List<(string, int)>();
        public List<string> Where = new List<string>(), Obtain = new List<string>();
    }
    public sealed class WWeapon
    {
        public string Id, Name, Family, Mk, Class, FireMode, Rarity, Color, Icon;
        public long? Price;
        public double? Damage, Head, Rpm, Mag, Range, Dps, Reload, FastReload, Aim, Draw;
        public List<string> Where = new List<string>();
    }
    public sealed class WBranch { public string Id, Name, Sub, Art; public readonly List<WNode> Nodes = new List<WNode>(); }
    public sealed class WNode
    {
        public string Id, Name, En, Desc, Rarity, Color, Icon, ItemId;
        public int Stage, Years;
        public long? Research, Price;
        public List<string> Requires = new List<string>(), Unlocks = new List<string>();
        public WBranch Branch;
    }
    public sealed class WInput { public string Name, Icon, ItemId; public long Qty; }
    public sealed class WBarter { public string Id, Name, Icon, ItemId, Npc, Usage; public List<WInput> Inputs = new List<WInput>(); }
    public sealed class WBuilding { public string Id, Name, En, Type, Desc; public List<string> ItemIds = new List<string>(); }
    public sealed class WRobbery { public string Id, Name, Group, Place, Status, StatusType, Icon, Summary; public List<string> Tools = new List<string>(), Steps = new List<string>(); }
}
