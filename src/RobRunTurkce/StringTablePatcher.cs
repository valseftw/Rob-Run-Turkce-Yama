using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace RobRunTurkce
{
    public sealed class PatchReport
    {
        public int Applied, Untranslated, Rejected, Outdated, Total;
        public readonly List<string> Problems = new List<string>();
    }

    /// <summary>
    /// İngilizce metin tablosu paketindeki metinleri Türkçeleriyle değiştirir.
    /// Anahtarlar (metin kimlikleri) "localization-assets-shared" paketinden okunur; o paket değiştirilmez.
    /// </summary>
    public static class StringTablePatcher
    {
        public const string EnglishBundle = "localization-string-tables-english(unitedstates)(en-us)_assets_all.bundle";
        public const string SharedBundle = "localization-assets-shared_assets_all.bundle";

        /// <summary>Paylaşılan tablodan: SharedTableData PathID -> (metin kimliği -> anahtar)</summary>
        static Dictionary<long, Dictionary<long, string>> ReadKeys(AssetsManager am, string sharedPath)
        {
            var bun = BundleIO.Open(am, sharedPath);
            var inst = am.LoadAssetsFileFromBundle(bun, 0, false);
            var result = new Dictionary<long, Dictionary<long, string>>();
            foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                var bf = am.GetBaseField(inst, info);
                if (bf["m_Entries"].IsDummy) continue;
                var d = new Dictionary<long, string>();
                foreach (var e in bf["m_Entries.Array"].Children) d[e["m_Id"].AsLong] = e["m_Key"].AsString;
                result[info.PathId] = d;
            }
            return result;
        }

        /// <summary>Oyundaki İngilizce metinleri (tablo -> anahtar -> metin) okur.</summary>
        public static SortedDictionary<string, SortedDictionary<string, string>> ReadEnglish(string bundleDir, string englishBundlePath = null)
        {
            var am = new AssetsManager();
            var keys = ReadKeys(am, Path.Combine(bundleDir, SharedBundle));
            var bun = BundleIO.Open(am, englishBundlePath ?? Path.Combine(bundleDir, EnglishBundle));
            var inst = am.LoadAssetsFileFromBundle(bun, 0, false);
            var result = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                var bf = am.GetBaseField(inst, info);
                if (bf["m_TableData"].IsDummy) continue;
                if (!keys.TryGetValue(bf["m_SharedData.m_PathID"].AsLong, out var kd)) continue;
                var t = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var e in bf["m_TableData.Array"].Children)
                    if (kd.TryGetValue(e["m_Id"].AsLong, out var k)) t[k] = e["m_Localized"].AsString;
                result[Translations.BaseTableName(bf["m_Name"].AsString)] = t;
            }
            am.UnloadAll(true);
            return result;
        }

        /// <summary>
        /// <paramref name="srcBundle"/> paketini okuyup çevirileri uygular ve <paramref name="outBundle"/> olarak yazar.
        /// </summary>
        public static PatchReport Patch(string bundleDir, string srcBundle, string outBundle, Translations tr, bool transliterate)
        {
            var rep = new PatchReport();
            var am = new AssetsManager();
            var keys = ReadKeys(am, Path.Combine(bundleDir, SharedBundle));
            var bun = BundleIO.Open(am, srcBundle);
            var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

            foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                var bf = am.GetBaseField(inst, info);
                if (bf["m_TableData"].IsDummy) continue;
                var table = Translations.BaseTableName(bf["m_Name"].AsString);
                if (!keys.TryGetValue(bf["m_SharedData.m_PathID"].AsLong, out var kd)) continue;
                tr.Tables.TryGetValue(table, out var trTable);

                foreach (var e in bf["m_TableData.Array"].Children)
                {
                    rep.Total++;
                    Entry entry = null;
                    if (!kd.TryGetValue(e["m_Id"].AsLong, out var key) || trTable == null || !trTable.TryGetValue(key, out entry))
                    { rep.Untranslated++; continue; }
                    var en = e["m_Localized"].AsString;
                    var problem = Translations.Check(en, entry.Tr);
                    if (problem != null)
                    {
                        rep.Rejected++;
                        rep.Problems.Add($"{table}/{key}: {problem} (İngilizce bırakıldı)");
                        continue;
                    }
                    if (entry.Hash != null && entry.Hash != Translations.Hash(en)) rep.Outdated++;
                    e["m_Localized"].AsString = transliterate ? Translations.Transliterate(entry.Tr) : entry.Tr;
                    rep.Applied++;
                }
                info.SetNewData(bf);
            }

            BundleIO.Save(bun, inst, 0, outBundle);
            am.UnloadAll(true);
            return rep;
        }

        /// <summary>Çevirmenler için: oyundaki İngilizce metinleri ve mevcut çevirileri tek dosyada dışa aktarır.</summary>
        public static void Export(string bundleDir, string englishBundlePath, Translations existing, string outPath)
        {
            var en = ReadEnglish(bundleDir, englishBundlePath);
            var sb = new StringBuilder();
            sb.Append("{\n  \"_bilgi\": {\"aciklama\": \"en = oyundaki İngilizce metin, tr = çeviri, h = İngilizce metnin özeti\"}");
            foreach (var t in en)
            {
                Dictionary<string, Entry> et = null;
                existing?.Tables.TryGetValue(t.Key, out et);
                sb.Append(",\n  ").Append(Json.Quote(t.Key)).Append(": {");
                bool first = true;
                foreach (var kv in t.Value)
                {
                    Entry ex = null; et?.TryGetValue(kv.Key, out ex);
                    sb.Append(first ? "\n    " : ",\n    ").Append(Json.Quote(kv.Key)).Append(": {\"h\": ")
                      .Append(Json.Quote(Translations.Hash(kv.Value))).Append(", \"en\": ").Append(Json.Quote(kv.Value))
                      .Append(", \"tr\": ").Append(Json.Quote(ex?.Tr ?? "")).Append('}');
                    first = false;
                }
                sb.Append("\n  }");
            }
            sb.Append("\n}\n");
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
