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
        public WMap Map;        // Looty Bay haritası (yoksa null)
        public WGuides Guides;  // başlangıç / birikimli rehberler (yoksa null)

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
            if (root["harita"] is Json.Obj h) w.Map = LoadMap(h);
            if (root["rehberler"] is Json.Obj g) w.Guides = LoadGuides(g);
            if (w.Items.Count == 0) throw new InvalidDataException("Rehber verisinde eşya yok.");
            return w;
        }

        static WRef Ref(Json.Obj x) => x == null ? null : new WRef { Id = S(x, "id"), Name = S(x, "ad"), Icon = S(x, "simge"), ItemId = S(x, "esya"), Count = (int)(N(x, "adet") ?? N(x, "puan") ?? 0) };
        static List<WRef> Refs(Json.Obj o, string k) => Objs(o, k).Select(Ref).Where(r => r?.Name != null).ToList();

        static WMap LoadMap(Json.Obj h)
        {
            var m = new WMap { Image = S(h, "gorsel"), Width = N(h, "genislik") ?? 0, Height = N(h, "yukseklik") ?? 0 };
            foreach (var d in Objs(h, "bolgeler")) m.Districts.Add((S(d, "ad"), N(d, "x") ?? 0, N(d, "y") ?? 0));
            foreach (var g in Objs(h, "gruplar"))
                m.Groups.Add(new WMapGroup { Id = S(g, "id"), Name = S(g, "ad"), Kinds = Objs(g, "turler").Select(k => (S(k, "id"), S(k, "ad"))).ToList() });
            foreach (var l in Objs(h, "katmanlar")) m.Layers.Add(new WMapLayer { Id = S(l, "id"), Name = S(l, "ad"), Color = S(l, "renk") });
            foreach (var p in Objs(h, "noktalar"))
                m.Pois.Add(new WPoi
                {
                    Id = S(p, "id"), Kind = S(p, "tur"), Group = S(p, "grup"), KindName = S(p, "turAd"), Name = S(p, "ad"), En = S(p, "en"), Desc = S(p, "aciklama"),
                    Special = S(p, "ozel"), X = N(p, "x") ?? 0, Y = N(p, "y") ?? 0, Loot = Refs(p, "ganimet"), Refills = Refs(p, "sarj"),
                    Keys = Objs(p, "anahtarlar").Select(k => new WKey
                    {
                        Name = S(k, "ad"), Opens = S(k, "acar"), Safes = (int)(N(k, "kasa") ?? 0), Cabinets = (int)(N(k, "dolap") ?? 0), Boxes = (int)(N(k, "kutu") ?? 0),
                        Computers = (int)(N(k, "bilgisayar") ?? 0), Drill = Ref(k["matkap"] as Json.Obj), Usb = Ref(k["usb"] as Json.Obj), Inside = Refs(k, "icerik"),
                    }).ToList(),
                    Jobs = Objs(p, "gorevler").Select(j => new WJob { Name = S(j, "ad"), Need = Refs(j, "gerek"), Result = Ref(j["sonuc"] as Json.Obj), Steps = Strs(j, "adimlar"), Note = S(j, "not") }).ToList(),
                });
            foreach (var o in Objs(h, "nesneler"))
                m.Objects.Add(new WObj
                {
                    Layer = S(o, "s"), X = N(o, "x") ?? 0, Y = N(o, "y") ?? 0, Name = S(o, "ad"), Building = S(o, "bina"), Region = S(o, "bolge"),
                    Loot = Objs(o, "g").Select(x => (S(x, "id"), (int)(N(x, "min") ?? 0), (int)(N(x, "max") ?? 0))).Where(x => x.Item1 != null).ToList(),
                });
            return m;
        }

        static WGuides LoadGuides(Json.Obj g)
        {
            var r = new WGuides();
            foreach (var s in Objs(g, "adimlar"))
            {
                var a = s["eylem"] as Json.Obj;
                r.Steps.Add(new WStep { No = S(s, "no"), Badge = S(s, "rozet"), Title = S(s, "baslik"), Text = S(s, "metin"), Points = Strs(s, "maddeler"), Tips = Strs(s, "ipuclari"), ActionType = S(a, "tur"), ActionTarget = S(a, "hedef"), ActionLabel = S(a, "etiket") });
            }
            foreach (var s in Objs(g, "para"))
                r.Money.Add(new WMoney { Rank = S(s, "sira"), Title = S(s, "baslik"), Difficulty = S(s, "zorluk"), Time = S(s, "sure"), Payout = S(s, "kazanc"), Needs = S(s, "gerekenler"), How = S(s, "nasil"), Icon = S(s, "simge") });
            foreach (var s in Objs(g, "hatalar")) r.Mistakes.Add((S(s, "no"), S(s, "baslik"), S(s, "metin")));
            foreach (var s in Objs(g, "cihazlar"))
                r.Devices.Add(new WDevice
                {
                    Id = S(s, "id"), Name = S(s, "ad"), Badge = S(s, "rozet"), Color = S(s, "renk"), Points = (int)(N(s, "puan") ?? 0), Research = L(s, "arastirma"), Price = L(s, "fiyat"),
                    Prereq = S(s, "onsart"), HowToFill = S(s, "doldurma"), WhenFull = S(s, "dolunca"), Barters = Strs(s, "takas"), Icon = S(s, "simge"), ItemId = S(s, "esya"),
                });
            r.StarterPack = Strs(g, "baslangicPaketi");
            foreach (var s in Objs(g, "yerlesim"))
                r.Base.Add(new WArticle
                {
                    Title = S(s, "baslik"), Badge = S(s, "rozet"), Paras = Strs(s, "paragraflar"), Points = Strs(s, "maddeler"), Related = Strs(s, "ilgili"), Branch = S(s, "dal"),
                    Costs = Objs(s, "maliyet").Select(c => (S(c, "id"), S(c, "ad"), L(c, "arastirma"), L(c, "fiyat"))).ToList(),
                });
            return r;
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

    /// <summary>Eşya/simge başvurusu (harita ve rehberlerde). Count: adet ya da şarj puanı.</summary>
    public sealed class WRef { public string Id, Name, Icon, ItemId; public int Count; }
    public sealed class WMap
    {
        public string Image; public double Width, Height;
        public readonly List<(string name, double x, double y)> Districts = new List<(string, double, double)>();
        public readonly List<WMapGroup> Groups = new List<WMapGroup>();
        public readonly List<WMapLayer> Layers = new List<WMapLayer>();
        public readonly List<WPoi> Pois = new List<WPoi>();
        public readonly List<WObj> Objects = new List<WObj>();
    }
    public sealed class WMapGroup { public string Id, Name; public List<(string id, string name)> Kinds = new List<(string, string)>(); }
    public sealed class WMapLayer { public string Id, Name, Color; }
    public sealed class WPoi
    {
        public string Id, Kind, Group, KindName, Name, En, Desc, Special; public double X, Y;
        public List<WRef> Loot = new List<WRef>(), Refills = new List<WRef>();
        public List<WKey> Keys = new List<WKey>(); public List<WJob> Jobs = new List<WJob>();
    }
    public sealed class WKey { public string Name, Opens; public int Safes, Cabinets, Boxes, Computers; public WRef Drill, Usb; public List<WRef> Inside = new List<WRef>(); }
    public sealed class WJob { public string Name, Note; public List<WRef> Need = new List<WRef>(); public WRef Result; public List<string> Steps = new List<string>(); }
    public sealed class WObj { public string Layer, Name, Building, Region; public double X, Y; public List<(string id, int min, int max)> Loot = new List<(string, int, int)>(); }

    public sealed class WGuides
    {
        public readonly List<WStep> Steps = new List<WStep>();
        public readonly List<WMoney> Money = new List<WMoney>();
        public readonly List<(string no, string title, string text)> Mistakes = new List<(string, string, string)>();
        public readonly List<WDevice> Devices = new List<WDevice>();
        public List<string> StarterPack = new List<string>();
        public readonly List<WArticle> Base = new List<WArticle>(); // Yerleşim ve Üs yazıları
    }
    /// <summary>Yerleşim ve Üs rehberindeki bir yazı: oyunun kendi açıklamaları, özet maddeler, bedeller, ilgili kayıtlar.</summary>
    public sealed class WArticle
    {
        public string Title, Badge, Branch;
        public List<string> Paras = new List<string>(), Points = new List<string>(), Related = new List<string>();
        public List<(string id, string name, long? research, long? price)> Costs = new List<(string, string, long?, long?)>();
    }
    public sealed class WStep { public string No, Badge, Title, Text, ActionType, ActionTarget, ActionLabel; public List<string> Points = new List<string>(), Tips = new List<string>(); }
    public sealed class WMoney { public string Rank, Title, Difficulty, Time, Payout, Needs, How, Icon; }
    public sealed class WDevice { public string Id, Name, Badge, Color, Prereq, HowToFill, WhenFull, Icon, ItemId; public int Points; public long? Research, Price; public List<string> Barters = new List<string>(); }
}
