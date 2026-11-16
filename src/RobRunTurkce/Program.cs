using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;


namespace RobRunTurkce
{
    public static class Program
    {
        /// <summary>Test için: Unity önbellek kökü yerine kullanılacak klasör (--onbellek-klasoru).</summary>
        public static string CacheRootOverride;

        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        static readonly string[] CliCommands = { "--kur", "--kaldir", "--durum", "--disa-aktar", "--kopyaya", "--font-onizleme", "--guncelleme-denetle", "--yardim", "-h", "/?" };

        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        public static int Main(string[] args)
        {
            // Bağımlılık (AssetsTools.NET, MIT lisanslı) exe'nin içine gömülüdür; buradan yüklenir.
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                if (!e.Name.StartsWith("AssetsTools.NET", StringComparison.OrdinalIgnoreCase)) return null;
                using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("AssetsTools.NET.dll"))
                using (var ms = new MemoryStream()) { st.CopyTo(ms); return Assembly.Load(ms.ToArray()); }
            };

            CacheRootOverride = Arg(args, "--onbellek-klasoru");
            Updater.SourceOverride = Arg(args, "--guncelleme-kaynagi");
            if (args.Any(a => CliCommands.Contains(a)))
                return RunCli(args);

            // Varsayılan: WPF arayüzü
            System.Windows.Forms.Application.EnableVisualStyles(); // klasör seçme penceresi için
            var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose };
            app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("pack://application:,,,/RobRunTurkceYama;component/Theme.xaml") });
            if (Arg(args, "--ekran-goruntusu") is string shotDir) // geliştirme: sayfaların PNG görüntüleri
            {
                MainWindow.CaptureScreens(shotDir, Arg(args, "--oyun"), Arg(args, "--yedek-klasoru"));
                return 0;
            }
            if (args.Contains("--baslat")) // "ROB RUN Türkçe" kısayolu: denetle, gerekirse yeniden uygula, oyunu aç
            {
                app.Run(new LauncherWindow(Arg(args, "--oyun"), Arg(args, "--yedek-klasoru"), args.Contains("--deneme")));
                return 0;
            }
            new System.Threading.Thread(() => { try { Updater.CleanupOld(); } catch { } }) { IsBackground = true }.Start();
            var win = new MainWindow(Arg(args, "--oyun"), Arg(args, "--otomatik"), Arg(args, "--yedek-klasoru"));
            app.Run(win);
            return 0;
        }

        public static string Arg(string[] a, string name)
        {
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        public static bool CanWrite(string dir)
        {
            try
            {
                var p = Path.Combine(dir, ".robrun-turkce-yazma-testi");
                File.WriteAllText(p, "x"); File.Delete(p); return true;
            }
            catch { return false; }
        }

        /// <summary>Programı yönetici olarak yeniden başlatır (Windows izin penceresi açılır). İptal edilirse false.</summary>
        public static bool RestartElevated(string arguments)
        {
            var psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location) { UseShellExecute = true, Verb = "runas", Arguments = arguments };
            try { Process.Start(psi); return true; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        public static string Quote(string s) => "\"" + s.Replace("\"", "") + "\"";

        public static string ReadResource(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        /// <summary>Çalışan exe'nin SHA-256 özeti (VirusTotal bağlantısı ve kullanıcının karşılaştırması için).</summary>
        public static string OwnSha256()
        {
            try { return Installer.Sha256(Assembly.GetExecutingAssembly().Location); }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        // Komut satırı (geliştiriciler/çevirmenler için). Oyuncuların buna ihtiyacı yoktur.
        // ------------------------------------------------------------------
        static int RunCli(string[] args)
        {
            AttachConsole(-1); // exe'yi açan komut penceresine yaz
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            Console.WriteLine();
            try { return Cli(args); }
            catch (Exception e)
            {
                Console.WriteLine("HATA: " + e.Message);
                return 1;
            }
        }

        static int Cli(string[] args)
        {
            if (args.Contains("--yardim") || args.Contains("-h") || args.Contains("/?"))
            {
                Console.WriteLine(@"Kullanım: RobRunTurkceYama.exe [seçenekler]
  (seçeneksiz)                  Arayüzü açar (oyuncular için)
  --oyun <klasör>               Oyun klasörünü elle belirtir
  --kur --kabul                 Arayüzsüz kurar (riskleri kabul ettiğinizi belirtir)
  --kaldir                      Arayüzsüz kaldırır
  --durum                       Kurulum durumunu gösterir
  --disa-aktar <dosya.json>     Oyundaki İngilizce metinleri + mevcut çevirileri dışa aktarır (çevirmenler için)
  --ceviri <tr.json>            Gömülü çeviri yerine bu dosyayı kullanır
  --kopyaya <klasör>            Oyuna dokunmadan yamalı dosyaları bu klasöre yazar (deneme için)
  --font-onizleme <klasör>      Üretilen harflerin önizlemelerini (.png) yazar
  --yedek-klasoru <klasör>      Durum kaydının tutulacağı klasör (varsayılan: %LOCALAPPDATA%\RobRunTurkceYama\yedek)
  --onbellek-klasoru <klasör>   Test için: Unity önbellek kökü yerine bu klasörü kullanır
  --baslat                      Başlatıcı: güncellemeleri denetler, gerekirse Türkçeyi yeniden uygular, ROB RUN'ı açar
  --guncelleme-denetle          GitHub'daki son sürümü denetler (imzayı doğrular), hiçbir şey değiştirmez
  --guncelleme-kaynagi <adres>  Test için: GitHub API yerine bu ""latest release"" JSON adresini kullanır
  --deneme                      Test için: --baslat oyunu (Steam'i) açmaz");
                return 0;
            }
            if (args.Contains("--guncelleme-denetle"))
            {
                if (!Updater.Enabled) { Console.WriteLine("Güncelleme kapalı: release.json içinde GitHub deposu yok."); return 0; }
                var info = Updater.Check();
                var cur = Translations.LoadActive();
                Console.WriteLine("İmza doğrulandı.");
                Console.WriteLine($"Program : bu {Version}, son {info.App}{(info.AppNewer ? "  (yeni sürüm var)" : "")}");
                Console.WriteLine($"Çeviri  : bu {cur.Version}, son {info.Tr}{(info.TrNewer(cur) ? "  (yeni çeviri var)" : "")}");
                return 0;
            }
            var tr = Arg(args, "--ceviri") is string trFile ? Translations.LoadFile(trFile) : Translations.LoadActive();
            string game = Arg(args, "--oyun") ?? GameLocator.Find();
            if (game == null || !GameLocator.IsGameDir(game))
                throw new DirectoryNotFoundException("ROB RUN klasörü bulunamadı. --oyun \"<klasör>\" ile belirtin.");
            string bdir = GameLocator.BundleDir(game);
            var installer = new Installer(game, Console.WriteLine, Arg(args, "--yedek-klasoru"));

            var exportPath = Arg(args, "--disa-aktar");
            if (exportPath != null)
            {
                var st = installer.LoadState();
                // Yama kuruluysa İngilizce metin yedekten okunur
                string enPath = null;
                var es = st?.Files.FirstOrDefault(f => f.Name == StringTablePatcher.EnglishBundle);
                if (es != null && Installer.Sha256(Path.Combine(bdir, StringTablePatcher.EnglishBundle)) != es.Original)
                    enPath = Path.Combine(installer.BackupRoot, StringTablePatcher.EnglishBundle);
                StringTablePatcher.Export(bdir, enPath, tr, exportPath);
                Console.WriteLine("Dışa aktarıldı: " + exportPath);
                return 0;
            }

            var copyTo = Arg(args, "--kopyaya");
            var preview = Arg(args, "--font-onizleme");
            if (copyTo != null || preview != null)
            {
                string outDir = copyTo ?? Path.Combine(Path.GetTempPath(), "RobRunTurkceYama-onizleme");
                Directory.CreateDirectory(outDir);
                var f = FontPatcher.Patch(Path.Combine(bdir, FontPatcher.FontBundle), Path.Combine(outDir, FontPatcher.FontBundle), preview);
                foreach (var l in f.Lines) Console.WriteLine(l);
                if (copyTo != null)
                {
                    var r = StringTablePatcher.Patch(bdir, Path.Combine(bdir, StringTablePatcher.EnglishBundle), Path.Combine(outDir, StringTablePatcher.EnglishBundle), tr, f.Composed == 0);
                    Console.WriteLine($"{r.Applied}/{r.Total} metin çevrildi, {r.Untranslated} çevrilmemiş, {r.Rejected} hatalı, {r.Outdated} eski.");
                    foreach (var p in r.Problems) Console.WriteLine("  ! " + p);
                    Console.WriteLine("Yamalı dosyalar: " + outDir);
                }
                return 0;
            }

            if (args.Contains("--durum")) { Console.WriteLine(installer.StatusText(tr)); return 0; }

            if (args.Contains("--kur") || args.Contains("--kaldir"))
            {
                if (args.Contains("--kur") && !args.Contains("--kabul"))
                    throw new InvalidOperationException("Arayüzsüz kurulum için riskleri kabul ettiğinizi --kabul ile belirtin.");
                if (args.Contains("--kur")) installer.Install(tr, Version + " / çeviri " + tr.Version);
                else installer.Uninstall();
                return 0;
            }
            return 0;
        }
    }
}
