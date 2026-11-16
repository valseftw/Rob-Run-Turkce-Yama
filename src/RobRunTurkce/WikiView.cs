using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace RobRunTurkce
{
    /// <summary>ListBox satırı (Theme.xaml'daki WikiRow şablonuna bağlanır).</summary>
    public sealed class WRow
    {
        public string Title { get; set; }
        public string Sub { get; set; }
        public string Value { get; set; }
        public string IconName;
        public ImageSource Icon => Wiki.Icon(IconName);
        public string Initial => string.IsNullOrEmpty(Title) ? "" : Title.Substring(0, 1);
        public Brush Accent { get; set; } = Brushes.Transparent;
        public object Tag;
        public string Search; // küçük harfli arama metni
    }

    /// <summary>
    /// Rehber: ROB RUN Türkçe Wiki verisinin uygulama içi görünümü. Solda arama + filtre + liste, sağda ayrıntı.
    /// Kenar çubuğundaki bölüm listesi (Nav) de burada üretilir.
    /// </summary>
    public sealed class WikiView : Grid
    {
        readonly Wiki w;
        public readonly StackPanel Nav = new StackPanel();
        readonly TextBlock title = new TextBlock(), subtitle = new TextBlock();
        readonly TextBox search = new TextBox();
        readonly TextBlock searchHint = new TextBlock();
        readonly StackPanel chips = new StackPanel { Orientation = Orientation.Horizontal };
        readonly ScrollViewer chipScroll;
        readonly ListBox list = new ListBox();
        readonly ScrollViewer detail = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly TextBlock empty = new TextBlock();
        int section = -1;
        string chip; // seçili filtre (null = tümü)
        List<WRow> rows = new List<WRow>();
        readonly List<Border> navItems = new List<Border>();

        static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
        static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        static Brush B(string hex) { var b = new SolidColorBrush(C(hex)); b.Freeze(); return b; }
        static Brush Rarity(string hex) { try { return string.IsNullOrEmpty(hex) ? Brushes.Transparent : B(hex); } catch { return Brushes.Transparent; } }
        static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");
        static readonly Brush Label = B("#F5F5F7"), Secondary = B("#98989F"), Surface = B("#2C2C2E"), Sep = B("#38383A"), Blue = B("#0A84FF");

        static readonly (string name, string glyph, string tint)[] Sections =
        {
            ("Eşyalar", "", "#0A84FF"), ("Silahlar", "", "#FF453A"), ("Larry'nin Ağacı", "", "#30D158"),
            ("Takaslar", "", "#FF9F0A"), ("Binalar", "", "#5E5CE6"), ("Soygunlar", "", "#BF5AF2"),
        };

        public WikiView(Wiki wiki)
        {
            w = wiki;
            Margin = new Thickness(32, 4, 28, 20);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            ColumnDefinitions.Add(new ColumnDefinition());

            // --- başlık + arama
            var head = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tstack = new StackPanel();
            title.FontFamily = Display; title.FontSize = 26; title.FontWeight = FontWeights.Bold; title.Foreground = Label;
            subtitle.FontSize = 12.5; subtitle.Foreground = Secondary; subtitle.Margin = new Thickness(0, 2, 0, 0);
            tstack.Children.Add(title); tstack.Children.Add(subtitle);
            head.Children.Add(tstack);
            var sb = SearchBox(); Grid.SetColumn(sb, 1); sb.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(sb);
            SetColumnSpan(head, 3); Children.Add(head);

            // --- filtre çipleri
            chipScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = chips, Margin = new Thickness(0, 0, 0, 12) };
            chipScroll.PreviewMouseWheel += (s, e) => { chipScroll.ScrollToHorizontalOffset(chipScroll.HorizontalOffset - e.Delta / 2.0); e.Handled = true; };
            SetRow(chipScroll, 1); SetColumnSpan(chipScroll, 3); Children.Add(chipScroll);

            // --- liste
            list.Background = Brushes.Transparent; list.BorderThickness = new Thickness(0);
            list.ItemTemplate = (DataTemplate)Application.Current.FindResource("WikiRow");
            list.ItemContainerStyle = (Style)Application.Current.FindResource("WikiListItem");
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            list.SelectionChanged += (s, e) => { if (list.SelectedItem is WRow r) ShowDetail(r.Tag); };
            var listCard = new Border { Background = B("#161618"), CornerRadius = new CornerRadius(14), Padding = new Thickness(6, 6, 2, 6), Child = list };
            SetRow(listCard, 2); Children.Add(listCard);
            empty.Text = "Sonuç yok"; empty.Foreground = Secondary; empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center; empty.Visibility = Visibility.Collapsed;
            SetRow(empty, 2); Children.Add(empty);

            // --- ayrıntı
            SetRow(detail, 2); SetColumn(detail, 2); Children.Add(detail);

            BuildNav();
            Show(0);
        }

        // ============================================================ kenar çubuğu bölüm listesi

        void BuildNav()
        {
            int[] counts = { w.Items.Count, w.Weapons.Count, w.Branches.Sum(b => b.Nodes.Count), w.Barters.Count, w.Buildings.Count, w.Robberies.Count };
            for (int i = 0; i < Sections.Length; i++)
            {
                int idx = i;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var tint = C(Sections[i].tint);
                var light = Color.FromArgb(255, (byte)Math.Min(255, tint.R + 28), (byte)Math.Min(255, tint.G + 28), (byte)Math.Min(255, tint.B + 28));
                g.Children.Add(new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(6.5), Background = new LinearGradientBrush(light, tint, 90),
                    Child = new TextBlock { Text = Sections[i].glyph, FontFamily = Icons, FontSize = 12.5, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                });
                var name = new TextBlock { Text = Sections[i].name, FontSize = 14, Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Label };
                Grid.SetColumn(name, 1); g.Children.Add(name);
                var cnt = new TextBlock { Text = counts[i].ToString("N0", Tr), FontSize = 12, Foreground = Secondary, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(cnt, 2); g.Children.Add(cnt);
                var item = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 6, 10, 6), Margin = new Thickness(0, 0, 0, 3), Background = Brushes.Transparent, Child = g, Cursor = Cursors.Hand };
                item.MouseEnter += (s, e) => { if (section != idx) item.Background = B("#1FFFFFFF"); };
                item.MouseLeave += (s, e) => { if (section != idx) item.Background = Brushes.Transparent; };
                item.MouseLeftButtonUp += (s, e) => Show(idx);
                navItems.Add(item);
                Nav.Children.Add(item);
            }
            var foot = new TextBlock
            {
                Text = $"Kaynak: ROB RUN Türkçe Wiki\nVeri sürümü {w.Version}" + (w.GameVersion != null ? " · oyun " + w.GameVersion : ""),
                FontSize = 11, Foreground = Secondary, Margin = new Thickness(8, 14, 0, 0), TextWrapping = TextWrapping.Wrap,
            };
            Nav.Children.Add(foot);
        }

        void UpdateNav()
        {
            for (int i = 0; i < navItems.Count; i++)
                navItems[i].Background = i == section ? B("#3A0A84FF") : Brushes.Transparent;
        }

        // ============================================================ arama kutusu

        FrameworkElement SearchBox()
        {
            var g = new Grid { Width = 250, Height = 34 };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Surface, BorderBrush = B("#3A3A3C"), BorderThickness = new Thickness(1) });
            g.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 13, Foreground = Secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 1, 0, 0) });
            searchHint.Text = "Ara"; searchHint.Foreground = B("#6C6C70"); searchHint.FontSize = 13.5; searchHint.VerticalAlignment = VerticalAlignment.Center; searchHint.Margin = new Thickness(36, 0, 0, 0); searchHint.IsHitTestVisible = false;
            g.Children.Add(searchHint);
            search.Background = Brushes.Transparent; search.BorderThickness = new Thickness(0); search.Foreground = Label; search.CaretBrush = Blue;
            search.FontSize = 13.5; search.VerticalContentAlignment = VerticalAlignment.Center; search.Margin = new Thickness(32, 0, 10, 0);
            search.SelectionBrush = Blue; search.FocusVisualStyle = null;
            search.TextChanged += (s, e) => { searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; ApplyFilter(); };
            search.KeyDown += (s, e) => { if (e.Key == Key.Escape) search.Text = ""; else if (e.Key == Key.Down && list.Items.Count > 0) { list.SelectedIndex = Math.Max(0, list.SelectedIndex); (list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex) as UIElement)?.Focus(); } };
            g.Children.Add(search);
            return g;
        }

        // ============================================================ bölümler

        public void Show(int sec, object select = null)
        {
            bool changed = sec != section;
            section = sec;
            UpdateNav();
            if (changed) { chip = null; search.Text = ""; }
            // Larry'nin ağacı: dallar karışmasın diye her zaman bir dal seçili (seçilen düğümün dalı)
            if (sec == 2 && w.Branches.Count > 0)
                chip = (select as WNode)?.Branch.Name ?? (changed || chip == null ? w.Branches[0].Name : chip);
            title.Text = Sections[sec].name;
            rows = BuildRows(sec);
            BuildChips(sec);
            ApplyFilter(select);
            if (changed)
            {
                var tt = new TranslateTransform(0, 10); RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(220)));
            }
        }

        string Lower(params string[] parts) => string.Join(" ", parts.Where(p => p != null)).ToLower(Tr);

        List<WRow> BuildRows(int sec)
        {
            switch (sec)
            {
                case 0:
                    return w.Items.OrderBy(i => i.Name, StringComparer.Create(Tr, true)).Select(i => new WRow
                    {
                        Title = i.Name, Sub = (i.Category ?? "") + (i.Rarity != null ? " · " + i.Rarity : ""), Value = Price(i.Price), IconName = i.Icon,
                        Accent = Rarity(i.Color), Tag = i, Search = Lower(i.Name, i.En, i.Category, i.Rarity, i.Id),
                    }).ToList();
                case 1:
                    return w.Weapons.OrderBy(x => x.Class).ThenBy(x => x.Name).Select(x => new WRow
                    {
                        Title = x.Name, Sub = x.Class + (x.Rarity != null ? " · " + x.Rarity : ""), Value = x.Dps != null ? "DPS " + Num(x.Dps) : null, IconName = x.Icon,
                        Accent = Rarity(x.Color), Tag = x, Search = Lower(x.Name, x.Family, x.Class, x.Rarity),
                    }).ToList();
                case 2:
                    return w.Branches.SelectMany(b => b.Nodes.OrderBy(n => n.Stage)).Select(n => new WRow
                    {
                        Title = n.Name, Sub = $"{n.Stage}. aşama" + (n.Research > 0 ? " · araştırma " + Wiki.Money(n.Research) : ""), Value = Wiki.Money(n.Price), IconName = n.Icon,
                        Accent = Rarity(n.Color), Tag = n, Search = Lower(n.Name, n.En, n.Branch.Name),
                    }).ToList();
                case 3:
                    return w.Barters.Select(b => new WRow
                    {
                        Title = b.Name, Sub = b.Npc, Value = b.Inputs.Count + " girdi", IconName = b.Icon, Tag = b,
                        Accent = Rarity(b.ItemId != null && w.ItemById.TryGetValue(b.ItemId, out var it) ? it.Color : null),
                        Search = Lower(b.Name, b.Npc, string.Join(" ", b.Inputs.Select(x => x.Name))),
                    }).ToList();
                case 4:
                    return w.Buildings.OrderBy(b => b.Name, StringComparer.Create(Tr, true)).Select(b => new WRow
                    {
                        Title = b.Name, Sub = b.Type, Value = b.ItemIds.Count + " eşya", IconName = b.ItemIds.Select(id => w.ItemById.TryGetValue(id, out var it) ? it.Icon : null).FirstOrDefault(x => x != null),
                        Tag = b, Search = Lower(b.Name, b.En, b.Type),
                    }).ToList();
                default:
                    return w.Robberies.Select(r => new WRow
                    {
                        Title = r.Name, Sub = r.Group + (r.Status != null ? " · " + r.Status : ""), IconName = r.Icon, Tag = r, Accent = StatusBrush(r.StatusType),
                        Search = Lower(r.Name, r.Group, r.Place),
                    }).ToList();
            }
        }

        static Brush StatusBrush(string t) => t == "available" ? B("#30D158") : t == "closed" || t == "unavailable" ? B("#FF453A") : B("#FF9F0A");

        string ChipOf(WRow r)
        {
            switch (r.Tag)
            {
                case WItem i: return i.Category;
                case WWeapon x: return x.Class;
                case WNode n: return n.Branch.Name;
                case WBuilding b: return b.Type;
                case WRobbery rb: return rb.Group;
                default: return null;
            }
        }

        void BuildChips(int sec)
        {
            chips.Children.Clear();
            var names = rows.Select(ChipOf).Where(x => x != null).GroupBy(x => x).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
            if (sec == 2) names = w.Branches.Select(b => b.Name).ToList(); // ağaç: oyundaki dal sırası
            if (names.Count < 2) { chipScroll.Visibility = Visibility.Collapsed; return; }
            chipScroll.Visibility = Visibility.Visible;
            string group = "wikichip" + sec;
            void Add(string label, string value)
            {
                var rb = new RadioButton { Content = label, GroupName = group, Style = (Style)Application.Current.FindResource("Chip"), IsChecked = value == chip };
                rb.Checked += (s, e) => { chip = value; ApplyFilter(); };
                chips.Children.Add(rb);
            }
            if (sec != 2) Add("Tümü", null);
            foreach (var n in names) Add(n, n);
        }

        void ApplyFilter(object select = null)
        {
            var q = search.Text.Trim().ToLower(Tr);
            var shown = rows.Where(r => (chip == null || ChipOf(r) == chip) && (q.Length == 0 || r.Search.Contains(q))).ToList();
            list.ItemsSource = shown;
            subtitle.Text = shown.Count == rows.Count ? $"{rows.Count.ToString("N0", Tr)} kayıt" : $"{shown.Count.ToString("N0", Tr)} / {rows.Count.ToString("N0", Tr)} kayıt";
            empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var target = select != null ? shown.FirstOrDefault(r => ReferenceEquals(r.Tag, select)) : null;
            if (target == null && select != null)
            {
                // seçilecek kayıt filtre dışında kaldıysa filtreyi kaldır
                chip = null; search.Text = ""; shown = rows; list.ItemsSource = shown;
                foreach (RadioButton rb in chips.Children) rb.IsChecked = rb.Content as string == "Tümü";
                target = shown.FirstOrDefault(r => ReferenceEquals(r.Tag, select));
            }
            list.SelectedItem = target ?? shown.FirstOrDefault();
            if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
            else detail.Content = null;
        }

        // ============================================================ ayrıntı paneli

        void ShowDetail(object tag)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };
            switch (tag)
            {
                case WItem i: ItemDetail(sp, i); break;
                case WWeapon x: WeaponDetail(sp, x); break;
                case WNode n: NodeDetail(sp, n); break;
                case WBarter b: BarterDetail(sp, b); break;
                case WBuilding b: BuildingDetail(sp, b); break;
                case WRobbery r: RobberyDetail(sp, r); break;
            }
            detail.Content = sp;
            detail.ScrollToTop();
            sp.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }

        static string Num(double? v) => v == null ? "—" : v.Value.ToString("0.##", Tr);
        /// <summary>0 fiyat = bilinmiyor/satılmaz: gösterilmez.</summary>
        static string Price(long? v) => v > 0 ? Wiki.Money(v) : null;

        void Hero(StackPanel sp, string icon, string name, string en, string rarity, string color, string kind)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var glow = Rarity(color);
            var box = new Border
            {
                Width = 92, Height = 92, CornerRadius = new CornerRadius(20), Background = B("#2C2C2E"), BorderBrush = glow, BorderThickness = new Thickness(color != null ? 1.5 : 0),
                Child = new Image { Source = Wiki.Icon(icon), Margin = new Thickness(10), Stretch = Stretch.Uniform },
            };
            RenderOptions.SetBitmapScalingMode(box.Child, BitmapScalingMode.HighQuality);
            if (color != null) try { box.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = C(color), BlurRadius = 26, ShadowDepth = 0, Opacity = 0.35 }; } catch { }
            g.Children.Add(box);
            var t = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(new TextBlock { Text = name, FontFamily = Display, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Label, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(en) && !string.Equals(en, name, StringComparison.OrdinalIgnoreCase))
                t.Children.Add(new TextBlock { Text = en, FontSize = 13, Foreground = Secondary, Margin = new Thickness(0, 2, 0, 0) });
            var pills = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
            if (rarity != null) pills.Children.Add(Pill(rarity, color));
            if (kind != null) pills.Children.Add(Pill(kind, null));
            if (pills.Children.Count > 0) t.Children.Add(pills);
            Grid.SetColumn(t, 1); g.Children.Add(t);
            sp.Children.Add(g);
        }

        static Border Pill(string text, string color)
        {
            Color c; try { c = color != null ? C(color) : C("#98989F"); } catch { c = C("#98989F"); }
            return new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 6, 4), Background = new SolidColorBrush(Color.FromArgb(46, c.R, c.G, c.B)),
                Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(color != null ? c : C("#D1D1D6")) }
            };
        }

        static void Para(StackPanel sp, string text, double top = 16)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            sp.Children.Add(new TextBlock { Text = text, FontSize = 14, LineHeight = 21, Foreground = B("#D1D1D6"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) });
        }

        static void Header(StackPanel sp, string text) =>
            sp.Children.Add(new TextBlock { Text = text.ToUpper(Tr), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Secondary, Margin = new Thickness(4, 22, 0, 7) });

        /// <summary>İstatistik kutucukları (iOS widget görünümü).</summary>
        static void Tiles(StackPanel sp, IEnumerable<(string label, string value)> items, int columns = 4)
        {
            var list = items.Where(x => x.value != null).ToList();
            if (list.Count == 0) return;
            var ug = new UniformGrid { Columns = Math.Min(columns, list.Count), Margin = new Thickness(0, 18, 0, 0) };
            foreach (var (label, value) in list)
            {
                var s = new StackPanel();
                // Uzun değerler kutuya sığsın diye küçülür (kırpılmaz)
                s.Children.Add(new Viewbox
                {
                    Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Height = 26,
                    Child = new TextBlock { Text = value, FontFamily = Display, FontSize = 19, FontWeight = FontWeights.Bold, Foreground = Label },
                });
                s.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = Secondary, Margin = new Thickness(0, 1, 0, 0), TextWrapping = TextWrapping.Wrap });
                ug.Children.Add(new Border { Background = Surface, CornerRadius = new CornerRadius(12), Padding = new Thickness(13, 10, 10, 11), Margin = new Thickness(0, 0, 8, 8), Child = s });
            }
            sp.Children.Add(ug);
        }

        /// <summary>Gruplanmış liste kartı. Satıra tıklanabiliyorsa sağda ok gösterilir.</summary>
        void Card(StackPanel sp, IEnumerable<(string icon, string text, string value, Action click)> items, bool numbered = false)
        {
            var arr = items.ToList();
            if (arr.Count == 0) return;
            var stack = new StackPanel();
            for (int i = 0; i < arr.Count; i++)
            {
                var (icon, text, value, click) = arr[i];
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                FrameworkElement lead;
                if (numbered) lead = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Blue, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0), Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                else if (icon != null) lead = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(7), Background = B("#3A3A3C"), Child = new Image { Source = Wiki.Icon(icon), Margin = new Thickness(2) } };
                else lead = new Ellipse { Width = 6, Height = 6, Fill = Blue, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 8, 4, 0) };
                g.Children.Add(lead);
                var tb = new TextBlock { Text = text, FontSize = 13.5, LineHeight = 19, Foreground = Label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(tb, 1); g.Children.Add(tb);
                var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (value != null) right.Children.Add(new TextBlock { Text = value, FontSize = 13, Foreground = Secondary });
                if (click != null) right.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 11, Foreground = B("#6C6C70"), Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(right, 2); g.Children.Add(right);
                var row = new Border { Padding = new Thickness(14, 10, 14, 10), Background = Brushes.Transparent, Child = g };
                if (click != null)
                {
                    row.Cursor = Cursors.Hand;
                    row.MouseEnter += (s, e) => row.Background = B("#14FFFFFF");
                    row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
                    row.MouseLeftButtonUp += (s, e) => click();
                }
                stack.Children.Add(row);
                if (i < arr.Count - 1) stack.Children.Add(new Rectangle { Height = 1, Fill = Sep, Margin = new Thickness(numbered || icon != null ? 58 : 32, 0, 0, 0) });
            }
            sp.Children.Add(new Border { Background = Surface, CornerRadius = new CornerRadius(12), Child = stack, ClipToBounds = true });
        }

        Action OpenItem(string id) => id != null && w.ItemById.TryGetValue(id, out var it) ? (Action)(() => Show(0, it)) : null;
        Action OpenNode(string id) => id != null && w.NodeById.TryGetValue(id, out var n) ? (Action)(() => Show(2, n)) : null;

        // ------------------------------------------------------------------ bölüm ayrıntıları

        void ItemDetail(StackPanel sp, WItem i)
        {
            Hero(sp, i.Icon, i.Name, i.En, i.Rarity, i.Color, i.Category);
            Para(sp, i.Desc, 18);
            var tiles = new List<(string, string)> { ("Fiyat", Price(i.Price)) };
            if (i.Fence.Count > 0) { tiles.Add(("Tefeci en az", Price(i.Fence.Min(f => f.price)))); tiles.Add(("Tefeci en çok", Price(i.Fence.Max(f => f.price)))); }
            Tiles(sp, tiles, 3);
            if (i.Fence.Count > 1)
            {
                Header(sp, "Tefeci fiyatları (kademeye göre)");
                Card(sp, i.Fence.Select(f => ((string)null, f.tier + ". kademe", Wiki.Money(f.price), (Action)null)));
            }
            if (i.Where.Count > 0) { Header(sp, "Nerede bulunur"); Card(sp, i.Where.Select(x => ((string)null, x, (string)null, (Action)null))); }
            var obtain = i.Obtain.Where(o => !i.Where.Contains(o)).ToList();
            if (obtain.Count > 0) { Header(sp, "Nasıl elde edilir"); Card(sp, obtain.Select(x => ((string)null, x, (string)null, (Action)null))); }
            // Bu eşya Larry'nin ağacında / takaslarda / binalarda geçiyor mu?
            if (w.NodeById.TryGetValue(i.Id, out var node))
            {
                Header(sp, "Larry'nin ağacında");
                Card(sp, new[] { (node.Icon, node.Branch.Name + " · " + node.Stage + ". aşama", Wiki.Money(node.Research), OpenNode(node.Id)) });
            }
            var barters = w.Barters.Where(b => b.ItemId == i.Id || b.Inputs.Any(x => x.ItemId == i.Id)).ToList();
            if (barters.Count > 0)
            {
                Header(sp, "Takaslar");
                Card(sp, barters.Select(b => (b.Icon, b.ItemId == i.Id ? b.Name + " (bu eşyayı verir)" : b.Name + " (girdi olarak)", (string)null, (Action)(() => Show(3, b)))));
            }
            var builds = w.Buildings.Where(b => b.ItemIds.Contains(i.Id)).ToList();
            if (builds.Count > 0)
            {
                Header(sp, $"Bulunduğu binalar ({builds.Count})");
                Card(sp, builds.Take(12).Select(b => ((string)null, b.Name, b.Type, (Action)(() => Show(4, b)))));
            }
        }

        void WeaponDetail(StackPanel sp, WWeapon x)
        {
            Hero(sp, x.Icon, x.Name, null, x.Rarity, x.Color, x.Class);
            Tiles(sp, new[]
            {
                ("Hasar", Num(x.Damage)), ("Kafaya", Num(x.Head)), ("Atış/dk", Num(x.Rpm)), ("DPS", Num(x.Dps)),
                ("Şarjör", Num(x.Mag)), ("Menzil", x.Range != null ? Num(x.Range) + " m" : null), ("Şarjör değiştirme", x.Reload != null ? Num(x.Reload) + " sn" : null), ("Hızlı değiştirme", x.FastReload != null ? Num(x.FastReload) + " sn" : null),
                ("Nişan alma", x.Aim != null ? Num(x.Aim) + " sn" : null), ("Çekme", x.Draw != null ? Num(x.Draw) + " sn" : null), ("Fiyat", Price(x.Price)),
            }, 3);
            if (x.FireMode != null) { Header(sp, "Atış modu"); Card(sp, new[] { ((string)null, x.FireMode, (string)null, (Action)null) }); }
            if (x.Where.Count > 0) { Header(sp, "Nereden alınır"); Card(sp, x.Where.Select(s => ((string)null, s, (string)null, (Action)null))); }
            var family = w.Weapons.Where(o => o.Family == x.Family && o != x).ToList();
            if (family.Count > 0)
            {
                Header(sp, x.Family + " ailesi");
                Card(sp, family.Select(o => (o.Icon, o.Name, o.Dps != null ? "DPS " + Num(o.Dps) : null, (Action)(() => Show(1, o)))));
            }
            if (w.NodeById.TryGetValue(x.Id, out var node))
            {
                Header(sp, "Larry'nin ağacında");
                Card(sp, new[] { (node.Icon, node.Branch.Name + " · " + node.Stage + ". aşama", Wiki.Money(node.Research), OpenNode(node.Id)) });
            }
        }

        void NodeDetail(StackPanel sp, WNode n)
        {
            var art = Wiki.Icon(n.Branch.Art);
            if (art != null)
            {
                var banner = new Border { Height = 92, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 18), Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center } };
                var g = new Grid(); g.Children.Add(banner);
                g.Children.Add(new Border { CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 18), Background = new LinearGradientBrush(C("#E6141416"), C("#33141416"), 0) });
                var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 18) };
                t.Children.Add(new TextBlock { Text = n.Branch.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Label });
                if (n.Branch.Sub != null) t.Children.Add(new TextBlock { Text = n.Branch.Sub, FontSize = 12, Foreground = B("#C7C7CC"), Margin = new Thickness(0, 2, 0, 0) });
                g.Children.Add(t);
                sp.Children.Add(g);
            }
            Hero(sp, n.Icon, n.Name, n.En, n.Rarity, n.Color, n.Stage + ". aşama");
            Para(sp, n.Desc, 18);
            Tiles(sp, new[] { ("Araştırma", n.Research > 0 ? Wiki.Money(n.Research) : "Ücretsiz"), ("Satın alma", Wiki.Money(n.Price)), ("Süre", n.Years > 0 ? n.Years + " yıl" : null), ("Aşama", n.Stage.ToString()) });
            var req = n.Requires.Select(id => w.NodeById.TryGetValue(id, out var r) ? r : null).Where(r => r != null).ToList();
            Header(sp, "Önce açılması gerekenler");
            if (req.Count == 0) Card(sp, new[] { ((string)null, "Yok, dalın başlangıç düğümü", (string)null, (Action)null) });
            else Card(sp, req.Select(r => (r.Icon, r.Name, r.Stage + ". aşama", OpenNode(r.Id))));
            var kids = n.Unlocks.Select(id => w.NodeById.TryGetValue(id, out var r) ? r : null).Where(r => r != null).ToList();
            if (kids.Count > 0) { Header(sp, "Bunu açınca"); Card(sp, kids.Select(r => (r.Icon, r.Name, r.Stage + ". aşama", OpenNode(r.Id)))); }
            if (n.ItemId != null) { Header(sp, "Eşya"); Card(sp, new[] { (n.Icon, n.Name + " eşya sayfası", (string)null, OpenItem(n.ItemId)) }); }
        }

        void BarterDetail(StackPanel sp, WBarter b)
        {
            w.ItemById.TryGetValue(b.ItemId ?? "", out var it);
            Hero(sp, b.Icon, b.Name, it?.En, it?.Rarity, it?.Color, "Takas");
            Para(sp, b.Usage, 18);
            Header(sp, "Gerekenler");
            Card(sp, b.Inputs.Select(x => (x.Icon, x.Name, x.ItemId == "cash_1" ? Wiki.Money(x.Qty) : "× " + x.Qty.ToString("N0", Tr), OpenItem(x.ItemId))));
            if (b.Npc != null) { Header(sp, "Nerede yapılır"); Card(sp, new[] { ((string)null, b.Npc, (string)null, (Action)null) }); }
            if (it != null) { Header(sp, "Sonuç"); Card(sp, new[] { (it.Icon, it.Name, Wiki.Money(it.Price), OpenItem(it.Id)) }); }
        }

        void BuildingDetail(StackPanel sp, WBuilding b)
        {
            var first = b.ItemIds.Select(id => w.ItemById.TryGetValue(id, out var it) ? it : null).FirstOrDefault(x => x != null);
            Hero(sp, first?.Icon, b.Name, b.En, null, null, b.Type);
            Para(sp, b.Desc, 18);
            var items = b.ItemIds.Select(id => w.ItemById.TryGetValue(id, out var it) ? it : null).Where(x => x != null).ToList();
            if (items.Count == 0) return;
            Header(sp, $"Bulunabilecek eşyalar ({items.Count})");
            var wrap = new WrapPanel();
            foreach (var it in items.OrderByDescending(x => x.Price ?? 0))
            {
                var tile = new Border
                {
                    Width = 64, Height = 64, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 8, 8), Background = Surface, Cursor = Cursors.Hand,
                    BorderBrush = Rarity(it.Color), BorderThickness = new Thickness(1),
                    ToolTip = it.Name + (it.Price != null ? " · " + Wiki.Money(it.Price) : ""),
                    Child = new Image { Source = Wiki.Icon(it.Icon), Margin = new Thickness(8) },
                };
                var target = it;
                tile.MouseEnter += (s, e) => tile.Background = B("#3A3A3C");
                tile.MouseLeave += (s, e) => tile.Background = Surface;
                tile.MouseLeftButtonUp += (s, e) => Show(0, target);
                wrap.Children.Add(tile);
            }
            sp.Children.Add(wrap);
        }

        void RobberyDetail(StackPanel sp, WRobbery r)
        {
            Hero(sp, r.Icon, r.Name, null, r.Status, r.StatusType == "available" ? "#30D158" : r.StatusType == "closed" ? "#FF453A" : "#FF9F0A", r.Group);
            Para(sp, r.Summary, 18);
            if (r.Place != null) { Header(sp, "Yer"); Card(sp, new[] { ((string)null, r.Place, (string)null, (Action)null) }); }
            if (r.Tools.Count > 0) { Header(sp, "Gerekli aletler"); Card(sp, r.Tools.Select(t => ((string)null, t, (string)null, (Action)null))); }
            if (r.Steps.Count > 0) { Header(sp, "Adımlar"); Card(sp, r.Steps.Select(t => ((string)null, t, (string)null, (Action)null)), numbered: true); }
        }
    }
}
