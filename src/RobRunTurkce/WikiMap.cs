using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace RobRunTurkce
{
    /// <summary>
    /// Looty Bay etkileşimli haritası: tekerlekle yakınlaştırma (imlecin olduğu yere), sürükleyerek kaydırma,
    /// bina/nokta işaretleri (her yakınlıkta aynı boyutta), arama noktası katmanları, arama ve eşya filtresi.
    /// </summary>
    public sealed partial class WikiMap : Grid
    {
        public event Action<object> Selected; // WPoi ya da WObj
        /// <summary>Sağda haritanın üstünde duran kartın genişliği: ortalama ve sığdırma bu alanı hesaba katmaz.</summary>
        public double RightInset { get => rightInset; set { rightInset = value; tools.Margin = new Thickness(0, 0, value + 12, 12); } }
        double rightInset;
        readonly StackPanel tools;
        double ViewW => Math.Max(100, viewport.ActualWidth - RightInset);

        readonly WMap map;
        readonly Wiki wiki;
        readonly Border viewport;
        readonly Canvas world = new Canvas();
        readonly ScaleTransform scale = new ScaleTransform(1, 1);
        readonly TranslateTransform pan = new TranslateTransform();
        readonly ScaleTransform inv = new ScaleTransform(1, 1); // işaretleri sabit boyutta tutar (tüm işaretlerde ortak)
        readonly List<(WPoi poi, FrameworkElement el)> pois = new List<(WPoi, FrameworkElement)>();
        readonly List<(WObj obj, Ellipse el)> objs = new List<(WObj, Ellipse)>();
        readonly HashSet<string> visibleGroups = new HashSet<string>(), visibleLayers = new HashSet<string>();
        readonly TextBlock status = new TextBlock();
        FrameworkElement selectedEl;
        string query = "", itemFilter, kindFilter, buildingFilter;
        bool fitted, dragging, moved;
        Point dragStart, panStart;
        object pendingClick;

        static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
        static Color C(string hex) { try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return Colors.Gray; } }
        static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly Brush dotStroke = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));

        public static readonly Dictionary<string, string> GroupColor = new Dictionary<string, string>
        {
            ["sell"] = "#30D158", ["heists"] = "#FF453A", ["shops"] = "#0A84FF", ["services"] = "#64D2FF", ["other"] = "#FF9F0A",
        };
        public static string Glyph(WPoi p)
        {
            switch (p.Kind)
            {
                case "fence": return "";            // ödeme kartı
                case "smuggler": case "car_terminal": return ""; // araç
                case "heist_bank": return "";       // banka
                case "heist_gallery": return "";    // resim
                case "fuse_box": return "";         // şimşek
                case "hospital": return "";         // sağlık
                case "exit": return "";             // konum
            }
            switch (p.Group)
            {
                case "heists": return "";
                case "shops": return "";
                case "other": return "";
                default: return "";
            }
        }

        public WikiMap(WMap map, Wiki wiki)
        {
            this.map = map; this.wiki = wiki;
            world.Width = map.Width; world.Height = map.Height;
            var tg = new TransformGroup(); tg.Children.Add(scale); tg.Children.Add(pan);
            world.RenderTransform = tg;
            var bg = Wiki.Icon(map.Image);
            if (bg != null) world.Children.Add(new Image { Source = bg, Width = map.Width, Height = map.Height, Stretch = Stretch.Fill });
            RenderOptions.SetBitmapScalingMode(world, BitmapScalingMode.HighQuality);

            // bölge adları
            foreach (var (name, x, y) in map.Districts)
            {
                var t = new TextBlock { Text = name, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), IsHitTestVisible = false,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.9 } };
                Place(t, x, y);
                world.Children.Add(t);
            }
            // arama noktaları (küçük renkli noktalar)
            var layerColor = map.Layers.ToDictionary(l => l.Id, l => l.Color);
            foreach (var o in map.Objects)
            {
                var c = C(layerColor.TryGetValue(o.Layer ?? "", out var lc) ? lc : "#999999");
                var e = new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(c), Stroke = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), StrokeThickness = 1, Cursor = Cursors.Hand, Visibility = Visibility.Collapsed };
                e.ToolTip = Tip(o.Name, o.Building);
                var target = o;
                e.MouseLeftButtonDown += (s, ev) => pendingClick = target;
                Place(e, o.X, o.Y);
                world.Children.Add(e);
                objs.Add((o, e));
            }
            // binalar ve önemli noktalar
            foreach (var p in map.Pois)
            {
                var col = C(GroupColor.TryGetValue(p.Group ?? "", out var gc) ? gc : "#8E8E93");
                var g = new Grid { Width = 26, Height = 26, Cursor = Cursors.Hand };
                g.Children.Add(new Ellipse { Fill = new SolidColorBrush(col), Stroke = Brushes.White, StrokeThickness = 2,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.6 } });
                g.Children.Add(new TextBlock { Text = Glyph(p), FontFamily = Icons, FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                g.ToolTip = Tip(p.Name, p.KindName);
                var target = p;
                g.MouseLeftButtonDown += (s, ev) => pendingClick = target;
                Place(g, p.X, p.Y);
                world.Children.Add(g);
                pois.Add((p, g));
            }

            viewport = new Border { CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(C("#61ADB5")) /* haritanın deniz rengi */, ClipToBounds = true, Child = world, Cursor = Cursors.SizeAll };
            Children.Add(viewport);

            // yakınlaştırma düğmeleri ve durum yazısı
            tools = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 12, 12) };
            tools.Children.Add(ToolButton("", () => ZoomBy(1.5)));
            tools.Children.Add(ToolButton("", () => ZoomBy(1 / 1.5)));
            tools.Children.Add(ToolButton("", Fit));
            Children.Add(tools);
            status.Foreground = Brushes.White; status.FontSize = 12; status.Margin = new Thickness(10, 5, 10, 6);
            Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(190, 28, 28, 30)), CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12), Child = status, IsHitTestVisible = false });

            viewport.MouseWheel += (s, e) => { ZoomAt(e.GetPosition(viewport), e.Delta > 0 ? 1.25 : 0.8); e.Handled = true; };
            viewport.MouseLeftButtonDown += (s, e) => { dragging = true; moved = false; dragStart = e.GetPosition(viewport); panStart = new Point(pan.X, pan.Y); viewport.CaptureMouse(); };
            viewport.MouseMove += (s, e) =>
            {
                if (!dragging) return;
                var p = e.GetPosition(viewport);
                if (!moved && (p - dragStart).Length < 4) return;
                moved = true;
                pan.X = panStart.X + (p.X - dragStart.X); pan.Y = panStart.Y + (p.Y - dragStart.Y);
                Clamp();
            };
            viewport.MouseLeftButtonUp += (s, e) =>
            {
                dragging = false; viewport.ReleaseMouseCapture();
                var click = pendingClick; pendingClick = null;
                if (!moved && click != null) Select(click);
            };
            viewport.SizeChanged += (s, e) => { if (!fitted && viewport.ActualWidth > 0) { fitted = true; Fit(); } else Clamp(); };

            // varsayılan: tüm bina/nokta grupları açık, arama noktası katmanları kapalı (927 nokta kalabalık yapar)
            foreach (var g in map.Groups) visibleGroups.Add(g.Id);
            Apply();
            InitUser();
        }

        static ToolTip Tip(string title, string sub) => new ToolTip
        {
            Background = new SolidColorBrush(C("#2C2C2E")), BorderBrush = new SolidColorBrush(C("#3A3A3C")), Foreground = Brushes.White, Padding = new Thickness(9, 5, 9, 6),
            Content = new StackPanel { Children = { new TextBlock { Text = title, FontWeight = FontWeights.SemiBold }, new TextBlock { Text = sub ?? "", FontSize = 11.5, Foreground = new SolidColorBrush(C("#98989F")), Visibility = string.IsNullOrEmpty(sub) ? Visibility.Collapsed : Visibility.Visible } } },
        };

        Border ToolButton(string glyph, Action click)
        {
            var b = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 6, 0, 0), Background = new SolidColorBrush(Color.FromArgb(220, 44, 44, 46)), Cursor = Cursors.Hand,
                BorderBrush = new SolidColorBrush(C("#48484A")), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            b.MouseLeftButtonUp += (s, e) => { click(); e.Handled = true; };
            b.MouseLeftButtonDown += (s, e) => e.Handled = true;
            return b;
        }

        /// <summary>İşareti haritadaki noktaya ortalar; yakınlaştırmada kendi merkezinde ters ölçeklenir.</summary>
        void Place(FrameworkElement el, double x, double y)
        {
            el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = double.IsNaN(el.Width) ? el.DesiredSize.Width : el.Width, h = double.IsNaN(el.Height) ? el.DesiredSize.Height : el.Height;
            Canvas.SetLeft(el, x - w / 2); Canvas.SetTop(el, y - h / 2);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = inv;
        }

        // ------------------------------------------------------------------ kamera

        double MinScale => viewport.ActualWidth <= 0 ? 0.1 : Math.Min(ViewW / map.Width, viewport.ActualHeight / map.Height) * 0.95;
        const double MaxScale = 2.5;

        void SetScale(double s)
        {
            s = Math.Max(MinScale, Math.Min(MaxScale, s));
            scale.ScaleX = scale.ScaleY = s;
            // ekrandaki işaret boyutu: uzaktayken %60, yakınlaştıkça %100'e çıkar, sonra sabit kalır
            inv.ScaleX = inv.ScaleY = (0.6 + 0.4 * Math.Min(1.0, s / 0.8)) / s;
            UpdateUserScale(s);
        }

        void ZoomAt(Point at, double factor)
        {
            double old = scale.ScaleX;
            SetScale(old * factor);
            double k = scale.ScaleX / old;
            pan.X = at.X - (at.X - pan.X) * k; pan.Y = at.Y - (at.Y - pan.Y) * k;
            Clamp();
        }

        void ZoomBy(double f) => ZoomAt(new Point(ViewW / 2, viewport.ActualHeight / 2), f);

        public void Fit()
        {
            if (viewport.ActualWidth <= 0) return;
            SetScale(MinScale);
            pan.X = (ViewW - map.Width * scale.ScaleX) / 2;
            pan.Y = (viewport.ActualHeight - map.Height * scale.ScaleX) / 2;
        }

        /// <summary>Görünen (filtrelenmiş) işaretlerin hepsi ekrana sığacak şekilde yaklaşır; hiç yoksa tüm haritayı gösterir.</summary>
        public void FitToVisible()
        {
            var pts = pois.Where(x => x.el.Visibility == Visibility.Visible).Select(x => new Point(x.poi.X, x.poi.Y))
                .Concat(objs.Where(x => x.el.Visibility == Visibility.Visible).Select(x => new Point(x.obj.X, x.obj.Y))).ToList();
            if (pts.Count == 0 || viewport.ActualWidth <= 0) { Fit(); return; }
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double s = Math.Min(ViewW / (maxX - minX + 400), viewport.ActualHeight / (maxY - minY + 400));
            CenterOn((minX + maxX) / 2, (minY + maxY) / 2, Math.Min(1.2, s));
        }
        /// <summary>Haritanın görüş alanından tamamen çıkmasını engeller.</summary>
        void Clamp()
        {
            double w = map.Width * scale.ScaleX, h = map.Height * scale.ScaleX, vw = ViewW, vh = viewport.ActualHeight, m = 80;
            pan.X = w + 2 * m < vw ? (vw - w) / 2 : Math.Min(m, Math.Max(vw - w - m, pan.X));
            pan.Y = h + 2 * m < vh ? (vh - h) / 2 : Math.Min(m, Math.Max(vh - h - m, pan.Y));
        }

        /// <summary>Kamerayı noktaya yumuşakça götürür.</summary>
        public void CenterOn(double x, double y, double zoom = 1.1)
        {
            if (viewport.ActualWidth <= 0) { Dispatcher.BeginInvoke(new Action(() => CenterOn(x, y, zoom)), System.Windows.Threading.DispatcherPriority.Loaded); return; }
            SetScale(zoom);
            double tx = ViewW / 2 - x * scale.ScaleX, ty = viewport.ActualHeight / 2 - y * scale.ScaleX;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var ax = new DoubleAnimation(tx, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
            var ay = new DoubleAnimation(ty, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
            ax.Completed += (s, e) => { pan.X = tx; Clamp(); };
            ay.Completed += (s, e) => { pan.Y = ty; Clamp(); };
            pan.BeginAnimation(TranslateTransform.XProperty, ax);
            pan.BeginAnimation(TranslateTransform.YProperty, ay);
        }

        // ------------------------------------------------------------------ katmanlar ve filtreler

        public bool GroupVisible(string id) => visibleGroups.Contains(id);
        public bool LayerVisible(string id) => visibleLayers.Contains(id);
        public void SetGroup(string id, bool on) { if (on) visibleGroups.Add(id); else visibleGroups.Remove(id); Apply(); }
        public void SetLayer(string id, bool on) { if (on) visibleLayers.Add(id); else visibleLayers.Remove(id); Apply(); }
        public void SetQuery(string q) { query = (q ?? "").Trim().ToLower(Tr); Apply(); }

        public string ItemFilter => itemFilter;
        public string KindFilter => kindFilter;
        public string BuildingFilter => buildingFilter;

        /// <summary>Yalnızca bu eşyanın çıktığı noktaları gösterir (null: filtre yok).</summary>
        public int SetItemFilter(string itemId) { itemFilter = itemId; kindFilter = null; buildingFilter = null; Apply(); return objs.Count(o => o.el.Visibility == Visibility.Visible); }
        /// <summary>Yalnızca bu türdeki bina/noktaları gösterir (ör. fuse_box).</summary>
        public void SetKindFilter(string kind) { kindFilter = kind; itemFilter = null; buildingFilter = null; Apply(); }
        /// <summary>Bir binadaki tüm arama noktalarını gösterir.</summary>
        public void SetBuildingFilter(string building) { buildingFilter = building; itemFilter = null; kindFilter = null; Apply(); }
        public void ClearFilters() { itemFilter = kindFilter = buildingFilter = null; Apply(); }

        string ItemName(string id) => id != null && wiki.ItemById.TryGetValue(id, out var it) ? it.Name : id;

        void Apply()
        {
            int shownPoi = 0, shownObj = 0;
            bool q = query.Length > 0;
            foreach (var (p, el) in pois)
            {
                bool on;
                if (kindFilter != null) on = p.Kind == kindFilter;
                else if (itemFilter != null) on = p.Loot.Any(l => l.Id == itemFilter) || p.Keys.Any(k => k.Inside.Any(l => l.Id == itemFilter));
                else if (buildingFilter != null) on = p.Name == buildingFilter;
                else on = visibleGroups.Contains(p.Group);
                if (on && q) on = (p.Name + " " + p.KindName + " " + p.En).ToLower(Tr).Contains(query);
                el.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                if (on) shownPoi++;
            }
            foreach (var (o, el) in objs)
            {
                bool on;
                if (itemFilter != null) on = o.Loot.Any(l => l.id == itemFilter);
                else if (buildingFilter != null) on = o.Building == buildingFilter;
                else if (kindFilter != null) on = false;
                else if (q) on = (o.Name + " " + o.Building + " " + string.Join(" ", o.Loot.Select(l => ItemName(l.id)))).ToLower(Tr).Contains(query);
                else on = visibleLayers.Contains(o.Layer);
                el.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                // filtreyle bulunan noktalar belirgin olsun
                bool strong = on && (itemFilter != null || buildingFilter != null || q);
                el.Width = el.Height = strong ? 13 : 9;
                el.Stroke = strong ? Brushes.White : dotStroke; el.StrokeThickness = strong ? 2 : 1;
                Canvas.SetLeft(el, o.X - el.Width / 2); Canvas.SetTop(el, o.Y - el.Height / 2);
                if (on) shownObj++;
            }
            status.Text = $"{shownPoi} bina/nokta · {shownObj.ToString("N0", Tr)} arama noktası";
        }

        void Select(object o)
        {
            if (selectedEl != null) selectedEl.Effect = null;
            selectedEl = o is WPoi p ? pois.FirstOrDefault(x => x.poi == p).el : o is WObj ob ? objs.FirstOrDefault(x => x.obj == ob).el : null;
            if (selectedEl != null) selectedEl.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.White, BlurRadius = 16, ShadowDepth = 0, Opacity = 1 };
            Selected?.Invoke(o);
        }

        /// <summary>Dışarıdan seçim (ör. aramadan): kamerayı noktaya götürür ve ayrıntıyı açar.</summary>
        public void Reveal(object o)
        {
            if (o is WPoi p) CenterOn(p.X, p.Y, 0.55);
            else if (o is WObj ob) CenterOn(ob.X, ob.Y, 1.0);
            Select(o);
        }
    }
}
