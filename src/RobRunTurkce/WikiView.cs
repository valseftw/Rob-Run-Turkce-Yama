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

    /// <summary>Başlangıç rehberi satırı: adım, para yöntemi ya da sık yapılan hata.</summary>
    sealed class GuideItem { public string Kind; public object Value; }

    /// <summary>
    /// Rehber: ROB RUN Türkçe Wiki verisinin uygulama içi görünümü. Solda arama + filtre + liste, sağda ayrıntı.
    /// Looty Bay haritası bölümünde liste yerine etkileşimli harita gösterilir. Kenar çubuğundaki bölüm listesi (Nav) de burada üretilir.
    /// </summary>
    public sealed class WikiView : Grid
    {
        readonly Wiki w;
        public readonly ScrollViewer Nav = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly StackPanel navStack = new StackPanel();
        readonly TextBlock title = new TextBlock(), subtitle = new TextBlock();
        readonly TextBox search = new TextBox();
        readonly TextBlock searchHint = new TextBlock();
        readonly StackPanel chips = new StackPanel { Orientation = Orientation.Horizontal };
        readonly ScrollViewer chipScroll;
        readonly ListBox list = new ListBox();
        readonly Border listCard;
        readonly ScrollViewer detail = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly Border detailHost = new Border(); // harita bölümünde haritanın üstünde yüzen kart olur
        readonly TextBlock empty = new TextBlock();
        WikiMap mapView;
        int section = -1;
        string chip; // seçili filtre (null = tümü)
        List<WRow> rows = new List<WRow>();
        readonly Dictionary<int, Border> navItems = new Dictionary<int, Border>();
        readonly HashSet<string> plan = new HashSet<string>(StringComparer.Ordinal); // sevkiyat planlayıcısı

        static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
        static Color C(string hex) { try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return Colors.Gray; } }
        static Brush B(string hex) { var b = new SolidColorBrush(C(hex)); b.Freeze(); return b; }
        static Brush Rarity(string hex) => string.IsNullOrEmpty(hex) ? Brushes.Transparent : B(hex);
        static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");
        static readonly Brush Label = B("#F5F5F7"), Secondary = B("#98989F"), Surface = B("#2C2C2E"), Sep = B("#38383A"), Blue = B("#0A84FF");

        const int SecItems = 0, SecWeapons = 1, SecTree = 2, SecBarters = 3, SecBuildings = 4, SecRobberies = 5, SecMap = 6, SecGuide = 7, SecDevices = 8;
        static readonly (string name, string glyph, string tint)[] Sections =
        {
            ("Eşyalar", "", "#0A84FF"), ("Silahlar", "", "#FF453A"), ("Larry'nin Sevkiyatları", "", "#30D158"),
            ("Takaslar", "", "#FF9F0A"), ("Binalar", "", "#5E5CE6"), ("Soygunlar", "", "#BF5AF2"),
            ("Looty Bay Haritası", "", "#64D2FF"), ("Başlangıç Rehberi", "", "#FF375F"), ("Birikimli Cihazlar", "", "#FFB340"),
        };
        /// <summary>Kenar çubuğundaki sıra.</summary>
        static readonly int[] NavOrder = { SecGuide, SecItems, SecMap, SecTree, SecWeapons, SecDevices, SecBarters, SecBuildings, SecRobberies };

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
            listCard = new Border { Background = B("#161618"), CornerRadius = new CornerRadius(14), Padding = new Thickness(6, 6, 2, 6), Child = list };
            SetRow(listCard, 2); Children.Add(listCard);
            empty.Text = "Sonuç yok"; empty.Foreground = Secondary; empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center; empty.Visibility = Visibility.Collapsed;
            SetRow(empty, 2); Children.Add(empty);

            // --- ayrıntı
            detailHost.Child = detail;
            SetRow(detailHost, 2); SetColumn(detailHost, 2); Children.Add(detailHost);

            Nav.Content = navStack;
            BuildNav();
            Show(w.Guides != null ? SecGuide : SecItems);
        }

        bool Available(int sec) => sec == SecMap ? w.Map != null : sec == SecGuide || sec == SecDevices ? w.Guides != null : true;

        // ============================================================ kenar çubuğu bölüm listesi

        void BuildNav()
        {
            int Count(int sec)
            {
                switch (sec)
                {
                    case SecItems: return w.Items.Count;
                    case SecWeapons: return w.Weapons.Count;
                    case SecTree: return w.Branches.Sum(b => b.Nodes.Count);
                    case SecBarters: return w.Barters.Count;
                    case SecBuildings: return w.Buildings.Count;
                    case SecRobberies: return w.Robberies.Count;
                    case SecMap: return w.Map?.Pois.Count ?? 0;
                    case SecGuide: return w.Guides == null ? 0 : w.Guides.Steps.Count + w.Guides.Money.Count + w.Guides.Mistakes.Count;
                    default: return w.Guides?.Devices.Count ?? 0;
                }
            }
            foreach (int idx in NavOrder)
            {
                if (!Available(idx)) continue;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var tint = C(Sections[idx].tint);
                var light = Color.FromArgb(255, (byte)Math.Min(255, tint.R + 28), (byte)Math.Min(255, tint.G + 28), (byte)Math.Min(255, tint.B + 28));
                g.Children.Add(new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Background = new LinearGradientBrush(light, tint, 90),
                    Child = new TextBlock { Text = Sections[idx].glyph, FontFamily = Icons, FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                });
                var name = new TextBlock { Text = Sections[idx].name, FontSize = 13.5, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Label, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(name, 1); g.Children.Add(name);
                var cnt = new TextBlock { Text = Count(idx).ToString("N0", Tr), FontSize = 11.5, Foreground = Secondary, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(cnt, 2); g.Children.Add(cnt);
                var item = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 4, 10, 4), Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent, Child = g, Cursor = Cursors.Hand };
                int sec = idx;
                item.MouseEnter += (s, e) => { if (section != sec) item.Background = B("#1FFFFFFF"); };
                item.MouseLeave += (s, e) => { if (section != sec) item.Background = Brushes.Transparent; };
                item.MouseLeftButtonUp += (s, e) => Show(sec);
                navItems[idx] = item;
                navStack.Children.Add(item);
            }
            navStack.Children.Add(new TextBlock
            {
                Text = $"ROB RUN Türkçe Wiki · veri {w.Version}" + (w.GameVersion != null ? " · " + w.GameVersion : ""),
                FontSize = 10.5, Foreground = Secondary, Margin = new Thickness(8, 10, 0, 0), TextWrapping = TextWrapping.Wrap,
            });
        }

        void UpdateNav()
        {
            foreach (var kv in navItems) kv.Value.Background = kv.Key == section ? B("#3A0A84FF") : Brushes.Transparent;
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
            search.TextChanged += (s, e) =>
            {
                searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (section == SecMap) mapView?.SetQuery(search.Text); else ApplyFilter();
            };
            search.KeyDown += (s, e) => { if (e.Key == Key.Escape) search.Text = ""; else if (e.Key == Key.Down && list.Items.Count > 0) { list.SelectedIndex = Math.Max(0, list.SelectedIndex); (list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex) as UIElement)?.Focus(); } };
            g.Children.Add(search);
            return g;
        }

        // ============================================================ bölümler

        public void Show(int sec, object select = null)
        {
            if (!Available(sec)) return;
            bool changed = sec != section;
            section = sec;
            UpdateNav();
            if (changed) { chip = null; search.Text = ""; }
            title.Text = Sections[sec].name;
            searchHint.Text = sec == SecMap ? "Haritada ara (bina, eşya…)" : "Ara";
            SetMapMode(sec == SecMap);
            if (sec == SecMap)
            {
                BuildMapChips();
                subtitle.Text = $"{w.Map.Pois.Count} bina ve nokta · {w.Map.Objects.Count.ToString("N0", Tr)} arama noktası · sürükle ve tekerlekle yakınlaştır";
                if (select != null) mapView.Reveal(select); else ShowMapSummary();
            }
            else
            {
                // Larry'nin sevkiyatları: dallar karışmasın diye her zaman bir dal seçili (seçilen düğümün dalı)
                if (sec == SecTree && w.Branches.Count > 0)
                    chip = (select as WNode)?.Branch.Name ?? (changed || chip == null ? w.Branches[0].Name : chip);
                rows = BuildRows(sec);
                BuildChips(sec);
                ApplyFilter(select);
            }
            if (changed)
            {
                var tt = new TranslateTransform(0, 10); RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(220)));
            }
        }

        /// <summary>Harita bölümünde liste gizlenir; harita geniş, ayrıntı paneli dar olur.</summary>
        void SetMapMode(bool on)
        {
            if (on && mapView == null)
            {
                mapView = new WikiMap(w.Map, w) { RightInset = MapCardWidth + 20 };
                mapView.Selected += o => ShowDetail(o);
                SetRow(mapView, 2); SetColumnSpan(mapView, 3);
                Children.Insert(Children.IndexOf(detailHost), mapView); // kart haritanın üstünde kalsın
            }
            if (mapView != null) mapView.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            listCard.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (on) empty.Visibility = Visibility.Collapsed;
            // Harita tüm genişliği kaplar; ayrıntı sağda yüzen bir kart olur (Apple Haritalar düzeni)
            SetColumn(detailHost, on ? 0 : 2); SetColumnSpan(detailHost, on ? 3 : 1);
            detailHost.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            detailHost.Width = on ? MapCardWidth : double.NaN;
            detailHost.Margin = on ? new Thickness(0, 10, 10, 10) : new Thickness(0);
            detailHost.Padding = on ? new Thickness(16, 14, 6, 6) : new Thickness(0);
            detailHost.CornerRadius = new CornerRadius(on ? 14 : 0);
            detailHost.Background = on ? B("#EB1C1C1E") : null;
            detailHost.BorderBrush = on ? B("#3A3A3C") : null;
            detailHost.BorderThickness = new Thickness(on ? 1 : 0);
            detailHost.Effect = on ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.45 } : null;
        }

        const double MapCardWidth = 320;

        string Lower(params string[] parts) => string.Join(" ", parts.Where(p => p != null)).ToLower(Tr);

        List<WRow> BuildRows(int sec)
        {
            switch (sec)
            {
                case SecItems:
                    return w.Items.OrderBy(i => i.Name, StringComparer.Create(Tr, true)).Select(i => new WRow
                    {
                        Title = i.Name, Sub = (i.Category ?? "") + (i.Rarity != null ? " · " + i.Rarity : ""), Value = Price(i.Price), IconName = i.Icon,
                        Accent = Rarity(i.Color), Tag = i, Search = Lower(i.Name, i.En, i.Category, i.Rarity, i.Id),
                    }).ToList();
                case SecWeapons:
                    return w.Weapons.OrderBy(x => x.Class).ThenBy(x => x.Name).Select(x => new WRow
                    {
                        Title = x.Name, Sub = x.Class + (x.Rarity != null ? " · " + x.Rarity : ""), Value = x.Dps != null ? "DPS " + Num(x.Dps) : null, IconName = x.Icon,
                        Accent = Rarity(x.Color), Tag = x, Search = Lower(x.Name, x.Family, x.Class, x.Rarity),
                    }).ToList();
                case SecTree:
                    return w.Branches.SelectMany(b => b.Nodes.OrderBy(n => n.Stage)).Select(n => new WRow
                    {
                        Title = (plan.Contains(n.Id) ? "✓ " : "") + n.Name, Sub = $"{n.Stage}. aşama" + (n.Research > 0 ? " · araştırma " + Wiki.Money(n.Research) : ""), Value = Wiki.Money(n.Price), IconName = n.Icon,
                        Accent = plan.Contains(n.Id) ? Blue : Rarity(n.Color), Tag = n, Search = Lower(n.Name, n.En, n.Branch.Name),
                    }).ToList();
                case SecBarters:
                    return w.Barters.Select(b => new WRow
                    {
                        Title = b.Name, Sub = b.Npc, Value = b.Inputs.Count + " girdi", IconName = b.Icon, Tag = b,
                        Accent = Rarity(b.ItemId != null && w.ItemById.TryGetValue(b.ItemId, out var it) ? it.Color : null),
                        Search = Lower(b.Name, b.Npc, string.Join(" ", b.Inputs.Select(x => x.Name))),
                    }).ToList();
                case SecBuildings:
                    return w.Buildings.OrderBy(b => b.Name, StringComparer.Create(Tr, true)).Select(b => new WRow
                    {
                        Title = b.Name, Sub = b.Type, Value = b.ItemIds.Count + " eşya", IconName = b.ItemIds.Select(id => w.ItemById.TryGetValue(id, out var it) ? it.Icon : null).FirstOrDefault(x => x != null),
                        Tag = b, Search = Lower(b.Name, b.En, b.Type),
                    }).ToList();
                case SecRobberies:
                    return w.Robberies.Select(r => new WRow
                    {
                        Title = r.Name, Sub = r.Group + (r.Status != null ? " · " + r.Status : ""), IconName = r.Icon, Tag = r, Accent = StatusBrush(r.StatusType),
                        Search = Lower(r.Name, r.Group, r.Place),
                    }).ToList();
                case SecGuide:
                    var g = w.Guides;
                    return g.Steps.Select(s => new WRow { Title = s.Title, Sub = s.No + " · " + s.Badge, Tag = new GuideItem { Kind = "Adım adım", Value = s }, Accent = B("#FF375F"), Search = Lower(s.Title, s.Badge, s.Text) })
                        .Concat(g.Money.Select(m => new WRow { Title = m.Title, Sub = m.Rank, Value = m.Payout, IconName = m.Icon, Tag = new GuideItem { Kind = "Para kazanma", Value = m }, Accent = B("#30D158"), Search = Lower(m.Title, m.Rank, m.How) }))
                        .Concat(g.Mistakes.Select(m => new WRow { Title = m.title, Sub = "Sık yapılan hata " + m.no, Tag = new GuideItem { Kind = "Sık hatalar", Value = m }, Accent = B("#FF9F0A"), Search = Lower(m.title, m.text) }))
                        .ToList();
                default:
                    return w.Guides.Devices.Select(d => new WRow
                    {
                        Title = d.Name, Sub = d.Badge, Value = d.Points + " puan", IconName = d.Icon, Tag = d, Accent = Rarity(d.Color), Search = Lower(d.Name, d.Badge, d.HowToFill),
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
                case GuideItem gi: return gi.Kind;
                default: return null;
            }
        }

        void BuildChips(int sec)
        {
            chips.Children.Clear();
            var names = rows.Select(ChipOf).Where(x => x != null).GroupBy(x => x).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
            if (sec == SecTree) names = w.Branches.Select(b => b.Name).ToList(); // oyundaki dal sırası
            if (sec == SecGuide) names = new List<string> { "Adım adım", "Para kazanma", "Sık hatalar" };
            if (names.Count < 2) { chipScroll.Visibility = Visibility.Collapsed; return; }
            chipScroll.Visibility = Visibility.Visible;
            string group = "wikichip" + sec;
            void Add(string label, string value)
            {
                var rb = new RadioButton { Content = label, GroupName = group, Style = (Style)Application.Current.FindResource("Chip"), IsChecked = value == chip };
                rb.Checked += (s, e) => { chip = value; ApplyFilter(); };
                chips.Children.Add(rb);
            }
            if (sec != SecTree) Add("Tümü", null);
            foreach (var n in names) Add(n, n);
        }

        /// <summary>Harita katman düğmeleri: bina/nokta grupları ve arama noktası katmanları (aç/kapa).</summary>
        void BuildMapChips()
        {
            chips.Children.Clear();
            chipScroll.Visibility = Visibility.Visible;
            CheckBox Toggle(string label, string color, bool on, Action<bool> set)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = B(color), Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
                var cb = new CheckBox { Content = sp, IsChecked = on, Style = (Style)Application.Current.FindResource("ChipToggle") };
                cb.Checked += (s, e) => set(true);
                cb.Unchecked += (s, e) => set(false);
                return cb;
            }
            foreach (var g in w.Map.Groups)
            {
                var id = g.Id;
                chips.Children.Add(Toggle(g.Name, WikiMap.GroupColor.TryGetValue(id, out var c) ? c : "#8E8E93", mapView.GroupVisible(id), on => mapView.SetGroup(id, on)));
            }
            chips.Children.Add(new Rectangle { Width = 1, Height = 20, Fill = Sep, Margin = new Thickness(4, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            foreach (var l in w.Map.Layers)
            {
                var id = l.Id;
                chips.Children.Add(Toggle(l.Name, l.Color, mapView.LayerVisible(id), on => mapView.SetLayer(id, on)));
            }
        }

        void ApplyFilter(object select = null)
        {
            var q = search.Text.Trim().ToLower(Tr);
            var shown = rows.Where(r => (chip == null || ChipOf(r) == chip) && (q.Length == 0 || r.Search.Contains(q))).ToList();
            list.ItemsSource = shown;
            subtitle.Text = shown.Count == rows.Count ? $"{rows.Count.ToString("N0", Tr)} kayıt" : $"{shown.Count.ToString("N0", Tr)} / {rows.Count.ToString("N0", Tr)} kayıt";
            if (section == SecTree && plan.Count > 0) subtitle.Text += $" · planda {plan.Count} düğüm";
            empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            WRow target = null;
            if (select != null)
            {
                target = shown.FirstOrDefault(r => SameTag(r.Tag, select));
                if (target == null)
                {
                    // seçilecek kayıt filtre dışında kaldıysa filtreyi kaldır
                    chip = section == SecTree ? (select as WNode)?.Branch.Name : null; search.Text = "";
                    shown = rows.Where(r => chip == null || ChipOf(r) == chip).ToList(); list.ItemsSource = shown;
                    foreach (var rb in chips.Children.OfType<RadioButton>()) rb.IsChecked = (rb.Content as string) == (chip ?? "Tümü");
                    target = shown.FirstOrDefault(r => SameTag(r.Tag, select));
                }
            }
            list.SelectedItem = target ?? shown.FirstOrDefault();
            if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
            else detail.Content = null;
        }

        static bool SameTag(object a, object b) => ReferenceEquals(a, b) || (a is GuideItem g && ReferenceEquals(g.Value, b));

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
                case WPoi p: PoiDetail(sp, p); break;
                case WObj o: ObjDetail(sp, o); break;
                case GuideItem gi: GuideDetail(sp, gi.Value); break;
                case WDevice d: DeviceDetail(sp, d); break;
            }
            detail.Content = sp;
            detail.ScrollToTop();
            sp.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }

        static string Num(double? v) => v == null ? "—" : v.Value.ToString("0.##", Tr);
        /// <summary>0 fiyat = bilinmiyor/satılmaz: gösterilmez.</summary>
        static string Price(long? v) => v > 0 ? Wiki.Money(v) : null;

        void Hero(StackPanel sp, string icon, string name, string en, string rarity, string color, string kind, string glyph = null, string glyphColor = null)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var img = Wiki.Icon(icon);
            FrameworkElement inner = img != null
                ? (FrameworkElement)new Image { Source = img, Margin = new Thickness(10), Stretch = Stretch.Uniform }
                : new TextBlock { Text = glyph ?? (string.IsNullOrEmpty(name) ? "" : name.Substring(0, 1)), FontFamily = glyph != null ? Icons : Display, FontSize = glyph != null ? 34 : 36, FontWeight = FontWeights.Bold, Foreground = glyph != null ? Brushes.White : Secondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(inner, BitmapScalingMode.HighQuality);
            var box = new Border
            {
                Width = 84, Height = 84, CornerRadius = new CornerRadius(19), Background = glyphColor != null ? B(glyphColor) : B("#2C2C2E"),
                BorderBrush = Rarity(color), BorderThickness = new Thickness(color != null ? 1.5 : 0), Child = inner,
            };
            if (color != null) box.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = C(color), BlurRadius = 26, ShadowDepth = 0, Opacity = 0.35 };
            g.Children.Add(box);
            var t = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(new TextBlock { Text = name, FontFamily = Display, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Label, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(en) && !string.Equals(en, name, StringComparison.OrdinalIgnoreCase))
                t.Children.Add(new TextBlock { Text = en, FontSize = 13, Foreground = Secondary, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            var pills = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            if (rarity != null) pills.Children.Add(Pill(rarity, color));
            if (kind != null) pills.Children.Add(Pill(kind, null));
            if (pills.Children.Count > 0) t.Children.Add(pills);
            Grid.SetColumn(t, 1); g.Children.Add(t);
            sp.Children.Add(g);
        }

        static Border Pill(string text, string color)
        {
            var c = color != null ? C(color) : C("#98989F");
            return new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 6, 4), Background = new SolidColorBrush(Color.FromArgb(46, c.R, c.G, c.B)),
                Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(color != null ? c : C("#D1D1D6")), TextWrapping = TextWrapping.Wrap }
            };
        }

        static void Para(StackPanel sp, string text, double top = 16)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            sp.Children.Add(new TextBlock { Text = text, FontSize = 14, LineHeight = 21, Foreground = B("#D1D1D6"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) });
        }

        static void Header(StackPanel sp, string text) =>
            sp.Children.Add(new TextBlock { Text = text.ToUpper(Tr), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Secondary, Margin = new Thickness(4, 22, 0, 7) });

        /// <summary>İstatistik kutucukları (iOS widget görünümü). Uzun değerler kutuya sığsın diye küçülür.</summary>
        static void Tiles(StackPanel sp, IEnumerable<(string label, string value)> items, int columns = 4)
        {
            var list = items.Where(x => x.value != null).ToList();
            if (list.Count == 0) return;
            var ug = new UniformGrid { Columns = Math.Min(columns, list.Count), Margin = new Thickness(0, 18, 0, 0) };
            foreach (var (label, value) in list)
            {
                var s = new StackPanel();
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

        /// <summary>Ayrıntı panelindeki eylem düğmeleri.</summary>
        static void Buttons(StackPanel sp, params (string text, bool primary, Action click)[] buttons)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            foreach (var (text, primary, click) in buttons.Where(b => b.text != null))
            {
                var b = new Button { Content = text, Height = 32, Margin = new Thickness(0, 0, 8, 8), Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "SecondaryButton") };
                b.Click += (s, e) => click();
                wrap.Children.Add(b);
            }
            sp.Children.Add(wrap);
        }

        Action OpenItem(string id) => id != null && w.ItemById.TryGetValue(id, out var it) ? (Action)(() => Show(SecItems, it)) : null;
        Action OpenNode(string id) => id != null && w.NodeById.TryGetValue(id, out var n) ? (Action)(() => Show(SecTree, n)) : null;
        string ItemName(string id) => id != null && w.ItemById.TryGetValue(id, out var it) ? it.Name : id;
        string ItemIcon(string id) => id != null && w.ItemById.TryGetValue(id, out var it) ? it.Icon : null;

        IEnumerable<(string, string, string, Action)> RefRows(IEnumerable<WRef> refs, Func<WRef, string> value = null) =>
            refs.Select(r => (r.Icon ?? ItemIcon(r.ItemId), r.Name, value?.Invoke(r), OpenItem(r.ItemId)));

        // ------------------------------------------------------------------ harita bağlantıları

        int MapCount(string itemId) => w.Map == null ? 0 : w.Map.Objects.Count(o => o.Loot.Any(l => l.id == itemId)) + w.Map.Pois.Count(p => p.Loot.Any(l => l.Id == itemId));

        /// <summary>Geliştirme/ekran görüntüsü: eşyanın çıktığı noktaları haritada gösterir.</summary>
        internal void ShowItemOnMap(string itemId) => ShowOnMap(m => m.SetItemFilter(itemId));
        /// <summary>Geliştirme/ekran görüntüsü: başlangıç paketini plana ekleyip ağacı açar.</summary>
        internal void PlanStarterPack()
        {
            if (w.Guides == null) return;
            foreach (var id in w.Guides.StarterPack) AddWithPrereqs(id);
            Show(SecTree, w.Guides.StarterPack.Select(id => w.NodeById.TryGetValue(id, out var n) ? n : null).FirstOrDefault(n => n != null));
        }

        void ShowOnMap(Action<WikiMap> apply)
        {
            Show(SecMap);
            apply(mapView);
            mapView.FitToVisible();
            ShowMapSummary();
        }

        void ShowMapSummary()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };
            Hero(sp, null, "Looty Bay", "Etkileşimli şehir haritası", null, null, "4 bölge", "", "#0A84FF");
            string filter = mapView.ItemFilter != null ? "Eşya: " + ItemName(mapView.ItemFilter)
                : mapView.KindFilter != null ? "Tür: " + (w.Map.Pois.FirstOrDefault(p => p.Kind == mapView.KindFilter)?.KindName ?? mapView.KindFilter)
                : mapView.BuildingFilter != null ? "Bina: " + mapView.BuildingFilter : null;
            if (filter != null)
            {
                Header(sp, "Etkin filtre");
                Card(sp, new[] { ((string)null, filter, "Kaldır", (Action)(() => { mapView.ClearFilters(); ShowMapSummary(); })) });
            }
            Para(sp, "Fareyle sürükleyerek kaydır, tekerlekle yakınlaştır. Bir binaya ya da noktaya tıklayınca ayrıntısı burada açılır. Yukarıdaki düğmelerle katmanları aç/kapa; 927 arama noktası (kasa, dolap, vitrin, şarj noktası…) varsayılan olarak kapalıdır.");
            Header(sp, "Bölgeler");
            Card(sp, w.Map.Districts.Select(d => ((string)null, d.name, (string)null, (Action)(() => mapView.CenterOn(d.x, d.y, 0.6)))));
            Header(sp, "Önemli noktalar");
            Card(sp, w.Map.Groups.Select(g => ((string)null, g.Name, w.Map.Pois.Count(p => p.Group == g.Id).ToString(), (Action)null)));
            detail.Content = sp;
            detail.ScrollToTop();
        }

        void PoiDetail(StackPanel sp, WPoi p)
        {
            Hero(sp, null, p.Name, p.En, null, null, p.KindName, WikiMap.Glyph(p), WikiMap.GroupColor.TryGetValue(p.Group ?? "", out var gc) ? gc : "#8E8E93");
            if (p.Special != null) sp.Children.Add(new WrapPanel { Margin = new Thickness(0, 12, 0, 0), Children = { Pill(p.Special, "#FF9F0A") } });
            Para(sp, p.Desc, 14);
            var inside = w.Map.Objects.Where(o => o.Building == p.Name).ToList();
            if (inside.Count > 0)
            {
                var layerName = w.Map.Layers.ToDictionary(l => l.Id, l => l.Name);
                Tiles(sp, inside.GroupBy(o => o.Layer).OrderByDescending(g => g.Count()).Take(4).Select(g => (layerName.TryGetValue(g.Key ?? "", out var n) ? n : g.Key, g.Count().ToString())), 2);
                Buttons(sp, ($"Binadaki {inside.Count} noktayı göster", true, () => { mapView.SetBuildingFilter(p.Name); mapView.CenterOn(p.X, p.Y, 0.9); ShowMapSummary(); }));
            }
            if (p.Loot.Count > 0) { Header(sp, "Çıkan değerli eşyalar"); Card(sp, RefRows(p.Loot)); }
            if (p.Refills.Count > 0) { Header(sp, "Cihaz şarj noktaları"); Card(sp, RefRows(p.Refills, r => r.Count + " puan")); }
            foreach (var k in p.Keys)
            {
                Header(sp, k.Name ?? "Anahtar");
                if (k.Opens != null) Para(sp, k.Opens, 0);
                Tiles(sp, new[] { ("Çelik kasa", k.Safes > 0 ? k.Safes.ToString() : null), ("Dolap", k.Cabinets > 0 ? k.Cabinets.ToString() : null), ("Kutu", k.Boxes > 0 ? k.Boxes.ToString() : null), ("Bilgisayar", k.Computers > 0 ? k.Computers.ToString() : null) });
                var need = new[] { k.Drill, k.Usb }.Where(x => x != null).ToList();
                if (need.Count > 0) Card(sp, RefRows(need, r => "× " + r.Count));
                if (k.Inside.Count > 0) { Header(sp, "Kilitli odada"); Card(sp, RefRows(k.Inside)); }
            }
            foreach (var j in p.Jobs)
            {
                Header(sp, j.Name ?? "Özel görev");
                if (j.Need.Count > 0) Card(sp, RefRows(j.Need, r => "gerekli"));
                if (j.Result != null) { sp.Children.Add(new Border { Height = 8 }); Card(sp, RefRows(new[] { j.Result }, r => "ödül")); }
                if (j.Steps.Count > 0) { sp.Children.Add(new Border { Height = 8 }); Card(sp, j.Steps.Select(s => ((string)null, s, (string)null, (Action)null)), numbered: true); }
                Para(sp, j.Note, 10);
            }
        }

        void ObjDetail(StackPanel sp, WObj o)
        {
            var layer = w.Map.Layers.FirstOrDefault(l => l.Id == o.Layer);
            Hero(sp, o.Loot.Select(l => ItemIcon(l.id)).FirstOrDefault(x => x != null), o.Name, null, layer?.Name, layer?.Color, null, "", layer?.Color);
            var poi = w.Map.Pois.FirstOrDefault(p => p.Name == o.Building);
            Header(sp, "Konum");
            Card(sp, new[]
            {
                ((string)null, string.IsNullOrEmpty(o.Building) ? "Açık alan" : o.Building, (string)null, poi != null ? (Action)(() => mapView.Reveal(poi)) : null),
                ((string)null, o.Region ?? "—", (string)null, (Action)null),
            });
            if (o.Loot.Count > 0)
            {
                Header(sp, "Çıkan eşyalar");
                Card(sp, o.Loot.Select(l => (ItemIcon(l.id), ItemName(l.id), l.max > 0 ? (l.min == l.max ? $"× {l.max}" : $"× {l.min}–{l.max}") : null, OpenItem(l.id))));
            }
        }

        // ------------------------------------------------------------------ eşya, silah, ağaç, takas, bina, soygun

        void ItemDetail(StackPanel sp, WItem i)
        {
            Hero(sp, i.Icon, i.Name, i.En, i.Rarity, i.Color, i.Category);
            Para(sp, i.Desc, 18);
            var tiles = new List<(string, string)> { ("Fiyat", Price(i.Price)) };
            if (i.Fence.Count > 0) { tiles.Add(("Tefeci en az", Price(i.Fence.Min(f => f.price)))); tiles.Add(("Tefeci en çok", Price(i.Fence.Max(f => f.price)))); }
            Tiles(sp, tiles, 3);
            int onMap = MapCount(i.Id);
            if (onMap > 0) Buttons(sp, ($"Haritada göster ({onMap} nokta)", true, () => ShowOnMap(m => m.SetItemFilter(i.Id))));
            if (i.Fence.Count > 1)
            {
                Header(sp, "Tefeci fiyatları (kademeye göre)");
                Card(sp, i.Fence.Select(f => ((string)null, f.tier + ". kademe", Wiki.Money(f.price), (Action)null)));
            }
            if (i.Where.Count > 0) { Header(sp, "Nerede bulunur"); Card(sp, i.Where.Select(x => ((string)null, x, (string)null, (Action)null))); }
            var obtain = i.Obtain.Where(o => !i.Where.Contains(o)).ToList();
            if (obtain.Count > 0) { Header(sp, "Nasıl elde edilir"); Card(sp, obtain.Select(x => ((string)null, x, (string)null, (Action)null))); }
            if (w.NodeById.TryGetValue(i.Id, out var node))
            {
                Header(sp, "Larry'nin sevkiyatlarında");
                Card(sp, new[] { (node.Icon, node.Branch.Name + " · " + node.Stage + ". aşama", Wiki.Money(node.Research), OpenNode(node.Id)) });
            }
            var device = w.Guides?.Devices.FirstOrDefault(d => d.Id == i.Id);
            if (device != null) { Header(sp, "Birikimli cihaz"); Card(sp, new[] { (device.Icon, device.Name, device.Points + " puan", (Action)(() => Show(SecDevices, device))) }); }
            var barters = w.Barters.Where(b => b.ItemId == i.Id || b.Inputs.Any(x => x.ItemId == i.Id)).ToList();
            if (barters.Count > 0)
            {
                Header(sp, "Takaslar");
                Card(sp, barters.Select(b => (b.Icon, b.ItemId == i.Id ? b.Name + " (bu eşyayı verir)" : b.Name + " (girdi olarak)", (string)null, (Action)(() => Show(SecBarters, b)))));
            }
            var builds = w.Buildings.Where(b => b.ItemIds.Contains(i.Id)).ToList();
            if (builds.Count > 0)
            {
                Header(sp, $"Bulunduğu binalar ({builds.Count})");
                Card(sp, builds.Take(12).Select(b => ((string)null, b.Name, b.Type, (Action)(() => Show(SecBuildings, b)))));
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
                Card(sp, family.Select(o => (o.Icon, o.Name, o.Dps != null ? "DPS " + Num(o.Dps) : null, (Action)(() => Show(SecWeapons, o)))));
            }
            if (w.NodeById.TryGetValue(x.Id, out var node))
            {
                Header(sp, "Larry'nin sevkiyatlarında");
                Card(sp, new[] { (node.Icon, node.Branch.Name + " · " + node.Stage + ". aşama", Wiki.Money(node.Research), OpenNode(node.Id)) });
            }
        }

        // ------------------------------------------------------------------ sevkiyat planlayıcısı

        /// <summary>Düğümü ve açılması için gereken tüm önceki düğümleri plana ekler.</summary>
        void AddWithPrereqs(string id)
        {
            var stack = new Stack<string>(); stack.Push(id);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (!plan.Add(cur) || !w.NodeById.TryGetValue(cur, out var n)) continue;
                foreach (var r in n.Requires) stack.Push(r);
            }
        }

        void RefreshTree(WNode select)
        {
            rows = BuildRows(SecTree);
            ApplyFilter(select);
        }

        void PlanCard(StackPanel sp, WNode current)
        {
            var nodes = plan.Select(id => w.NodeById.TryGetValue(id, out var n) ? n : null).Where(n => n != null).ToList();
            var card = new StackPanel();
            card.Children.Add(new TextBlock { Text = "SEVKİYAT PLANLAYICISI", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = B("#64D2FF") });
            if (nodes.Count == 0)
                card.Children.Add(new TextBlock { Text = "Açmak istediğin düğümleri plana ekle; araştırma ve satın alma bedellerini toplar. Gereken önceki düğümler kendiliğinden eklenir.", FontSize = 12.5, Foreground = B("#C7C7CC"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
            else
            {
                var stats = new UniformGrid { Columns = 4, Margin = new Thickness(0, 8, 0, 0) };
                foreach (var (label, value) in new[] { ("Düğüm", nodes.Count.ToString()), ("Araştırma", Wiki.Money(nodes.Sum(n => n.Research ?? 0))), ("Satın alma", Wiki.Money(nodes.Sum(n => n.Price ?? 0))), ("En uzun", nodes.Max(n => n.Years) > 0 ? nodes.Max(n => n.Years) + " yıl" : "—") })
                {
                    var s = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
                    s.Children.Add(new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Height = 22, Child = new TextBlock { Text = value, FontFamily = Display, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Label } });
                    s.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Secondary });
                    stats.Children.Add(s);
                }
                card.Children.Add(stats);
            }
            var row = new StackPanel();
            Buttons(row,
                (w.Guides?.StarterPack.Count > 0 ? "Başlangıç paketi" : null, false, () => { foreach (var id in w.Guides.StarterPack) AddWithPrereqs(id); RefreshTree(current); }),
                (nodes.Count > 0 ? "Planı temizle" : null, false, () => { plan.Clear(); RefreshTree(current); }));
            card.Children.Add(row);
            sp.Children.Add(new Border { Background = B("#1A0A84FF"), BorderBrush = B("#550A84FF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 4), Margin = new Thickness(0, 0, 0, 18), Child = card });
        }

        void NodeDetail(StackPanel sp, WNode n)
        {
            PlanCard(sp, n);
            Hero(sp, n.Icon, n.Name, n.En, n.Rarity, n.Color, n.Branch.Name + " · " + n.Stage + ". aşama");
            Para(sp, n.Desc, 16);
            Tiles(sp, new[] { ("Araştırma", n.Research > 0 ? Wiki.Money(n.Research) : "Ücretsiz"), ("Satın alma", Wiki.Money(n.Price)), ("Süre", n.Years > 0 ? n.Years + " yıl" : null), ("Aşama", n.Stage.ToString()) });
            bool inPlan = plan.Contains(n.Id);
            Buttons(sp,
                (inPlan ? "Plandan çıkar" : "Plana ekle (gerekenlerle)", !inPlan, () => { if (inPlan) plan.Remove(n.Id); else AddWithPrereqs(n.Id); RefreshTree(n); }),
                (n.ItemId != null ? "Eşya sayfası" : null, false, () => OpenItem(n.ItemId)?.Invoke()));
            var req = n.Requires.Select(id => w.NodeById.TryGetValue(id, out var r) ? r : null).Where(r => r != null).ToList();
            Header(sp, "Önce açılması gerekenler");
            if (req.Count == 0) Card(sp, new[] { ((string)null, "Yok, dalın başlangıç düğümü", (string)null, (Action)null) });
            else Card(sp, req.Select(r => (r.Icon, (plan.Contains(r.Id) ? "✓ " : "") + r.Name, r.Stage + ". aşama", OpenNode(r.Id))));
            var kids = n.Unlocks.Select(id => w.NodeById.TryGetValue(id, out var r) ? r : null).Where(r => r != null).ToList();
            if (kids.Count > 0) { Header(sp, "Bunu açınca"); Card(sp, kids.Select(r => (r.Icon, (plan.Contains(r.Id) ? "✓ " : "") + r.Name, r.Stage + ". aşama", OpenNode(r.Id)))); }
        }

        void BarterDetail(StackPanel sp, WBarter b)
        {
            w.ItemById.TryGetValue(b.ItemId ?? "", out var it);
            Hero(sp, b.Icon, b.Name, it?.En, it?.Rarity, it?.Color, "Takas");
            Para(sp, b.Usage, 18);
            Header(sp, "Gerekenler");
            Card(sp, b.Inputs.Select(x => (x.Icon, x.Name, x.ItemId == "cash_1" ? Wiki.Money(x.Qty) : "× " + x.Qty.ToString("N0", Tr), OpenItem(x.ItemId))));
            if (b.Npc != null) { Header(sp, "Nerede yapılır"); Card(sp, new[] { ((string)null, b.Npc, (string)null, (Action)null) }); }
            if (it != null) { Header(sp, "Sonuç"); Card(sp, new[] { (it.Icon, it.Name, Price(it.Price), OpenItem(it.Id)) }); }
        }

        void BuildingDetail(StackPanel sp, WBuilding b)
        {
            var first = b.ItemIds.Select(id => w.ItemById.TryGetValue(id, out var it) ? it : null).FirstOrDefault(x => x != null);
            Hero(sp, first?.Icon, b.Name, b.En, null, null, b.Type);
            Para(sp, b.Desc, 18);
            var poi = w.Map?.Pois.FirstOrDefault(p => p.Name == b.Name || (b.En != null && p.En == b.En));
            if (poi != null) Buttons(sp, ("Haritada göster", true, () => { Show(SecMap); mapView.ClearFilters(); mapView.Reveal(poi); }));
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
                    ToolTip = it.Name + (Price(it.Price) != null ? " · " + Price(it.Price) : ""),
                    Child = new Image { Source = Wiki.Icon(it.Icon), Margin = new Thickness(8) },
                };
                var target = it;
                tile.MouseEnter += (s, e) => tile.Background = B("#3A3A3C");
                tile.MouseLeave += (s, e) => tile.Background = Surface;
                tile.MouseLeftButtonUp += (s, e) => Show(SecItems, target);
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

        // ------------------------------------------------------------------ rehberler

        void GuideDetail(StackPanel sp, object v)
        {
            switch (v)
            {
                case WStep s:
                    Hero(sp, null, s.Title, null, s.Badge, "#FF375F", s.No, "", "#FF375F");
                    Para(sp, s.Text, 18);
                    if (s.Points.Count > 0) { sp.Children.Add(new Border { Height = 12 }); Card(sp, s.Points.Select(p => ((string)null, p, (string)null, (Action)null))); }
                    if (s.Tips.Count > 0) { Header(sp, "İpucu"); Card(sp, s.Tips.Select(p => ((string)null, p, (string)null, (Action)null))); }
                    if (s.ActionType == "poi_kind" && w.Map != null)
                        Buttons(sp, (s.ActionLabel ?? "Haritada göster", true, () => ShowOnMap(m => m.SetKindFilter(s.ActionTarget))));
                    else if (s.ActionType == "starter_tree")
                        Buttons(sp, (s.ActionLabel ?? "Başlangıç paketini seç", true, () =>
                        {
                            foreach (var id in w.Guides.StarterPack) AddWithPrereqs(id);
                            var first = w.Guides.StarterPack.Select(id => w.NodeById.TryGetValue(id, out var n) ? n : null).FirstOrDefault(n => n != null);
                            Show(SecTree, first);
                        }));
                    break;
                case WMoney m:
                    Hero(sp, m.Icon, m.Title, null, m.Rank, "#30D158", "Para kazanma");
                    Tiles(sp, new[] { ("Zorluk", m.Difficulty), ("Süre", m.Time), ("Kazanç", m.Payout) }, 3);
                    if (m.Needs != null) { Header(sp, "Gerekenler"); Card(sp, new[] { ((string)null, m.Needs, (string)null, (Action)null) }); }
                    if (m.How != null) { Header(sp, "Nasıl yapılır"); Para(sp, m.How, 0); }
                    break;
                case ValueTuple<string, string, string> mk:
                    Hero(sp, null, mk.Item2, null, "Sık yapılan hata " + mk.Item1, "#FF9F0A", null, "", "#FF9F0A");
                    Para(sp, mk.Item3, 18);
                    break;
            }
        }

        void DeviceDetail(StackPanel sp, WDevice d)
        {
            Hero(sp, d.Icon, d.Name, null, d.Badge, d.Color, "Birikimli cihaz");
            Tiles(sp, new[] { ("Dolması için", d.Points + " puan"), ("Araştırma", Price(d.Research)), ("Satın alma", Price(d.Price)) }, 3);
            var buttons = new List<(string, bool, Action)>();
            if (w.Map != null && w.Map.Layers.Any(l => l.Id == "CumulativeChargerView"))
                buttons.Add(("Şarj noktalarını haritada göster", true, () => ShowOnMap(m => { m.ClearFilters(); m.SetLayer("CumulativeChargerView", true); })));
            if (d.ItemId != null) buttons.Add(("Eşya sayfası", false, () => OpenItem(d.ItemId)?.Invoke()));
            if (w.NodeById.ContainsKey(d.Id)) buttons.Add(("Sevkiyatlarda göster", false, () => OpenNode(d.Id)?.Invoke()));
            if (buttons.Count > 0) Buttons(sp, buttons.ToArray());
            if (d.Prereq != null) { Header(sp, "Ön şart"); Para(sp, d.Prereq, 0); }
            if (d.HowToFill != null) { Header(sp, "Nasıl doldurulur"); Para(sp, d.HowToFill, 0); }
            if (d.WhenFull != null) { Header(sp, "Dolunca"); Para(sp, d.WhenFull, 0); }
            if (d.Barters.Count > 0) { Header(sp, "Takaslarda"); Card(sp, d.Barters.Select(b => ((string)null, b, (string)null, (Action)null))); }
        }
    }
}
