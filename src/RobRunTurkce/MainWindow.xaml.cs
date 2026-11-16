using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RobRunTurkce
{
    /// <summary>
    /// Oyuncuların gördüğü tek pencere (macOS Yükleyici düzeni, koyu mod).
    /// Adımlar: Giriş → Riskler (kabul / ret) → Kurulum → Özet. Konsol penceresi yoktur.
    /// </summary>
    public partial class MainWindow : Window
    {
        Translations tr;
        UpdateInfo update;
        readonly ReleaseInfo rel = ReleaseInfo.Load();
        readonly string backupRoot, autoAction, ownSha = Program.OwnSha256();
        string game;
        bool accepted, busy;
        int page;
        readonly string[] stepNames = { "Giriş", "Riskler", "Kurulum", "Özet" };
        FrameworkElement[] pages;

        static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        static readonly Color Blue = C("#0A84FF"), Green = C("#30D158"), Orange = C("#FF9F0A"), Red = C("#FF453A"),
            Indigo = C("#5E5CE6"), Teal = C("#64D2FF"), Gray = C("#8E8E93"), Discord = C("#5865F2"), TurkRed = C("#E30A17");
        static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");
        static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");

        public MainWindow(string gameArg, string autoAction, string backupRoot)
        {
            InitializeComponent();
            this.autoAction = autoAction;
            this.backupRoot = backupRoot;
            tr = Translations.LoadActive();
            pages = new FrameworkElement[] { PageIntro, PageRisks, PageInstall, PageDone };

            var logo = LoadImage("logo.png");
            SidebarImage.Source = LoadImage("sidebar.jpg");
            LogoBrush.ImageSource = logo; TargetBrush.ImageSource = logo; SheetLogo.ImageSource = logo;
            HeroBrush.ImageSource = LoadImage("hero.jpg");
            WindowTitle.Text = $"ROB RUN Türkçe Yama Yükleyicisi · sürüm {Program.Version}";

            BuildSteps();
            BuildIntro();
            BuildRisks();
            BuildInstall();
            BuildDone();

            MinButton.Click += (s, e) => WindowState = WindowState.Minimized;
            CloseButton.Click += (s, e) => Close();
            if (rel.Discord != null) SidebarDiscord.Click += (s, e) => OpenUrl(rel.Discord);
            else SidebarDiscord.Visibility = Visibility.Collapsed;
            ChangeFolder.Click += (s, e) => Browse();
            ToggleLog.Click += (s, e) =>
            {
                bool show = LogBox.Visibility != Visibility.Visible;
                LogBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                ToggleLog.Content = show ? "Ayrıntıları gizle" : "Ayrıntıları göster";
            };
            AcceptSwitch.Checked += (s, e) => SheetAccept.IsEnabled = true;
            AcceptSwitch.Unchecked += (s, e) => SheetAccept.IsEnabled = false;
            SheetDecline.Click += (s, e) => { HideSheet(); };
            SheetAccept.Click += (s, e) => { accepted = true; HideSheet(); Go(2); };

            SourceInitialized += (s, e) => ApplyWindowStyle();
            Closing += (s, e) => { if (busy) { e.Cancel = true; Alert("İşlem sürüyor", "Lütfen kurulumun bitmesini bekle. Yarıda kesmek oyun dosyalarını tutarsız bırakabilir.", "Tamam"); } };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && SheetLayer.Visibility == Visibility.Visible && !busy) HideSheet(); };

            SetGame(gameArg ?? GameLocator.Find());

            if (autoAction == "kur" || autoAction == "kaldir")
            {
                accepted = autoAction == "kur";
                Go(2, animate: false);
                ContentRendered += (s, e) => { if (autoAction == "kur") StartInstall(); else StartUninstall(); };
            }
            else Go(0, animate: false);

            UpdateButton.Click += (s, e) => ApplyAppUpdate();
            if (autoAction == null && Updater.Enabled)
                ContentRendered += (s, e) => CheckForUpdates();
        }

        // ============================================================ GitHub güncellemesi

        /// <summary>Arka planda son sürümü denetler. Yeni çeviri sessizce indirilir; yeni program için kenar çubuğunda kart çıkar.</summary>
        void CheckForUpdates()
        {
            new Thread(() =>
            {
                UpdateInfo info;
                try { info = Updater.Check(8000); } catch { return; } // çevrimdışı/doğrulanamadı: sessizce geç
                Translations fresh = null;
                if (info != null && info.TrNewer(tr)) { try { fresh = Updater.UpdateTranslation(info); } catch { } }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    update = info;
                    if (fresh != null && !busy)
                    {
                        tr = fresh;
                        Toast("Çeviri güncellendi · sürüm " + fresh.Version);
                        if (page == 2) RefreshStatus();
                    }
                    if (info != null && info.AppNewer)
                    {
                        UpdateTitle.Text = "Yeni sürüm hazır · " + info.App;
                        UpdateText.Text = "GitHub'dan indirilir; dijital imzası doğrulanmadan kurulmaz.";
                        UpdateCard.Visibility = Visibility.Visible;
                        UpdateCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
                    }
                }));
            }) { IsBackground = true }.Start();
        }

        void ApplyAppUpdate()
        {
            if (update == null || busy) return;
            busy = true;
            UpdateButton.IsEnabled = false; UpdateButton.Content = "İndiriliyor…";
            var info = update;
            new Thread(() =>
            {
                Exception err = null;
                try { Updater.UpdateApp(info, (game != null ? "--oyun " + Program.Quote(game) : "") + (backupRoot != null ? " --yedek-klasoru " + Program.Quote(backupRoot) : "")); }
                catch (Exception e) { err = e; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    busy = false;
                    if (err == null) { Application.Current.Shutdown(); return; }
                    UpdateButton.IsEnabled = true; UpdateButton.Content = "Güncelle";
                    Alert("Güncelleme yapılamadı", err.Message + "\n\nYeni sürümü GitHub sayfasından elle de indirebilirsin.", "Kapat",
                        info.Page != null ? "Sayfayı Aç" : null, info.Page != null ? (Action)(() => OpenUrl(info.Page)) : null);
                }));
            }) { IsBackground = true }.Start();
        }

        void CreateShortcuts()
        {
            try
            {
                Shortcuts.Create();
                if (game != null) AppSettings.Game = game;
                Toast("Masaüstüne \"ROB RUN Türkçe\" kısayolu eklendi");
                BuildFooter();
            }
            catch (Exception e) { Alert("Kısayol oluşturulamadı", e.Message, "Tamam"); }
        }

        void LaunchGame()
        {
            LauncherWindow.StartGame(false);
            Toast("ROB RUN Steam üzerinden açılıyor…");
        }

        // ============================================================ görsel yardımcılar

        static BitmapImage LoadImage(string name)
        {
            try
            {
                var st = typeof(MainWindow).Assembly.GetManifestResourceStream("assets/" + name);
                var img = new BitmapImage();
                img.BeginInit(); img.StreamSource = st; img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit(); img.Freeze();
                return img;
            }
            catch { return null; }
        }

        /// <summary>iOS "Ayarlar" tarzı renkli yuvarlatılmış kare simge.</summary>
        static Border IconSquare(string glyph, Color color, double size = 30, double glyphSize = 14)
        {
            var light = Color.FromArgb(255, (byte)Math.Min(255, color.R + 28), (byte)Math.Min(255, color.G + 28), (byte)Math.Min(255, color.B + 28));
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24),
                Background = new LinearGradientBrush(light, color, 90),
                Child = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = glyphSize, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        }

        static Border ImageSquare(ImageSource img, double size = 30) =>
            new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24), Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill } };

        sealed class Row
        {
            public string Glyph, Title, Detail, Value; public Color Tint; public Action Click; public bool Mono; public ImageSource Image;
        }

        /// <summary>Gruplanmış liste (iOS Ayarlar): satırlar, ince ayırıcılar, üzerine gelince vurgu, sağda değer/ok.</summary>
        static void FillList(Border host, IList<Row> rows)
        {
            var stack = new StackPanel();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var radius = rows.Count == 1 ? new CornerRadius(12) : i == 0 ? new CornerRadius(12, 12, 0, 0) : i == rows.Count - 1 ? new CornerRadius(0, 0, 12, 12) : new CornerRadius(0);
                var bg = new Border { CornerRadius = radius, Background = Brushes.Transparent, Padding = new Thickness(14, 11, 14, 11) };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var icon = r.Image != null ? ImageSquare(r.Image) : IconSquare(r.Glyph, r.Tint);
                icon.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(icon);
                var texts = new StackPanel { Margin = new Thickness(13, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                texts.Children.Add(new TextBlock { Text = r.Title, FontSize = 14, Foreground = new SolidColorBrush(C("#F5F5F7")), TextWrapping = TextWrapping.Wrap });
                if (!string.IsNullOrEmpty(r.Detail))
                {
                    var d = new TextBlock
                    {
                        Text = r.Detail, FontSize = r.Mono ? 11.5 : 12, Margin = new Thickness(0, 2, 0, 0),
                        Foreground = new SolidColorBrush(C("#98989F")), TextWrapping = r.Mono ? TextWrapping.NoWrap : TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    if (r.Mono) d.FontFamily = Mono;
                    texts.Children.Add(d);
                }
                Grid.SetColumn(texts, 1); grid.Children.Add(texts);
                var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (r.Value != null) right.Children.Add(new TextBlock { Text = r.Value, FontSize = 13.5, Foreground = new SolidColorBrush(C("#98989F")), VerticalAlignment = VerticalAlignment.Center });
                if (r.Click != null) right.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 11, Foreground = new SolidColorBrush(C("#6C6C70")), Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(right, 2); grid.Children.Add(right);
                bg.Child = grid;
                if (r.Click != null)
                {
                    bg.Cursor = Cursors.Hand;
                    var hover = new SolidColorBrush(C("#00FFFFFF"));
                    bg.Background = hover;
                    bg.MouseEnter += (s, e) => hover.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(C("#14FFFFFF"), TimeSpan.FromMilliseconds(120)));
                    bg.MouseLeave += (s, e) => hover.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(C("#00FFFFFF"), TimeSpan.FromMilliseconds(200)));
                    bg.MouseLeftButtonDown += (s, e) => hover.Color = C("#22FFFFFF");
                    var action = r.Click;
                    bg.MouseLeftButtonUp += (s, e) => { hover.Color = C("#14FFFFFF"); action(); };
                }
                stack.Children.Add(bg);
                if (i < rows.Count - 1)
                    stack.Children.Add(new Rectangle { Height = 1, Fill = new SolidColorBrush(C("#38383A")), Margin = new Thickness(57, 0, 0, 0) });
            }
            host.Child = stack;
        }

        static Border Tile(string glyph, Color tint, string big, string label, bool bigIsTitle = false)
        {
            var sp = new StackPanel();
            sp.Children.Add(IconSquare(glyph, tint, 30, 13));
            if (!bigIsTitle)
            {
                sp.Children.Add(new TextBlock { Text = big, FontFamily = Display, FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 0) });
                sp.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(C("#98989F")), TextWrapping = TextWrapping.Wrap });
            }
            else
            {
                sp.Children.Add(new TextBlock { Text = big, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
                sp.Children.Add(new TextBlock { Text = label, FontSize = 12, LineHeight = 17, Foreground = new SolidColorBrush(C("#98989F")), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            }
            return new Border { Background = new SolidColorBrush(C("#2C2C2E")), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 15, 14, 15), Margin = new Thickness(0, 0, 12, 0), Child = sp };
        }

        // ============================================================ sayfaları kur

        readonly List<(Ellipse dot, TextBlock mark, TextBlock label)> stepItems = new List<(Ellipse, TextBlock, TextBlock)>();
        void BuildSteps()
        {
            for (int i = 0; i < stepNames.Length; i++)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 18) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var dot = new Ellipse { Width = 20, Height = 20, StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                var mark = new TextBlock { FontFamily = Icons, FontSize = 9.5, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 20, TextAlignment = TextAlignment.Center };
                var label = new TextBlock { Text = stepNames[i], FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 } };
                var dotHost = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
                dotHost.Children.Add(dot); dotHost.Children.Add(mark);
                g.Children.Add(dotHost);
                Grid.SetColumn(label, 1); g.Children.Add(label);
                StepList.Children.Add(g);
                stepItems.Add((dot, mark, label));
            }
        }

        void UpdateSteps()
        {
            for (int i = 0; i < stepItems.Count; i++)
            {
                var (dot, mark, label) = stepItems[i];
                bool done = i < page || (page == 3 && i == 3), active = i == page && page != 3;
                dot.Fill = done ? new SolidColorBrush(Green) : active ? new SolidColorBrush(Blue) : Brushes.Transparent;
                dot.Stroke = done ? new SolidColorBrush(Green) : active ? new SolidColorBrush(Blue) : new SolidColorBrush(C("#80FFFFFF"));
                mark.Text = done ? "" : "";
                label.Foreground = new SolidColorBrush(active ? C("#FFFFFF") : done ? C("#E5E5EA") : C("#B0B0B6"));
                label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                if (active) dot.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Color = Blue, Opacity = 0.8 };
                else dot.Effect = null;
            }
        }

        void BuildIntro()
        {
            StatGrid.Children.Add(Tile("", Blue, "4.822", "Türkçe metin"));
            StatGrid.Children.Add(Tile("", Indigo, "6", "Yeni harf: ı İ ş Ş ğ Ğ"));
            var last = Tile("", Green, "1 tık", "Tamamen geri alma");
            last.Margin = new Thickness(0);
            StatGrid.Children.Add(last);

            var rows = new List<Row>();
            if (rel.GitHub != null) rows.Add(new Row { Glyph = "", Tint = Gray, Title = "Kaynak kodu", Detail = "GitHub'da herkese açık", Click = () => OpenUrl(rel.GitHub) });
            else rows.Add(new Row { Glyph = "", Tint = Gray, Title = "Kaynak kodu", Detail = "GitHub'da herkese açık yayımlanır" });
            if (ownSha != null)
            {
                rows.Add(new Row { Glyph = "", Tint = Blue, Title = "VirusTotal taraması", Detail = "Bu dosyanın tarama raporunu aç", Click = () => OpenUrl("https://www.virustotal.com/gui/file/" + ownSha) });
                rows.Add(new Row { Glyph = "", Tint = Indigo, Title = "SHA-256 özeti", Detail = ownSha, Mono = true, Value = "Kopyala", Click = () => { try { Clipboard.SetText(ownSha); Toast("Özet panoya kopyalandı"); } catch { } } });
            }
            rows.Add(new Row { Glyph = "", Tint = Green, Title = "Oyun klasörüne dokunmaz", Detail = "Türkçe dosyalar oyunun kendi önbellek klasörüne kurulur; oyun kodu, bellek ve anti-cheat değişmez." });
            rows.Add(new Row { Glyph = "\uE774", Tint = Teal, Title = "Yalnızca GitHub'a bağlanır", Detail = Updater.Enabled
                ? "Yeni çeviri ve sürümleri buradan alır; dijital imzası doğrulanmayan dosya kullanılmaz. Telemetri yok."
                : "Bu derlemede güncelleme denetimi kapalı; program internete hiç bağlanmaz." });
            FillList(TrustList, rows);
        }

        void BuildRisks()
        {
            var t1 = Tile("", Red, "Hesap riski", "Easy Anti-Cheat değiştirilmiş dosyayı fark edebilir; ban garantisi yoktur.", bigIsTitle: true);
            var t2 = Tile("", Orange, "Kullanım koşulları", "Oyunun EULA'sı dosya değişikliğini ve çeviriyi yasaklar.", bigIsTitle: true);
            var t3 = Tile("", Green, "Geri alınabilir", "Kaldır deyince önbellek dosyaları silinir; oyun dosyaları hiç değişmez.", bigIsTitle: true);
            t3.Margin = new Thickness(0);
            RiskTiles.Children.Add(t1); RiskTiles.Children.Add(t2); RiskTiles.Children.Add(t3);
            FillRiskText(RiskText, Program.ReadResource("RISKLER.md"));
        }

        void BuildInstall()
        {
            FillList(FileList, new List<Row>
            {
                new Row { Glyph = "", Tint = Blue, Title = "Türkçe metin paketi", Detail = "4.822 metin · oyunun İngilizce paketinden üretilir" },
                new Row { Glyph = "", Tint = Indigo, Title = "Türkçe yazı tipleri (ı İ ş Ş ğ Ğ)", Detail = "oyunun yazı tiplerine eksik harfler eklenir" },
                new Row { Glyph = "", Tint = Teal, Title = "Yönlendirme kataloğu", Detail = "oyunun kendi kataloğunun kopyası; yalnızca iki paketin adresi değişir" },
            });
        }

        void BuildDone()
        {
            var rows = new List<Row>
            {
                new Row { Glyph = "", Tint = Blue, Title = "Oyunu aç", Detail = "Ayarlar'da dil English seçili olmalı; metinler Türkçe görünür." },
                new Row { Glyph = "", Tint = Teal, Title = "\"ROB RUN Türkçe\" kısayolu", Detail = "Oyunu bu kısayolla açarsan güncellemeler denetlenir; oyun güncellendiyse Türkçe kendiliğinden yeniden uygulanır." },
            };
            if (rel.Discord != null) rows.Add(new Row { Image = LoadImage("logo.png"), Title = "Görüş ve hata bildir", Detail = "Rob Run: Türkiye Official Server Discord'u", Click = () => OpenUrl(rel.Discord) });
            FillList(NextList, rows);
        }

        /// <summary>RISKLER.md'yi biçimli metne çevirir (## başlık, - madde, **kalın**).</summary>
        static void FillRiskText(TextBlock tb, string md)
        {
            tb.Inlines.Clear();
            bool any = false;
            foreach (var raw in md.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("# ") || (line.Length == 0 && !any)) continue;
                if (line.StartsWith("## "))
                {
                    if (any) tb.Inlines.Add(new LineBreak());
                    tb.Inlines.Add(new Run(line.Substring(3)) { FontWeight = FontWeights.SemiBold, FontSize = 16, Foreground = Brushes.White });
                    tb.Inlines.Add(new LineBreak());
                }
                else if (line.StartsWith("- "))
                {
                    tb.Inlines.Add(new Run("•  ") { Foreground = new SolidColorBrush(Blue), FontWeight = FontWeights.Bold });
                    AddInline(tb, line.Substring(2));
                    tb.Inlines.Add(new LineBreak());
                }
                else if (line.Length == 0) tb.Inlines.Add(new LineBreak());
                else { AddInline(tb, line); tb.Inlines.Add(new LineBreak()); }
                any = true;
            }
        }

        static void AddInline(TextBlock tb, string text)
        {
            bool bold = false;
            foreach (var part in text.Replace("`", "").Split(new[] { "**" }, StringSplitOptions.None))
            {
                if (part.Length > 0) { var run = new Run(part) { FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal }; if (bold) run.Foreground = Brushes.White; tb.Inlines.Add(run); }
                bold = !bold;
            }
        }

        // ============================================================ gezinme

        void Go(int target, bool animate = true)
        {
            page = target;
            for (int i = 0; i < pages.Length; i++) pages[i].Visibility = i == target ? Visibility.Visible : Visibility.Collapsed;
            UpdateSteps();
            BuildFooter();
            if (target == 2) RefreshStatus();
            if (animate)
            {
                var el = pages[target];
                var tt = new TranslateTransform(28, 0);
                el.RenderTransform = tt;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
                el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            }
            if (target == 3) AnimateCheck();
        }

        Button Btn(string text, string style, Action click, bool enabled = true, double leftMargin = 10)
        {
            var b = new Button { Content = text, Style = (Style)FindResource(style), IsEnabled = enabled, Margin = new Thickness(leftMargin, 0, 0, 0) };
            b.Click += (s, e) => click();
            return b;
        }

        void BuildFooter()
        {
            FooterLeft.Children.Clear(); FooterRight.Children.Clear();
            switch (page)
            {
                case 0:
                    FooterLeft.Children.Add(Btn("Reddet ve Çık", "LinkButton", Close, leftMargin: 0));
                    FooterLeft.Children.Add(Btn("Yalnızca kaldırmak istiyorum", "LinkButton", () => Go(2), leftMargin: 18));
                    FooterRight.Children.Add(Btn("Devam", "PrimaryButton", () => Go(1)));
                    break;
                case 1:
                    FooterLeft.Children.Add(Btn("Reddet ve Çık", "LinkButton", Close, leftMargin: 0));
                    FooterRight.Children.Add(Btn("Geri", "SecondaryButton", () => Go(0)));
                    FooterRight.Children.Add(Btn("Devam", "PrimaryButton", () => { if (accepted) Go(2); else ShowSheet(); }));
                    break;
                case 2:
                    var st = game != null ? SafeStatus() : Installer.PatchStatus.NotInstalled;
                    FooterLeft.Children.Add(Btn("Kapat", "LinkButton", Close, enabled: !busy, leftMargin: 0));
                    if (st != Installer.PatchStatus.NotInstalled)
                        FooterLeft.Children.Add(Btn("Yamayı Kaldır", "DangerButton", StartUninstall, enabled: !busy && game != null, leftMargin: 18));
                    FooterRight.Children.Add(Btn("Geri", "SecondaryButton", () => Go(accepted ? 1 : 0), enabled: !busy));
                    if (accepted)
                        FooterRight.Children.Add(Btn(st == Installer.PatchStatus.NotInstalled ? "Kur" : "Yeniden Kur", "PrimaryButton", StartInstall, enabled: !busy && game != null));
                    else
                        FooterRight.Children.Add(Btn("Riskleri Oku", "PrimaryButton", () => Go(1), enabled: !busy));
                    break;
                case 3:
                    FooterLeft.Children.Add(Btn("Kurulum ekranına dön", "LinkButton", () => Go(2), leftMargin: 0));
                    FooterLeft.Children.Add(Btn("Kapat", "LinkButton", Close, leftMargin: 18));
                    if (SafeStatus() == Installer.PatchStatus.Installed)
                    {
                        FooterRight.Children.Add(Btn(Shortcuts.Exist ? "Kısayolu Yenile" : "Kısayol Oluştur", "SecondaryButton", CreateShortcuts));
                        FooterRight.Children.Add(Btn("ROB RUN'ı Türkçe Başlat", "PrimaryButton", LaunchGame));
                    }
                    else FooterRight.Children.Add(Btn("Kapat", "PrimaryButton", Close));
                    break;
            }
        }

        // ============================================================ kabul penceresi ve uyarılar

        void ShowSheet()
        {
            AcceptSwitch.IsChecked = false;
            SheetLayer.Visibility = Visibility.Visible;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            SheetMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-320, 0, TimeSpan.FromMilliseconds(340)) { EasingFunction = ease });
            SheetDim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            AcceptSwitch.Focus();
        }

        void HideSheet()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var a = new DoubleAnimation(0, -340, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease };
            a.Completed += (s, e) => SheetLayer.Visibility = Visibility.Collapsed;
            SheetMove.BeginAnimation(TranslateTransform.YProperty, a);
            SheetDim.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(230)));
        }

        /// <summary>Uygulama içi uyarı (macOS "alert" görünümü). primary null değilse iki düğmeli.</summary>
        void Alert(string title, string text, string okText, string primaryText = null, Action primary = null, bool danger = false)
        {
            var root = (Grid)WindowFrame.Child; // tüm pencereyi (kenar çubuğu dahil) kaplasın
            var layer = new Grid();
            Grid.SetColumnSpan(layer, 2);
            var dim = new Rectangle { Fill = new SolidColorBrush(C("#99000000")) };
            layer.Children.Add(dim);
            var card = new Border
            {
                Width = 400, Background = new SolidColorBrush(C("#2C2C2E")), CornerRadius = new CornerRadius(16), Padding = new Thickness(24, 22, 24, 18),
                BorderBrush = new SolidColorBrush(C("#3A3A3C")), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Direction = 270, Opacity = 0.55 },
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var sp = new StackPanel();
            sp.Children.Add(ImageSquare(LoadImage("logo.png"), 48));
            ((Border)sp.Children[0]).HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) });
            sp.Children.Add(new TextBlock { Text = text, FontSize = 13, LineHeight = 19, Foreground = new SolidColorBrush(C("#98989F")), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            void Dismiss()
            {
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
                fade.Completed += (s, e) => root.Children.Remove(layer);
                layer.BeginAnimation(OpacityProperty, fade);
            }
            var ok = new Button { Content = okText, Style = (Style)FindResource(primary != null ? "SecondaryButton" : "PrimaryButton") };
            ok.Click += (s, e) => Dismiss();
            buttons.Children.Add(ok);
            if (primary != null)
            {
                var p = new Button { Content = primaryText, Style = (Style)FindResource(danger ? "DangerButton" : "PrimaryButton"), Margin = new Thickness(10, 0, 0, 0) };
                p.Click += (s, e) => { Dismiss(); primary(); };
                buttons.Children.Add(p);
            }
            sp.Children.Add(buttons);
            card.Child = sp;
            var sc = new ScaleTransform(0.92, 0.92); card.RenderTransform = sc;
            layer.Children.Add(card);
            root.Children.Add(layer);
            var ease = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            layer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            ok.Focus();
        }

        /// <summary>Kısa süreli bilgi baloncuğu (alt ortada belirip kaybolur).</summary>
        void Toast(string text)
        {
            var root = (Grid)SheetLayer.Parent;
            var t = new Border
            {
                Background = new SolidColorBrush(C("#E63A3A3C")), CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 9, 18, 9),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 90),
                Child = new TextBlock { Text = text, FontSize = 13, Foreground = Brushes.White }, IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.4 }
            };
            Grid.SetRowSpan(t, 3);
            root.Children.Add(t);
            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1700))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2000))));
            anim.Completed += (s, e) => root.Children.Remove(t);
            t.BeginAnimation(OpacityProperty, anim);
        }

        void AnimateCheck()
        {
            var ease = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut };
            DoneScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
            DoneScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
            // onay işareti kendini çizer
            DoneCheck.StrokeDashArray = new DoubleCollection { 12, 12 };
            DoneCheck.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = TimeSpan.FromMilliseconds(260), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // ============================================================ durum ve işlemler

        void SetGame(string dir)
        {
            game = GameLocator.IsGameDir(dir) ? dir : null;
            GamePath.Text = game ?? "Oyun klasörü bulunamadı. \"Değiştir…\" ile ROB RUN'ın kurulu olduğu klasörü seç.";
            GamePath.Foreground = new SolidColorBrush(game != null ? C("#98989F") : Orange);
            RefreshStatus();
        }

        Installer.PatchStatus SafeStatus()
        {
            try { return new Installer(game, _ => { }, backupRoot).GetStatus(); }
            catch { return Installer.PatchStatus.NotInstalled; }
        }

        void SetPill(string text, Color c)
        {
            StatusPillText.Text = text;
            StatusPillText.Foreground = new SolidColorBrush(c);
            StatusPill.Background = new SolidColorBrush(Color.FromArgb(46, c.R, c.G, c.B));
        }

        void RefreshStatus()
        {
            if (busy) return;
            if (game == null) { SetPill("Klasör seçilmedi", Orange); return; }
            switch (SafeStatus())
            {
                case Installer.PatchStatus.Installed: SetPill("Türkçe yama kurulu", Green); InstallSubtitle.Text = "Türkçe yama bu oyunda kurulu. İstersen yeniden kurabilir ya da tek tıkla kaldırabilirsin."; break;
                case Installer.PatchStatus.Outdated: SetPill("Oyun güncellenmiş", Orange); InstallSubtitle.Text = "Oyun güncellenmiş; Türkçe kalkmış olabilir. \"Yeniden Kur\" ile tekrar uygula."; break;
                default: SetPill("Kurulu değil", Gray); InstallSubtitle.Text = accepted ? "Türkçe yama aşağıdaki oyuna kurulacak. Oyun klasöründeki hiçbir dosya değiştirilmez." : "Kurmak için önce riskleri okuyup kabul etmen gerekiyor."; break;
            }
        }

        void Browse()
        {
            if (busy) return;
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "ROB RUN'ın kurulu olduğu klasörü seç (içinde \"ROB RUN.exe\" olmalı)" })
            {
                if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                if (!GameLocator.IsGameDir(d.SelectedPath)) { Alert("Bu klasörde ROB RUN yok", "Seçtiğin klasörde \"ROB RUN.exe\" bulunamadı. Steam'de ROB RUN > Yönet > Yerel dosyalara göz at ile klasörü bulabilirsin.", "Tamam"); return; }
                SetGame(d.SelectedPath);
                BuildFooter();
            }
        }

        static readonly Regex StepLine = new Regex(@"^(\d)/(\d) (.+?)\.*$");
        void Log(string line)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<string>(Log), line); return; }
            LogBox.AppendText(line.TrimStart() + Environment.NewLine);
            LogBox.ScrollToEnd();
            var m = StepLine.Match(line.Trim());
            if (m.Success)
            {
                int n = int.Parse(m.Groups[1].Value), total = int.Parse(m.Groups[2].Value);
                ProgressStep.Text = $"Adım {n}/{total}";
                ProgressTitle.Text = m.Groups[3].Value + "…";
                SetProgress((n - 0.5) / total);
            }
        }

        void SetProgress(double fraction)
        {
            var parent = (FrameworkElement)ProgressFill.Parent;
            double w = Math.Max(0, parent.ActualWidth * Math.Min(1, fraction));
            ProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation(w, TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        /// <summary>Oyun klasörüne yazma izni yoksa programı yönetici olarak yeniden açar. true: iş yeni pencereye devredildi.</summary>
        bool HandOffToElevated(string action)
        {
            if (Program.CanWrite(GameLocator.BundleDir(game))) return false;
            Alert("Yönetici izni gerekiyor",
                "Oyun \"Program Files\" altında kurulu. Dosyaları değiştirmek için Windows izin isteyecek; program yönetici olarak yeniden açılıp işlemi orada yapacak.",
                "Vazgeç", "Devam", () =>
                {
                    string args = "--oyun " + Program.Quote(game) + " --otomatik " + action + (backupRoot != null ? " --yedek-klasoru " + Program.Quote(backupRoot) : "");
                    if (Program.RestartElevated(args)) Close();
                    else Toast("Yönetici izni verilmedi; hiçbir dosya değiştirilmedi");
                });
            return true;
        }

        void StartInstall()
        {
            if (game == null || !accepted || busy) return;
            var t = tr;
            Run("Kuruluyor", i => { i.Install(t, Program.Version + " / çeviri " + t.Version); AppSettings.Game = i.Game; },
                "Türkçe yama kuruldu", "ROB RUN artık Türkçe. Masaüstü kısayolu oluşturursan oyun güncellense bile Türkçe kendiliğinden yeniden uygulanır.");
        }

        void StartUninstall()
        {
            if (game == null || busy) return;
            void Do()
            {
                Run("Kaldırılıyor", i => { i.Uninstall(); Shortcuts.Remove(); }, "Yama kaldırıldı", "Türkçe dosyalar silindi; oyun bir sonraki açılışta İngilizce başlar. Fikrini değiştirirsen programı tekrar açıp kurabilirsin.");
            }
            if (autoAction == "kaldir") Do();
            else Alert("Türkçe yama kaldırılsın mı?", "Önbellekteki Türkçe dosyalar ve \"ROB RUN Türkçe\" kısayolları silinecek. Oyun dosyaları zaten hiç değişmedi.", "Vazgeç", "Kaldır", Do, danger: true);
        }

        void Run(string what, Action<Installer> work, string doneTitle, string doneBody)
        {
            busy = true;
            Go(2, animate: false);
            ProgressCard.Visibility = Visibility.Visible;
            ProgressTitle.Text = what + "…";
            ProgressStep.Text = "";
            ProgressFill.BeginAnimation(WidthProperty, null); ProgressFill.Width = 0;
            ProgressFill.Background = new SolidColorBrush(Blue);
            SetPill(what + "…", Blue);
            Dispatcher.BeginInvoke(new Action(() => { SetProgress(0.06); ((ScrollViewer)PageInstall).ScrollToEnd(); }), System.Windows.Threading.DispatcherPriority.Loaded);
            Log($"— {what} ({DateTime.Now:HH:mm:ss})");
            var inst = new Installer(game, Log, backupRoot);
            var t = new Thread(() =>
            {
                Exception err = null;
                try { work(inst); } catch (Exception e) { err = e; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    busy = false;
                    if (err == null)
                    {
                        SetProgress(1);
                        ProgressTitle.Text = "Tamamlandı"; ProgressStep.Text = "";
                        ProgressFill.Background = new SolidColorBrush(Green);
                        DoneTitle.Text = doneTitle; DoneText.Text = doneBody;
                        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
                        timer.Tick += (s, e) => { timer.Stop(); Go(3); };
                        timer.Start();
                    }
                    else
                    {
                        ProgressFill.Background = new SolidColorBrush(Red);
                        ProgressTitle.Text = "İşlem tamamlanamadı";
                        Log("HATA: " + err.Message);
                        Go(2, animate: false);
                        SetPill("Hata", Red);
                        Alert("İşlem tamamlanamadı", err.Message, "Tamam");
                    }
                }));
            }) { IsBackground = true };
            t.Start();
        }

        void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { Toast(url); }
        }

        // ============================================================ pencere

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        void ApplyWindowStyle()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            try
            {
                int dark = 1; DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                int round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, 4);       // Windows 11: yuvarlak köşeler
                int border = 0x003C3A3A; DwmSetWindowAttribute(hwnd, 34, ref border, 4);
            }
            catch { }
        }

        /// <summary>Geliştirme: sayfaların PNG görüntülerini kaydeder (pencere ekran dışında açılır).</summary>
        public static void CaptureScreens(string dir, string game, string backupRoot)
        {
            Directory.CreateDirectory(dir);
            var w = new MainWindow(game, null, backupRoot) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -6000, Top = 0, ShowInTaskbar = false, ShowActivated = false };
            w.Show();
            void Shot(string name)
            {
                for (int k = 0; k < 40; k++) { DoEvents(); Thread.Sleep(15); }
                var el = (FrameworkElement)w.Content;
                var dpi = VisualTreeHelper.GetDpi(w);
                var bmp = new RenderTargetBitmap((int)(el.ActualWidth * dpi.DpiScaleX), (int)(el.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                bmp.Render(el);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var f = File.Create(System.IO.Path.Combine(dir, name))) enc.Save(f);
            }
            w.Go(0, false); Shot("sayfa1.png");
            w.Go(1, false); Shot("sayfa2.png");
            w.ShowSheet(); w.AcceptSwitch.IsChecked = true; Shot("sayfa2-kabul.png"); w.SheetLayer.Visibility = Visibility.Collapsed;
            w.accepted = true; w.Go(2, false);
            w.ProgressCard.Visibility = Visibility.Visible; w.Log("2/4 Eksik Türkçe harfler yazı tiplerine ekleniyor..."); Shot("sayfa3.png");
            w.Go(3, false); Shot("sayfa4.png");
            w.UpdateTitle.Text = "Yeni sürüm hazır · 1.1.0"; w.UpdateText.Text = "GitHub'dan indirilir; dijital imzası doğrulanmadan kurulmaz.";
            w.UpdateCard.Visibility = Visibility.Visible; w.Go(0, false); Shot("guncelleme.png"); w.UpdateCard.Visibility = Visibility.Collapsed;
            w.Alert("Türkçe yama kaldırılsın mı?", "Orijinal oyun dosyaları yedekten geri yüklenecek.", "Vazgeç", "Kaldır", () => { }, danger: true); Shot("uyari.png");
            w.Close();
        }

        static void DoEvents()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
    }

    /// <summary>Derleme sırasında gömülen release.json: GitHub/Discord bağlantıları (boşsa gösterilmez).</summary>
    public sealed class ReleaseInfo
    {
        public string GitHub, Discord;
        public static ReleaseInfo Load()
        {
            try
            {
                var o = (Json.Obj)Json.Parse(Program.ReadResource("release.json"));
                return new ReleaseInfo { GitHub = SafeUrl(o["github"] as string), Discord = SafeUrl(o["discord"] as string) };
            }
            catch { return new ReleaseInfo(); }
        }
        static string SafeUrl(string u) => u != null && u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? u : null;
    }
}
