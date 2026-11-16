using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RobRunTurkce
{
    /// <summary>Kullanıcı ayarları: seçilen oyun klasörü (%LOCALAPPDATA%\RobRunTurkceYama\ayarlar.json).</summary>
    public static class AppSettings
    {
        static string FilePath => Path.Combine(Updater.DataRoot, "ayarlar.json");

        public static string Game
        {
            get
            {
                try { return File.Exists(FilePath) ? ((Json.Obj)Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8)))["oyun"] as string : null; }
                catch { return null; }
            }
            set
            {
                try
                {
                    Directory.CreateDirectory(Updater.DataRoot);
                    File.WriteAllText(FilePath, "{\n  \"oyun\": " + Json.Quote(value ?? "") + "\n}\n", new UTF8Encoding(false));
                }
                catch { }
            }
        }
    }

    /// <summary>"ROB RUN Türkçe" kısayolları: programın kalıcı kopyasını --baslat ile açar.</summary>
    public static class Shortcuts
    {
        public const string Name = "ROB RUN Türkçe";
        public static string AppDir => Path.Combine(Updater.DataRoot, "uygulama");
        public static string AppExe => Path.Combine(AppDir, Updater.ExeAsset);
        static string Desktop => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk");
        static string StartMenu => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk");

        public static bool Exist => File.Exists(Desktop) || File.Exists(StartMenu);

        static string Self => Assembly.GetExecutingAssembly().Location;
        static bool RunningFromAppDir => string.Equals(Path.GetFullPath(Self), Path.GetFullPath(AppExe), StringComparison.OrdinalIgnoreCase);

        /// <summary>Programı kalıcı klasöre kopyalar (İndirilenler silinse de kısayol çalışsın) ve kısayolları oluşturur.</summary>
        public static void Create()
        {
            if (!RunningFromAppDir)
            {
                Directory.CreateDirectory(AppDir);
                bool copy = !File.Exists(AppExe);
                if (!copy)
                {
                    try { copy = Updater.ParseVersion(FileVersionInfo.GetVersionInfo(AppExe).FileVersion) <= Updater.ParseVersion(Program.Version) && Installer.Sha256(AppExe) != Installer.Sha256(Self); }
                    catch { copy = true; }
                }
                if (copy) File.Copy(Self, AppExe, true);
            }
            foreach (var lnk in new[] { Desktop, StartMenu }) MakeLink(lnk, AppExe, "--baslat", "Türkçe yamayı denetler, gerekirse yeniden uygular ve ROB RUN'ı başlatır");
        }

        /// <summary>Kısayolları ve (çalışmıyorsa) kalıcı kopyayı siler.</summary>
        public static void Remove()
        {
            foreach (var lnk in new[] { Desktop, StartMenu }) try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
            if (!RunningFromAppDir) try { if (Directory.Exists(AppDir)) Directory.Delete(AppDir, true); } catch { }
        }

        static void MakeLink(string path, string target, string args, string description)
        {
            // Windows'un kendi kısayol nesnesi (WScript.Shell), geç bağlama ile
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var lt = link.GetType();
                void Set(string prop, string value) => lt.InvokeMember(prop, BindingFlags.SetProperty, null, link, new object[] { value });
                Set("TargetPath", target);
                Set("Arguments", args);
                Set("WorkingDirectory", Path.GetDirectoryName(target));
                Set("IconLocation", target + ",0");
                Set("Description", description);
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                Marshal.FinalReleaseComObject(link);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
    }

    /// <summary>
    /// Başlatıcı (--baslat): küçük bir açılış penceresi. Güncellemeleri denetler, oyun güncellendiyse
    /// ya da yeni çeviri geldiyse Türkçeyi yeniden uygular, sonra ROB RUN'ı Steam üzerinden açar.
    /// Yama hiç kurulmadıysa (riskler kabul edilmediyse) hiçbir şey yapmadan ana pencereyi açar.
    /// </summary>
    public sealed class LauncherWindow : Window
    {
        public const string SteamUrl = "steam://rungameid/4262460";

        readonly string gameArg, backupRoot;
        readonly bool dryRun;
        readonly TextBlock status, detail;
        readonly StackPanel buttons;
        readonly Border bar;

        static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        public LauncherWindow(string gameArg, string backupRoot, bool dryRun)
        {
            this.gameArg = gameArg; this.backupRoot = backupRoot; this.dryRun = dryRun;
            Title = "ROB RUN Türkçe";
            Width = 400; SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(C("#1C1C1E"));
            Foreground = new SolidColorBrush(C("#F5F5F7"));
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(typeof(LauncherWindow).Assembly.GetManifestResourceStream("assets/logo.png")); } catch { }

            var sp = new StackPanel { Margin = new Thickness(28, 26, 28, 22) };
            var logo = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(15), HorizontalAlignment = HorizontalAlignment.Center };
            try
            {
                var img = new System.Windows.Media.Imaging.BitmapImage();
                img.BeginInit(); img.StreamSource = typeof(LauncherWindow).Assembly.GetManifestResourceStream("assets/logo.png"); img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; img.EndInit();
                logo.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            }
            catch { logo.Background = new SolidColorBrush(C("#E30A17")); }
            logo.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.5 };
            sp.Children.Add(logo);
            sp.Children.Add(new TextBlock { Text = "ROB RUN Türkçe", FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) });
            status = new TextBlock { Text = "Hazırlanıyor…", FontSize = 13.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            sp.Children.Add(status);
            detail = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(C("#98989F")), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            sp.Children.Add(detail);

            // belirsiz ilerleme çubuğu (kayan parça)
            var track = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(C("#3A3A3C")), Margin = new Thickness(40, 18, 40, 0), ClipToBounds = true };
            bar = new Border { Width = 90, CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(C("#0A84FF")), HorizontalAlignment = HorizontalAlignment.Left };
            var tt = new TranslateTransform(); bar.RenderTransform = tt;
            track.Child = bar;
            sp.Children.Add(track);
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-90, 260, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });

            buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0), Visibility = Visibility.Collapsed };
            sp.Children.Add(buttons);

            Content = new Border { BorderBrush = new SolidColorBrush(C("#3A3A3C")), BorderThickness = new Thickness(1), Child = sp };
            MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                try { int dark = 1; DwmSetWindowAttribute(hwnd, 20, ref dark, 4); int round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, 4); } catch { }
            };
            ContentRendered += (s, e) => new Thread(Work) { IsBackground = true }.Start();
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void Ui(Action a) => Dispatcher.Invoke(a);
        void Say(string s, string d = "") => Ui(() => { status.Text = s; detail.Text = d; });

        void Work()
        {
            try { Updater.CleanupOld(); } catch { }
            string game = gameArg ?? AppSettings.Game;
            if (!GameLocator.IsGameDir(game)) game = GameLocator.Find();
            if (!GameLocator.IsGameDir(game)) { OpenMain(null); return; }

            Installer inst;
            Installer.PatchStatus st;
            try { inst = new Installer(game, _ => { }, backupRoot); st = inst.GetStatus(); }
            catch { OpenMain(game); return; }
            // Yama hiç kurulmadıysa riskler kabul edilmemiş demektir: kendiliğinden hiçbir şey yapılmaz.
            if (st == Installer.PatchStatus.NotInstalled) { OpenMain(game); return; }
            if (inst.GameRunning()) { Finish("ROB RUN zaten açık", "Türkçe denetimi oyun kapalıyken yapılır.", launch: false); return; }

            // 1) GitHub'dan güncelleme (en fazla birkaç saniye; başarısızlık oyunu engellemez)
            var tr = Translations.LoadActive();
            if (Updater.Enabled)
            {
                Say("Güncellemeler denetleniyor…");
                try
                {
                    var info = Updater.Check(5000);
                    if (info != null && info.AppNewer && CanReplaceSelf())
                    {
                        Say("Yeni sürüm indiriliyor…", "sürüm " + info.App);
                        string args = "--baslat" + (gameArg != null ? " --oyun " + Program.Quote(gameArg) : "") + (backupRoot != null ? " --yedek-klasoru " + Program.Quote(backupRoot) : "") + (dryRun ? " --deneme" : "") + PassThrough();
                        Updater.UpdateApp(info, args);
                        Ui(() => Application.Current.Shutdown());
                        return;
                    }
                    if (info != null && info.TrNewer(tr))
                    {
                        Say("Yeni çeviri indiriliyor…", "çeviri " + info.Tr);
                        tr = Updater.UpdateTranslation(info);
                    }
                    if (info != null && info.WikiNewer(Wiki.LoadActive()))
                    {
                        Say("Rehber güncelleniyor…", "rehber " + info.Wiki);
                        try { Updater.UpdateWiki(info); } catch { }
                    }
                }
                catch { /* çevrimdışı ya da doğrulanamadı: mevcut çeviriyle devam */ }
            }

            // 2) Oyun güncellendiyse ya da daha yeni çeviri varsa Türkçeyi yeniden uygula
            var installedTr = InstalledTranslationVersion(inst);
            if (st == Installer.PatchStatus.Outdated || Updater.ParseVersion(tr.Version) > installedTr)
            {
                Say(st == Installer.PatchStatus.Outdated ? "Oyun güncellenmiş, Türkçe yeniden uygulanıyor…" : "Çeviri güncelleniyor…");
                try
                {
                    var worker = new Installer(game, line =>
                    {
                        var m = Regex.Match(line.Trim(), @"^(\d)/(\d) (.+?)\.*$");
                        if (m.Success) Ui(() => detail.Text = $"Adım {m.Groups[1].Value}/{m.Groups[2].Value} · {m.Groups[3].Value}");
                    }, backupRoot);
                    worker.Install(tr, Program.Version + " / çeviri " + tr.Version);
                }
                catch (Exception e)
                {
                    Ui(() => ShowChoice("Türkçe uygulanamadı", e.Message, game));
                    return;
                }
            }
            Finish("ROB RUN başlatılıyor…", "Türkçe hazır · çeviri " + tr.Version, launch: true);
        }

        static string PassThrough()
        {
            var a = Environment.GetCommandLineArgs();
            var src = Program.Arg(a, "--guncelleme-kaynagi");
            var cache = Program.Arg(a, "--onbellek-klasoru");
            return (src != null ? " --guncelleme-kaynagi " + Program.Quote(src) : "") + (cache != null ? " --onbellek-klasoru " + Program.Quote(cache) : "");
        }

        static Version InstalledTranslationVersion(Installer inst)
        {
            try
            {
                var v = inst.LoadState()?.Version;
                var m = Regex.Match(v ?? "", @"çeviri\s+(\S+)");
                return m.Success ? Updater.ParseVersion(m.Groups[1].Value) : new Version(0, 0);
            }
            catch { return new Version(0, 0); }
        }

        static bool CanReplaceSelf()
        {
            try { return Program.CanWrite(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)); }
            catch { return false; }
        }

        void Finish(string s, string d, bool launch)
        {
            Say(s, d);
            if (launch) StartGame(dryRun);
            Thread.Sleep(launch ? 1400 : 2200);
            Ui(Close);
        }

        public static void StartGame(bool dryRun)
        {
            if (dryRun) return; // test: Steam açılmaz
            try { Process.Start(new ProcessStartInfo(SteamUrl) { UseShellExecute = true }); } catch { }
        }

        void ShowChoice(string title, string text, string game)
        {
            status.Text = title; detail.Text = text + "\nOyun İngilizce açılır; programı açıp \"Yeniden Kur\" diyebilirsin.";
            bar.Visibility = Visibility.Hidden;
            buttons.Visibility = Visibility.Visible;
            Button B(string t, string style, Action a, double ml = 0)
            {
                var b = new Button { Content = t, Margin = new Thickness(ml, 0, 0, 0) };
                if (Application.Current.TryFindResource(style) is Style sty) b.Style = sty;
                b.Click += (s, e) => a();
                return b;
            }
            buttons.Children.Add(B("Programı Aç", "SecondaryButton", () => OpenMain(game)));
            buttons.Children.Add(B("Yine de Başlat", "PrimaryButton", () => { StartGame(dryRun); Close(); }, 10));
        }

        void OpenMain(string game)
        {
            Ui(() =>
            {
                var w = new MainWindow(game, null, backupRoot);
                Application.Current.MainWindow = w;
                Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
                w.Show();
                Close();
            });
        }
    }
}
