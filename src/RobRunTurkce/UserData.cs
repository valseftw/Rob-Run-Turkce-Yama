using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RobRunTurkce
{
    /// <summary>Haritadaki kişisel not (işaret).</summary>
    public sealed class MapNote { public double X, Y; public string Text, Color = "#FF375F"; }

    /// <summary>Haritadaki rota durağı: bir bina/nokta (PoiId) ya da serbest konum.</summary>
    public sealed class RouteStop { public string PoiId, Label; public double X, Y; }

    /// <summary>
    /// Kullanıcının kişisel rehber verisi: sevkiyat planı ve açılanlar, soygun hazırlık listeleri,
    /// harita notları ve rota. %LOCALAPPDATA%\RobRunTurkceYama\kullanici.json dosyasında saklanır;
    /// program kapanınca kaybolmaz. İnternete gönderilmez.
    /// </summary>
    public sealed class UserData
    {
        public readonly HashSet<string> Plan = new HashSet<string>(StringComparer.Ordinal);   // planlanan düğümler
        public readonly HashSet<string> Done = new HashSet<string>(StringComparer.Ordinal);   // açılmış düğümler
        public readonly Dictionary<string, HashSet<string>> Prep = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal); // soygun → hazır eşyalar
        public readonly List<MapNote> Notes = new List<MapNote>();
        public readonly List<RouteStop> Route = new List<RouteStop>();
        public event Action Changed;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string FilePath => Path.Combine(Updater.DataRoot, "kullanici.json");

        public static UserData Load()
        {
            var d = new UserData();
            try
            {
                if (!File.Exists(FilePath)) return d;
                var o = (Json.Obj)Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8));
                foreach (var s in (o["plan"] as List<object>)?.OfType<string>() ?? Enumerable.Empty<string>()) d.Plan.Add(s);
                foreach (var s in (o["acilan"] as List<object>)?.OfType<string>() ?? Enumerable.Empty<string>()) d.Done.Add(s);
                if (o["hazirlik"] is Json.Obj prep)
                    foreach (var kv in prep) d.Prep[kv.Key] = new HashSet<string>((kv.Value as List<object>)?.OfType<string>() ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
                foreach (var n in (o["notlar"] as List<object>)?.OfType<Json.Obj>() ?? Enumerable.Empty<Json.Obj>())
                    d.Notes.Add(new MapNote { X = n["x"] is double x ? x : 0, Y = n["y"] is double y ? y : 0, Text = n["metin"] as string ?? "", Color = n["renk"] as string ?? "#FF375F" });
                foreach (var r in (o["rota"] as List<object>)?.OfType<Json.Obj>() ?? Enumerable.Empty<Json.Obj>())
                    d.Route.Add(new RouteStop { PoiId = r["nokta"] as string, Label = r["ad"] as string, X = r["x"] is double x ? x : 0, Y = r["y"] is double y ? y : 0 });
            }
            catch { /* bozuk dosya: boş veriyle devam */ }
            return d;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Updater.DataRoot);
                string Arr(IEnumerable<string> xs) => "[" + string.Join(", ", xs.OrderBy(x => x, StringComparer.Ordinal).Select(Json.Quote)) + "]";
                string Num(double v) => v.ToString("0.#", Inv);
                var sb = new StringBuilder("{\n");
                sb.Append("  \"surum\": 1,\n");
                sb.Append("  \"plan\": ").Append(Arr(Plan)).Append(",\n");
                sb.Append("  \"acilan\": ").Append(Arr(Done)).Append(",\n");
                sb.Append("  \"hazirlik\": {").Append(string.Join(", ", Prep.Where(kv => kv.Value.Count > 0).Select(kv => Json.Quote(kv.Key) + ": " + Arr(kv.Value)))).Append("},\n");
                sb.Append("  \"notlar\": [").Append(string.Join(", ", Notes.Select(n => $"{{\"x\": {Num(n.X)}, \"y\": {Num(n.Y)}, \"metin\": {Json.Quote(n.Text ?? "")}, \"renk\": {Json.Quote(n.Color ?? "#FF375F")}}}"))).Append("],\n");
                sb.Append("  \"rota\": [").Append(string.Join(", ", Route.Select(r => $"{{\"nokta\": {(r.PoiId == null ? "null" : Json.Quote(r.PoiId))}, \"ad\": {Json.Quote(r.Label ?? "")}, \"x\": {Num(r.X)}, \"y\": {Num(r.Y)}}}"))).Append("]\n");
                sb.Append("}\n");
                var tmp = FilePath + ".yeni";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch { }
            Changed?.Invoke();
        }

        public bool IsPrepared(string robberyId, string itemId) => Prep.TryGetValue(robberyId, out var s) && s.Contains(itemId);
        public void SetPrepared(string robberyId, string itemId, bool on)
        {
            if (!Prep.TryGetValue(robberyId, out var s)) Prep[robberyId] = s = new HashSet<string>(StringComparer.Ordinal);
            if (on) s.Add(itemId); else s.Remove(itemId);
            Save();
        }
    }
}
