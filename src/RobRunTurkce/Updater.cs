using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RobRunTurkce
{
    /// <summary>Bir GitHub sürümünün imzası doğrulanmış içeriği.</summary>
    public sealed class UpdateInfo
    {
        public Version App, Tr, Wiki;               // SHA256SUMS.txt içindeki (imzalı) sürüm satırları
        public readonly Dictionary<string, string> Sums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Page;                         // sürüm sayfası (tarayıcıda açmak için)
        public bool AppNewer => App != null && App > Updater.ParseVersion(Program.Version) && Sums.ContainsKey(Updater.ExeAsset) && Urls.ContainsKey(Updater.ExeAsset);
        public bool TrNewer(Translations current) => Tr != null && Tr > Updater.ParseVersion(current.Version) && Sums.ContainsKey(Updater.TrAsset) && Urls.ContainsKey(Updater.TrAsset);
        public bool WikiNewer(RobRunTurkce.Wiki current) => Wiki != null && Wiki > Updater.ParseVersion(current.Version) && Sums.ContainsKey(Updater.WikiAsset) && Urls.ContainsKey(Updater.WikiAsset);
    }

    /// <summary>
    /// GitHub'dan güncelleme. Program yalnızca release.json'daki GitHub deposunun "latest release"
    /// bilgisini ve o sürümün dosyalarını indirir; telemetri ya da veri gönderimi yoktur.
    ///
    /// Güvenlik: Sürümdeki SHA256SUMS.txt dosyası, yalnızca proje sahibinde bulunan gizli anahtarla
    /// imzalanır (SHA256SUMS.txt.sig, RSA-3072 / SHA-256). Exe'nin içinde yalnızca açık anahtar vardır.
    /// İmzası tutmayan ya da özeti listedekiyle eşleşmeyen hiçbir dosya kullanılmaz; GitHub hesabı
    /// ele geçirilse bile sahte bir exe veya çeviri dağıtılamaz.
    /// </summary>
    public static class Updater
    {
        public const string ExeAsset = "RobRunTurkceYama.exe";
        public const string TrAsset = "tr.json";
        public const string WikiAsset = "wiki.json";
        public const string SumsAsset = "SHA256SUMS.txt";
        public const string SigAsset = "SHA256SUMS.txt.sig";
        const string UserAgent = "RobRunTurkceYama";

        /// <summary>Geliştirme/test: GitHub API yerine kullanılacak "latest release" JSON adresi (--guncelleme-kaynagi).</summary>
        public static string SourceOverride;

        public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobRunTurkceYama");
        public static string CachedTranslation => Path.Combine(DataRoot, "ceviri", "tr.json");

        /// <summary>release.json'daki GitHub bağlantısından "sahip/depo" (yoksa null: güncelleme kapalı).</summary>
        public static string Repo
        {
            get
            {
                var m = Regex.Match(ReleaseInfo.Load().GitHub ?? "", @"^https://github\.com/([A-Za-z0-9-]+)/([A-Za-z0-9._-]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value + "/" + m.Groups[2].Value : null;
            }
        }

        public static bool Enabled => SourceOverride != null || Repo != null;

        public static Version ParseVersion(string s)
        {
            var m = Regex.Match(s ?? "", @"\d+(\.\d+){1,3}");
            return m.Success && Version.TryParse(m.Value, out var v) ? v : new Version(0, 0);
        }

        // ------------------------------------------------------------------ ağ

        static byte[] Get(string url, int timeoutMs, bool json = false)
        {
            if (SourceOverride == null && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Güvensiz adres reddedildi: " + url);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = WebRequest.Create(url);
            req.Timeout = timeoutMs;
            if (req is HttpWebRequest h)
            {
                h.UserAgent = UserAgent; // genel bir ad; kullanıcıya ait hiçbir bilgi gönderilmez
                h.ReadWriteTimeout = timeoutMs;
                h.AllowAutoRedirect = true; // GitHub indirmeleri objects.githubusercontent.com'a yönlendirir
                h.Accept = json ? "application/vnd.github+json" : "application/octet-stream";
            }
            using (var resp = req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[81920]; int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > 64L << 20) throw new InvalidDataException("İndirilen dosya beklenenden büyük.");
                }
                return ms.ToArray();
            }
        }

        /// <summary>Son sürümü denetler. Güncelleme kapalıysa null. Ağ/imza hatasında istisna fırlatır.</summary>
        public static UpdateInfo Check(int timeoutMs = 8000)
        {
            if (!Enabled) return null;
            string api = SourceOverride ?? $"https://api.github.com/repos/{Repo}/releases/latest";
            var rel = (Json.Obj)Json.Parse(Encoding.UTF8.GetString(Get(api, timeoutMs, json: true)));
            var info = new UpdateInfo { Page = rel["html_url"] as string };
            if (info.Page != null && !info.Page.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) info.Page = null;
            string allowedPrefix = Repo != null ? $"https://github.com/{Repo}/releases/download/" : null;
            if (rel["assets"] is List<object> assets)
                foreach (var a in assets)
                {
                    if (!(a is Json.Obj o) || !(o["name"] is string name) || !(o["browser_download_url"] is string url)) continue;
                    // Gerçek kullanımda yalnızca bu deponun sürüm dosyaları kabul edilir
                    if (SourceOverride == null && !url.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    info.Urls[name] = url;
                }
            if (!info.Urls.ContainsKey(SumsAsset) || !info.Urls.ContainsKey(SigAsset))
                throw new InvalidDataException("Sürümde imzalı özet listesi (SHA256SUMS.txt ve .sig) yok.");

            var sums = Get(info.Urls[SumsAsset], timeoutMs);
            var sig = Get(info.Urls[SigAsset], timeoutMs);
            if (!VerifySignature(sums, sig))
                throw new CryptographicException("Güncellemenin dijital imzası doğrulanamadı; güncelleme yok sayıldı.");

            foreach (var raw in Encoding.UTF8.GetString(sums).Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                Match m;
                if ((m = Regex.Match(line, @"^#\s*uygulama\s+(\S+)$")).Success) info.App = ParseVersion(m.Groups[1].Value);
                else if ((m = Regex.Match(line, @"^#\s*ceviri\s+(\S+)$")).Success) info.Tr = ParseVersion(m.Groups[1].Value);
                else if ((m = Regex.Match(line, @"^#\s*rehber\s+(\S+)$")).Success) info.Wiki = ParseVersion(m.Groups[1].Value);
                else if ((m = Regex.Match(line, @"^([0-9a-fA-F]{64})\s+\*?(\S+)$")).Success) info.Sums[m.Groups[2].Value] = m.Groups[1].Value.ToLowerInvariant();
            }
            return info;
        }

        public static bool VerifySignature(byte[] data, byte[] signature)
        {
            try
            {
                using (var rsa = new RSACng())
                {
                    rsa.FromXmlString(Program.ReadResource("guncelleme-acik-anahtar.xml"));
                    if (rsa.KeySize < 3072) return false;
                    return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch { return false; }
        }

        static byte[] Download(UpdateInfo info, string asset, int timeoutMs)
        {
            var bytes = Get(info.Urls[asset], timeoutMs);
            string sha;
            using (var h = SHA256.Create()) sha = BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            if (sha != info.Sums[asset])
                throw new CryptographicException(asset + " dosyasının özeti imzalı listedekiyle eşleşmiyor; dosya kullanılmadı.");
            return bytes;
        }

        // ------------------------------------------------------------------ çeviri güncellemesi

        /// <summary>Yeni çeviriyi indirir, doğrular ve yerel önbelleğe yazar. Yeni çeviriyi döner.</summary>
        public static Translations UpdateTranslation(UpdateInfo info, int timeoutMs = 20000)
        {
            var bytes = Download(info, TrAsset, timeoutMs);
            var text = new UTF8Encoding(false, true).GetString(bytes);
            var t = Translations.Load(text); // biçim hatalıysa burada durur
            if (ParseVersion(t.Version) != info.Tr) throw new InvalidDataException("Çeviri dosyasının sürümü imzalı listedekiyle eşleşmiyor.");
            WriteCache(CachedTranslation, bytes);
            return t;
        }

        /// <summary>Yeni rehber verisini indirir, doğrular ve yerel önbelleğe yazar.</summary>
        public static Wiki UpdateWiki(UpdateInfo info, int timeoutMs = 20000)
        {
            var bytes = Download(info, WikiAsset, timeoutMs);
            var w = Wiki.Load(new UTF8Encoding(false, true).GetString(bytes));
            if (ParseVersion(w.Version) != info.Wiki) throw new InvalidDataException("Rehber verisinin sürümü imzalı listedekiyle eşleşmiyor.");
            WriteCache(Wiki.CachedPath, bytes);
            return w;
        }

        static void WriteCache(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".yeni";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        // ------------------------------------------------------------------ program güncellemesi

        /// <summary>
        /// Yeni exe'yi indirir, özetini doğrular ve çalışan exe'nin yerine koyar (eski dosya .old olur).
        /// Yeni sürümü verilen argümanlarla başlatır. Çağıran taraf ardından kapanmalıdır.
        /// </summary>
        public static void UpdateApp(UpdateInfo info, string restartArgs, int timeoutMs = 60000)
        {
            var bytes = Download(info, ExeAsset, timeoutMs);
            string exe = Assembly.GetExecutingAssembly().Location;
            string fresh = exe + ".yeni", old = exe + ".old";
            File.WriteAllBytes(fresh, bytes);
            try
            {
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);       // Windows çalışan exe'nin yeniden adlandırılmasına izin verir
                File.Move(fresh, exe);
            }
            catch
            {
                if (!File.Exists(exe) && File.Exists(old)) File.Move(old, exe);
                try { File.Delete(fresh); } catch { }
                throw;
            }
            Process.Start(new ProcessStartInfo(exe, restartArgs ?? "") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
        }

        /// <summary>Önceki güncellemeden kalan .old dosyasını siler (eski süreç kapanmış olmalı).</summary>
        public static void CleanupOld()
        {
            string old = Assembly.GetExecutingAssembly().Location + ".old";
            for (int i = 0; i < 20 && File.Exists(old); i++)
            {
                try { File.Delete(old); } catch { System.Threading.Thread.Sleep(250); }
            }
        }
    }
}
