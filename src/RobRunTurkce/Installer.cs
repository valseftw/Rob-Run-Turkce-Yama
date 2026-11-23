using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RobRunTurkce
{
    /// <summary>
    /// Kurulum / kaldırma — "önbellek yöntemi":
    ///
    /// ROB RUN'ın Unity Addressables ayarı, açılışta oyunun kendi önbellek klasörüne
    /// (%USERPROFILE%\AppData\LocalLow\Kefir\ROB RUN\com.unity.addressables) bakar; orada oyunun güncel
    /// sürümüyle aynı özete (.hash) sahip bir katalog varsa onu kullanır. Yükleyici:
    ///   1) Oyunun İngilizce metin paketini ve yazı tipi paketini OKUR, Türkçe kopyalarını üretir,
    ///   2) Oyunun güncel kataloğunun bir kopyasında bu iki paketin adresini önbellekteki kopyalara çevirir
    ///      (adres uzunluğu birebir korunur; katalogdaki başka hiçbir bayt değişmez),
    ///   3) Hepsini önbellek klasörüne yazar.
    /// Oyun klasöründeki (Steam) hiçbir dosya değiştirilmez; yönetici izni gerekmez.
    /// Kaldırmak = bu dosyaları silmek. Oyun güncellenince katalog adı değişir, oyun kendiliğinden
    /// İngilizceye döner; yükleyiciyi tekrar çalıştırmak yeter.
    ///
    /// Eski sürümlerin "oyun dosyalarını değiştirme" yöntemiyle yapılmış bir kurulum bulunursa
    /// önce yedekten geri alınır.
    /// </summary>
    public sealed class Installer
    {
        public readonly string Game;
        public readonly string BackupRoot;   // durum kaydı ve (eski yöntemin) yedekleri
        public readonly string CacheDir;     // ...\LocalLow\Kefir\ROB RUN\com.unity.addressables
        readonly Action<string> log;

        const string RuntimeToken = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}";
        const string PersistentToken = "{UnityEngine.Application.persistentDataPath}";
        const string OldPrefix = RuntimeToken + "\\StandaloneWindows64\\";
        const string NewPrefix = PersistentToken + "/com.unity.addressables/";

        public Installer(string game, Action<string> log, string backupRoot = null, string cacheRoot = null)
        {
            Game = game; this.log = log;
            BackupRoot = backupRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobRunTurkceYama", "yedek");
            CacheDir = Path.Combine(cacheRoot ?? Program.CacheRootOverride ?? PersistentDataPath(game), "com.unity.addressables");
        }

        string StatePath => Path.Combine(BackupRoot, "durum.json");
        public static readonly string[] Targets = { StringTablePatcher.EnglishBundle, FontPatcher.FontBundle };

        // ------------------------------------------------------------------ yollar

        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        /// <summary>Unity'nin Application.persistentDataPath'i: LocalLow\&lt;şirket&gt;\&lt;ürün&gt; (ROB RUN_Data\app.info'dan).</summary>
        public static string PersistentDataPath(string game)
        {
            string low;
            try
            {
                SHGetKnownFolderPath(new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16"), 0, IntPtr.Zero, out var p);
                low = Marshal.PtrToStringUni(p); Marshal.FreeCoTaskMem(p);
            }
            catch { low = Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? "", "AppData", "LocalLow"); }
            string company = "Kefir", product = "ROB RUN";
            try
            {
                var lines = File.ReadAllLines(Path.Combine(game, GameLocator.DataFolder, "app.info"));
                if (lines.Length >= 2 && lines[0].Trim().Length > 0) { company = lines[0].Trim(); product = lines[1].Trim(); }
            }
            catch { }
            return Path.Combine(low, company, product);
        }

        /// <summary>Oyunun güncel kataloğu: settings.json'daki uzak/önbellek özet adresleri çözülür.</summary>
        public sealed class CatalogInfo { public string RemoteBin, RemoteHash, CacheBin, CacheHash, Name; }

        public CatalogInfo ReadCatalog()
        {
            string aa = Path.Combine(Game, GameLocator.DataFolder, "StreamingAssets", "aa");
            var settings = (Json.Obj)Json.Parse(File.ReadAllText(Path.Combine(aa, "settings.json"), Encoding.UTF8));
            string remote = null, cache = null;
            foreach (Json.Obj loc in (List<object>)settings["m_CatalogLocations"])
            {
                var keys = ((List<object>)loc["m_Keys"]).OfType<string>().ToList();
                if (keys.Contains("AddressablesMainContentCatalogRemoteHash")) remote = loc["m_InternalId"] as string;
                if (keys.Contains("AddressablesMainContentCatalogCacheHash")) cache = loc["m_InternalId"] as string;
            }
            if (remote == null || cache == null)
                throw new InvalidDataException("Oyunun katalog ayarı beklenen biçimde değil (önbellek desteği bulunamadı). Oyun yapısı değişmiş olabilir.");
            string Resolve(string id) => id.Replace(RuntimeToken, aa).Replace(PersistentToken, Path.GetDirectoryName(CacheDir)).Replace('/', '\\');
            var ci = new CatalogInfo { RemoteHash = Resolve(remote), CacheHash = Path.Combine(CacheDir, Path.GetFileName(Resolve(cache))) };
            ci.RemoteBin = Path.ChangeExtension(ci.RemoteHash, ".bin");
            ci.CacheBin = Path.ChangeExtension(ci.CacheHash, ".bin");
            ci.Name = Path.GetFileNameWithoutExtension(ci.RemoteHash);
            if (!File.Exists(ci.RemoteBin) || !File.Exists(ci.RemoteHash))
                throw new FileNotFoundException("Oyunun güncel katalog dosyası bulunamadı: " + Path.GetFileName(ci.RemoteBin));
            return ci;
        }

        /// <summary>Önbellekteki Türkçe kopyanın adı: adres uzunluğu birebir aynı kalsın diye ad uzatılır.</summary>
        static string CacheName(string bundle)
        {
            int extra = OldPrefix.Length - NewPrefix.Length; // şu an 9
            if (extra < 0) throw new InvalidOperationException("Önbellek adresi oyun adresinden uzun; bu sürüm desteklenmiyor.");
            string suffix = ("_turkce" + new string('_', Math.Max(0, extra))).Substring(0, extra);
            return Path.GetFileNameWithoutExtension(bundle) + suffix + ".bundle";
        }

        /// <summary>Katalogdaki paket adresini (uzunluk alanıyla birlikte doğrulayarak) önbellekteki kopyaya çevirir.</summary>
        static void Redirect(byte[] cat, string bundle)
        {
            var from = Encoding.ASCII.GetBytes(OldPrefix + bundle);
            var to = Encoding.ASCII.GetBytes(NewPrefix + CacheName(bundle));
            if (from.Length != to.Length) throw new InvalidOperationException("İç hata: adres uzunlukları eşit değil.");
            var hits = new List<int>();
            for (int i = 4; i <= cat.Length - from.Length; i++)
            {
                if (cat[i] != from[0]) continue;
                int j = 1; while (j < from.Length && cat[i + j] == from[j]) j++;
                if (j == from.Length) hits.Add(i);
            }
            if (hits.Count != 1)
                throw new InvalidDataException($"Katalogda {bundle} adresi {hits.Count} kez bulundu (1 bekleniyordu). Oyun yapısı değişmiş olabilir.");
            int at = hits[0];
            if (BitConverter.ToInt32(cat, at - 4) != from.Length)
                throw new InvalidDataException($"Katalogdaki {bundle} adresinin uzunluk alanı beklenenden farklı.");
            Buffer.BlockCopy(to, 0, cat, at, to.Length);
        }

        // ------------------------------------------------------------------ durum kaydı

        public sealed class FileState { public string Name, Original, Patched; }
        public sealed class State
        {
            public string Version, BuildGuid, Date, Mode, Method, Catalog, CacheDir;
            public List<FileState> Files = new List<FileState>();
            public bool IsCacheMethod => Method == "onbellek";
        }

        public enum PatchStatus { NotInstalled, Installed, Outdated }

        public PatchStatus GetStatus()
        {
            var st = LoadState();
            if (st == null || st.Files.Count == 0) return PatchStatus.NotInstalled;
            if (!st.IsCacheMethod)
            {
                string bdir = GameLocator.BundleDir(Game);
                bool all = st.Files.All(f => File.Exists(Path.Combine(bdir, f.Name)) && Sha256(Path.Combine(bdir, f.Name)) == f.Patched);
                return all ? PatchStatus.Installed : PatchStatus.Outdated;
            }
            try
            {
                var ci = ReadCatalog();
                if (ci.Name != st.Catalog) return PatchStatus.Outdated;                   // oyun güncellendi
                if (!File.Exists(ci.CacheHash) || File.ReadAllText(ci.CacheHash).Trim() != File.ReadAllText(ci.RemoteHash).Trim()) return PatchStatus.Outdated;
                bool ok = st.Files.All(f => File.Exists(Path.Combine(st.CacheDir, f.Name)) && Sha256(Path.Combine(st.CacheDir, f.Name)) == f.Patched);
                return ok ? PatchStatus.Installed : PatchStatus.Outdated;
            }
            catch { return PatchStatus.Outdated; }
        }

        public string StatusText(Translations tr)
        {
            var st = LoadState();
            var sb = new StringBuilder();
            sb.AppendLine($"Yükleyici sürümü : {Program.Version} (çeviri {tr.Version})");
            sb.AppendLine($"Oyun klasörü     : {Game}");
            sb.AppendLine($"Önbellek klasörü : {CacheDir}");
            switch (GetStatus())
            {
                case PatchStatus.NotInstalled: sb.Append("Durum            : Yama kurulu değil"); break;
                case PatchStatus.Installed: sb.Append($"Durum            : Yama KURULU ({st.Date}, {(st.IsCacheMethod ? "önbellek yöntemi" : "eski yöntem")})"); break;
                default: sb.Append("Durum            : Oyun güncellenmiş, yama devre dışı kalmış. Yeniden kurun."); break;
            }
            return sb.ToString();
        }

        public static string Sha256(string path)
        {
            using (var s = File.OpenRead(path)) using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        public State LoadState()
        {
            if (!File.Exists(StatePath)) return null;
            var o = (Json.Obj)Json.Parse(File.ReadAllText(StatePath, Encoding.UTF8));
            var st = new State
            {
                Version = o["surum"] as string, BuildGuid = o["oyunBuild"] as string, Date = o["tarih"] as string, Mode = o["harfModu"] as string,
                Method = o["yontem"] as string ?? "oyun-klasoru", Catalog = o["katalog"] as string, CacheDir = o["onbellekKlasoru"] as string
            };
            foreach (Json.Obj f in (List<object>)o["dosyalar"])
                st.Files.Add(new FileState { Name = (string)f["ad"], Original = f["orijinalSha256"] as string, Patched = (string)f["yamaliSha256"] });
            return st;
        }

        void SaveState(State st)
        {
            Directory.CreateDirectory(BackupRoot);
            var sb = new StringBuilder("{\n");
            sb.Append("  \"surum\": ").Append(Json.Quote(st.Version)).Append(",\n");
            sb.Append("  \"yontem\": ").Append(Json.Quote(st.Method)).Append(",\n");
            sb.Append("  \"oyunBuild\": ").Append(Json.Quote(st.BuildGuid ?? "")).Append(",\n");
            sb.Append("  \"katalog\": ").Append(Json.Quote(st.Catalog ?? "")).Append(",\n");
            sb.Append("  \"onbellekKlasoru\": ").Append(Json.Quote(st.CacheDir ?? "")).Append(",\n");
            sb.Append("  \"tarih\": ").Append(Json.Quote(st.Date)).Append(",\n");
            sb.Append("  \"harfModu\": ").Append(Json.Quote(st.Mode ?? "")).Append(",\n");
            sb.Append("  \"dosyalar\": [\n");
            sb.Append(string.Join(",\n", st.Files.Select(f =>
                $"    {{\"ad\": {Json.Quote(f.Name)}, \"orijinalSha256\": {Json.Quote(f.Original ?? "")}, \"yamaliSha256\": {Json.Quote(f.Patched)}}}")));
            sb.Append("\n  ]\n}\n");
            File.WriteAllText(StatePath, sb.ToString(), new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------ oyun açık mı?

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

        static string ProcessPath(Process p)
        {
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, p.Id);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024); int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        /// <summary>Bu klasördeki oyun (ya da anti-cheat başlatıcısı) çalışıyor mu? Yol okunamazsa temkinli davranıp "evet" der.</summary>
        public bool GameRunning() => GameProcesses().Count > 0;

        List<Process> GameProcesses()
        {
            var list = new List<Process>();
            string dir = Path.GetFullPath(Game).TrimEnd('\\', '/') + "\\";
            foreach (var p in Process.GetProcesses())
            {
                string n;
                try { n = p.ProcessName; } catch { continue; }
                if (!n.Equals("ROB RUN", StringComparison.OrdinalIgnoreCase) && !n.Equals("start_protected_game", StringComparison.OrdinalIgnoreCase)) continue;
                var path = ProcessPath(p);
                if (path == null || Path.GetFullPath(path).StartsWith(dir, StringComparison.OrdinalIgnoreCase)) list.Add(p);
            }
            return list;
        }

        /// <summary>
        /// Oyunu nazikçe kapatır: penceresine "kapat" isteği gönderir (pencerenin X düğmesi gibi).
        /// Zorla sonlandırmaz; oyun kapanmazsa false döner. Belleğe ya da anti-cheat'e dokunmaz.
        /// </summary>
        public bool CloseGame(int timeoutMs = 20000)
        {
            foreach (var p in GameProcesses())
                try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); } catch { }
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (!GameRunning()) return true;
                System.Threading.Thread.Sleep(400);
            }
            return !GameRunning();
        }

        // ------------------------------------------------------------------ yardımcılar

        /// <summary>Dosyada bu yamanın (eski yöntemle) izi var mı? (İngilizce tabloda Türkçe harf / fontta 'ş')</summary>
        bool LooksPatched(string name, string path)
        {
            try
            {
                if (name == StringTablePatcher.EnglishBundle)
                {
                    var en = StringTablePatcher.ReadEnglish(GameLocator.BundleDir(Game), path);
                    return en.Values.SelectMany(t => t.Values).Any(v => v.IndexOfAny(new[] { 'ı', 'ş', 'ğ', 'İ', 'Ş', 'Ğ' }) >= 0);
                }
                if (name == FontPatcher.FontBundle) return FontPatcher.HasTurkishGlyphs(path);
            }
            catch { }
            return false;
        }

        /// <summary>Dosyayı güvenli değiştirir: önce geçici kopya, sonra atomik yer değiştirme.</summary>
        static void ReplaceFile(string src, string dst)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            string tmp = dst + ".trtmp";
            File.Copy(src, tmp, true);
            try
            {
                if (File.Exists(dst)) File.Replace(tmp, dst, null);
                else File.Move(tmp, dst);
            }
            catch (PlatformNotSupportedException) { File.Copy(tmp, dst, true); File.Delete(tmp); }
            catch (IOException) { File.Copy(tmp, dst, true); File.Delete(tmp); }
        }

        /// <summary>Eski sürümün oyun klasöründe yaptığı değişiklikleri yedekten geri alır.</summary>
        bool RestoreLegacy(State st)
        {
            string bdir = GameLocator.BundleDir(Game);
            bool allOk = true;
            foreach (var f in st.Files)
            {
                string live = Path.Combine(bdir, f.Name), bak = Path.Combine(BackupRoot, f.Name);
                string cur = File.Exists(live) ? Sha256(live) : null;
                if (cur == f.Original) continue;
                if (cur == f.Patched && File.Exists(bak) && Sha256(bak) == f.Original)
                {
                    ReplaceFile(bak, live);
                    log($"  {f.Name}: eski yöntemle değiştirilmiş oyun dosyası yedekten geri yüklendi");
                    continue;
                }
                allOk = false;
                log($"  {f.Name}: eski kurulumun yedeği bu sürüme ait değil; dokunulmadı.");
            }
            if (allOk) foreach (var f in st.Files) { try { File.Delete(Path.Combine(BackupRoot, f.Name)); } catch { } }
            return allOk;
        }

        // ------------------------------------------------------------------ kur / kaldır

        public void Install(Translations tr, string version, bool force = false)
        {
            if (!force && GameRunning()) throw new InvalidOperationException("Oyun açık. Önce ROB RUN'ı tamamen kapatın.");
            string bdir = GameLocator.BundleDir(Game);
            foreach (var t in Targets)
                if (!File.Exists(Path.Combine(bdir, t))) throw new FileNotFoundException("Beklenen oyun dosyası bulunamadı (oyun yapısı değişmiş olabilir): " + t);

            log("1/4 Oyun sürümü ve dosyaları denetleniyor...");
            var old = LoadState();
            if (old != null && !old.IsCacheMethod)
            {
                log("  Eski yöntemle yapılmış kurulum bulundu; oyun dosyaları geri yükleniyor");
                if (!RestoreLegacy(old))
                    throw new IOException("Eski kurulum geri alınamadı. Steam > ROB RUN > Özellikler > Yüklü Dosyalar > 'Oyun dosyalarının bütünlüğünü doğrula' yapıp tekrar deneyin.");
                old = null;
            }
            foreach (var t in Targets)
                if (LooksPatched(t, Path.Combine(bdir, t)))
                    throw new IOException($"Oyun klasöründeki {t} değiştirilmiş görünüyor. Steam > ROB RUN > Özellikler > Yüklü Dosyalar > " +
                                          "'Oyun dosyalarının bütünlüğünü doğrula' yapıp tekrar deneyin.");
            var ci = ReadCatalog();
            log($"  Oyun kataloğu: {ci.Name}");

            var work = Path.Combine(Path.GetTempPath(), "RobRunTurkceYama-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                log("2/4 Eksik Türkçe harfler yazı tiplerine ekleniyor...");
                string fontOut = Path.Combine(work, CacheName(FontPatcher.FontBundle));
                bool transliterate = false;
                FontPatcher.FontReport frep = null;
                try
                {
                    frep = FontPatcher.Patch(Path.Combine(bdir, FontPatcher.FontBundle), fontOut);
                    foreach (var l in frep.Lines) log("     " + l);
                    if (frep.Composed == 0) transliterate = true;
                }
                catch (Exception e) { log("  Font yaması uygulanamadı: " + e.Message); transliterate = true; }
                bool useFont = !transliterate && frep != null && frep.FontsPatched > 0 && File.Exists(fontOut);
                if (transliterate) log("  UYARI: ı/ş/ğ harfleri i/s/g olarak yazılacak (yazı tipine eklenemedi).");

                log("3/4 Metinler Türkçeye çevriliyor...");
                string textOut = Path.Combine(work, CacheName(StringTablePatcher.EnglishBundle));
                var rep = StringTablePatcher.Patch(bdir, Path.Combine(bdir, StringTablePatcher.EnglishBundle), textOut, tr, transliterate);
                log($"     {rep.Applied}/{rep.Total} metin çevrildi, {rep.Untranslated} çevrilmemiş, {rep.Rejected} hatalı çeviri atlandı" +
                    (rep.Outdated > 0 ? $", {rep.Outdated} metnin İngilizcesi değişmiş (çeviri eski olabilir)" : ""));
                foreach (var p in rep.Problems.Take(10)) log("     ! " + p);

                // Katalog: oyunun güncel kataloğunun kopyasında iki paketin adresini önbelleğe çevir
                var cat = File.ReadAllBytes(ci.RemoteBin);
                Redirect(cat, StringTablePatcher.EnglishBundle);
                if (useFont) Redirect(cat, FontPatcher.FontBundle);
                string catOut = Path.Combine(work, Path.GetFileName(ci.CacheBin));
                File.WriteAllBytes(catOut, cat);
                string hashOut = Path.Combine(work, Path.GetFileName(ci.CacheHash));
                File.Copy(ci.RemoteHash, hashOut, true); // oyunun kendi özeti: "önbellek güncel" sayılsın

                log("4/4 Türkçe dosyalar oyunun önbellek klasörüne yazılıyor (oyun klasörüne dokunulmaz)...");
                var outputs = new List<string> { textOut, catOut, hashOut };
                if (useFont) outputs.Insert(1, fontOut);
                var st = new State
                {
                    Version = version, Method = "onbellek", BuildGuid = GameLocator.BuildGuid(Game), Catalog = ci.Name, CacheDir = CacheDir,
                    Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Mode = transliterate ? "harf-donusumu" : "font-yamasi"
                };
                foreach (var o in outputs) st.Files.Add(new FileState { Name = Path.GetFileName(o), Patched = Sha256(o) });

                // Önceki kurulumdan kalan ve artık kullanılmayan dosyaları (ör. eski katalog adı) temizle
                if (old?.CacheDir != null)
                    foreach (var f in old.Files.Where(f => !st.Files.Any(n => n.Name.Equals(f.Name, StringComparison.OrdinalIgnoreCase))))
                        DeleteCacheFile(old.CacheDir, f.Name);

                SaveState(st); // dosyalar yazılmadan önce: yarıda kesilse bile "Kaldır" neyi sileceğini bilir
                Directory.CreateDirectory(CacheDir);
                foreach (var o in outputs) ReplaceFile(o, Path.Combine(CacheDir, Path.GetFileName(o)));
                foreach (var f in st.Files)
                    if (Sha256(Path.Combine(CacheDir, f.Name)) != f.Patched)
                        throw new IOException($"{f.Name} önbelleğe doğru yazılamadı. 'Yamayı kaldır' ile temizleyin.");
                log("     " + CacheDir);
                log("Kurulum tamamlandı. Oyunda dil olarak 'English' seçili olmalı (Türkçe metinler onun yerine gelir).");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { }
            }
        }

        void DeleteCacheFile(string dir, string name)
        {
            // Güvenlik: yalnızca bir "com.unity.addressables" klasöründeki, yol içermeyen dosya adları silinir
            if (string.IsNullOrEmpty(dir) || !Path.GetFileName(dir.TrimEnd('\\', '/')).Equals("com.unity.addressables", StringComparison.OrdinalIgnoreCase)) return;
            if (name.IndexOfAny(new[] { '\\', '/' }) >= 0 || name.Contains("..")) return;
            string p = Path.Combine(dir, name);
            if (File.Exists(p)) { File.Delete(p); log($"  silindi: {name}"); }
        }

        public void Uninstall()
        {
            if (GameRunning()) throw new InvalidOperationException("Oyun açık. Önce ROB RUN'ı tamamen kapatın.");
            var st = LoadState();
            if (st == null) { log("Kayıtlı bir kurulum bulunamadı; yapılacak bir şey yok."); return; }
            if (!st.IsCacheMethod)
            {
                if (RestoreLegacy(st)) { try { File.Delete(StatePath); } catch { } log("Yama kaldırıldı, oyun dosyaları orijinal haline döndü."); }
                else log("Bazı dosyalar geri yüklenemedi. Kesin çözüm: Steam > ROB RUN > Özellikler > Yüklü Dosyalar > 'Oyun dosyalarının bütünlüğünü doğrula'.");
                return;
            }
            foreach (var f in st.Files) DeleteCacheFile(st.CacheDir, f.Name);
            try { File.Delete(StatePath); } catch { }
            log("Yama kaldırıldı: Türkçe önbellek dosyaları silindi. Oyun bir sonraki açılışta İngilizce başlar.");
        }
    }
}
