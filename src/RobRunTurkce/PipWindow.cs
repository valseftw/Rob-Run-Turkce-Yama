using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RobRunTurkce
{
    /// <summary>
    /// Ayrı küçük pencere (PiP): rehberdeki bir kaydı, soygun hazırlık listesini ya da Looty Bay haritasını
    /// oyunun üstünde açık tutmak için. Sürüklenir, boyutlandırılır, üstte tutma ve saydamlık ayarlanır.
    /// Not: Oyun "tam ekran (exclusive)" modundaysa Windows hiçbir pencereyi üstünde gösteremez;
    /// oyunda "Kenarlıksız pencere" (borderless) modu seçilmelidir.
    /// </summary>
    public sealed class PipWindow : Window
    {
        readonly WikiView owner;
        readonly Stack<object> history = new Stack<object>();
        readonly ScrollViewer body = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 12, 8, 8) };
        readonly Border card = new Border();
        readonly TextBlock heading = new TextBlock();
        readonly Border back;
        readonly WikiMap map;
        object current;
        int opacityStep;

        static readonly List<PipWindow> open = new List<PipWindow>();
        /// <summary>Geliştirme/ekran görüntüsü: en son açılan pencere.</summary>
        internal static PipWindow Last => open.Count > 0 ? open[open.Count - 1] : null;
        static Brush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        /// <summary>Kaydı ayrı pencerede açar (aynı kayıt zaten açıksa o pencereyi öne getirir).</summary>
        public static void Open(WikiView owner, object target)
        {
            if (target == null) return;
            foreach (var p in open) if (p.map == null && ReferenceEquals(p.current, target)) { p.Activate(); return; }
            var w = new PipWindow(owner, null);
            w.Navigate(target, push: false);
            w.Show();
        }

        /// <summary>Looty Bay haritasını ayrı pencerede açar.</summary>
        public static void OpenMap(WikiView owner, WMap m, Wiki wiki)
        {
            foreach (var p in open) if (p.map != null) { p.Activate(); return; }
            var w = new PipWindow(owner, new WikiMap(m, wiki)) { Width = 640, Height = 560 };
            w.heading.Text = "LOOTY BAY HARİTASI";
            w.Show();
        }

        PipWindow(WikiView owner, WikiMap map)
        {
            this.owner = owner; this.map = map;
            Title = "ROB RUN Rehberi";
            Width = 400; Height = 640; MinWidth = 300; MinHeight = 260;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.CanResizeWithGrip;
            Topmost = true; Background = Brushes.Transparent; ShowInTaskbar = true;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); Foreground = B("#F5F5F7");
            try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(typeof(PipWindow).Assembly.GetManifestResourceStream("assets/logo.png")); } catch { }
            WindowStartupLocation = WindowStartupLocation.Manual;
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 24 - open.Count * 28; Top = wa.Top + 60 + open.Count * 28;

            // başlık çubuğu
            var bar = new Grid { Height = 40, Background = Brushes.Transparent };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            back = Btn("", "Geri", () => { if (history.Count > 0) Navigate(history.Pop(), push: false); });
            back.Visibility = Visibility.Collapsed; back.Margin = new Thickness(8, 0, 0, 0);
            bar.Children.Add(back);
            heading.FontSize = 11.5; heading.FontWeight = FontWeights.SemiBold; heading.Foreground = B("#98989F"); heading.VerticalAlignment = VerticalAlignment.Center; heading.Margin = new Thickness(12, 0, 0, 0); heading.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(heading, 1); bar.Children.Add(heading);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0) };
            Border pin = null;
            pin = Btn("", "Üstte tut (açık)", () => { Topmost = !Topmost; pin.Opacity = Topmost ? 1 : 0.45; pin.ToolTip = Topmost ? "Üstte tut (açık)" : "Üstte tut (kapalı)"; });
            btns.Children.Add(pin);
            btns.Children.Add(Btn("", "Saydamlık", () => { opacityStep = (opacityStep + 1) % 3; Opacity = new[] { 1.0, 0.85, 0.7 }[opacityStep]; }));
            btns.Children.Add(Btn("", "Ana pencerede aç", () => { if (current != null) { owner.GoToPage(current); Application.Current.MainWindow?.Activate(); } }));
            btns.Children.Add(Btn("", "Kapat", Close));
            Grid.SetColumn(btns, 2); bar.Children.Add(btns);
            bar.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 1) try { DragMove(); } catch { } };

            var dock = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar);
            dock.Children.Add(new System.Windows.Shapes.Rectangle { Height = 1, Fill = B("#38383A"), VerticalAlignment = VerticalAlignment.Top });
            DockPanel.SetDock(dock.Children[1], Dock.Top);
            if (map != null)
            {
                // harita penceresi: harita + sağda seçilen kaydın kartı
                var g = new Grid();
                g.Children.Add(map);
                card.Child = body; card.Width = 300; card.HorizontalAlignment = HorizontalAlignment.Right; card.Margin = new Thickness(8);
                card.Background = B("#EB1C1C1E"); card.CornerRadius = new CornerRadius(12); card.BorderBrush = B("#3A3A3C"); card.BorderThickness = new Thickness(1);
                card.Visibility = Visibility.Collapsed;
                g.Children.Add(card);
                map.Selected += o => { map.RightInset = 316; Navigate(o, push: false); card.Visibility = Visibility.Visible; };
                dock.Children.Add(g);
            }
            else dock.Children.Add(body);

            Content = new Border
            {
                Background = B("#1C1C1E"), CornerRadius = new CornerRadius(14), BorderBrush = B("#48484A"), BorderThickness = new Thickness(1), Child = dock,
                Margin = new Thickness(10), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.5 },
            };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Loaded += (s, e) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            open.Add(this);
            Closed += (s, e) => open.Remove(this);
        }

        Border Btn(string glyph, string tip, Action click)
        {
            var b = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, Margin = new Thickness(2, 0, 0, 0),
                Child = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 12, Foreground = B("#F5F5F7"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            b.MouseEnter += (s, e) => b.Background = B("#26FFFFFF");
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonDown += (s, e) => e.Handled = true; // başlık sürüklemesini tetiklemesin
            b.MouseLeftButtonUp += (s, e) => { click(); e.Handled = true; };
            return b;
        }

        /// <summary>Kaydı bu pencerede gösterir; içindeki bağlantılar da burada açılır.</summary>
        void Navigate(object target, bool push = true)
        {
            if (push && current != null && !ReferenceEquals(current, target)) history.Push(current);
            current = target;
            if (map == null) { heading.Text = WikiView.KindLabel(target) + " · " + WikiView.TitleOf(target); Title = WikiView.TitleOf(target) + " · ROB RUN Rehberi"; }
            back.Visibility = history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            body.Content = owner.BuildDetail(target, o => Navigate(o));
            body.ScrollToTop();
        }
    }
}
