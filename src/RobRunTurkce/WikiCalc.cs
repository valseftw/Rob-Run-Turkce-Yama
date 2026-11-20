using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RobRunTurkce
{
    /// <summary>Hesaplayıcılar bölümündeki bir hesaplayıcı (tek örnekler).</summary>
    public sealed class CalcKind
    {
        public readonly string Id, Title, Sub, Icon;
        CalcKind(string id, string title, string sub, string icon) { Id = id; Title = title; Sub = sub; Icon = icon; }
        public static readonly CalcKind Fence = new CalcKind("tefeci", "Tefeci satış hesabı", "Aranma seviyesine göre çanta değeri", "alcohol_13.png");
        public static readonly CalcKind Cargo = new CalcKind("kargo", "Kaçak kargo kârı", "Süre, yuva ve satış bonusuna göre", "cargo_alcohol_grade_1.png");
        public static readonly CalcKind Heist = new CalcKind("soygun", "Bina soygunu getirisi", "Çanta yuvasına göre tahmini değer", "jewelry_pouch_1.png");
        public static readonly CalcKind[] All = { Fence, Cargo, Heist };
    }

    public sealed partial class WikiView
    {
        // Hesaplayıcı durumları (bölümden çıkınca kaybolmasın diye alanlarda)
        readonly List<(WItem item, int qty)> sellList = new List<(WItem, int)>();
        int fenceLevel = 1;
        double cargoBuy = 10000, cargoSell = 42500, cargoMinutes = 30, cargoBonus = 0; int cargoSlots = 1;
        WBuilding heistBuilding; int heistSlots = 12, heistLevel = 1;

        static readonly CultureInfo Num0 = CultureInfo.GetCultureInfo("en-US");
        static string M(double v) => "$" + Math.Round(v).ToString("N0", Num0);

        long FencePrice(WItem it, int level)
        {
            var f = it.Fence.FirstOrDefault(x => x.tier == level);
            if (f.price > 0) return f.price;
            if (it.Fence.Count > 0) return it.Fence.OrderBy(x => Math.Abs(x.tier - level)).First().price;
            return it.Price ?? 0;
        }

        void CalcDetail(StackPanel sp, CalcKind k)
        {
            Hero(sp, k.Icon, k.Title, null, null, null, "Hesaplayıcı", "", null);
            var result = new StackPanel();
            if (k == CalcKind.Fence) FenceCalc(sp, result);
            else if (k == CalcKind.Cargo) CargoCalc(sp, result);
            else HeistCalc(sp, result);
            sp.Children.Add(result);
        }

        // ------------------------------------------------------------------ ortak giriş elemanları

        /// <summary>1–5 arası seviye seçici (çip görünümü).</summary>
        FrameworkElement LevelPicker(string label, int value, Action<int> set)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Secondary, Margin = new Thickness(2, 0, 0, 6) });
            var row = new WrapPanel();
            string group = "lvl" + Guid.NewGuid().ToString("N");
            for (int i = 1; i <= 5; i++)
            {
                int lv = i;
                var rb = new RadioButton { Content = lv + ". seviye", GroupName = group, IsChecked = lv == value, Style = (Style)Application.Current.FindResource("Chip") };
                rb.Checked += (s, e) => set(lv);
                rb.Margin = new Thickness(0, 0, 8, 8);
                row.Children.Add(rb);
            }
            sp.Children.Add(row);
            return sp;
        }

        /// <summary>Sayı kutusu (yazdıkça hesap güncellenir).</summary>
        FrameworkElement NumberField(string label, double value, Action<double> set, string suffix = null)
        {
            var g = new StackPanel { Margin = new Thickness(0, 0, 10, 10), Width = 150 };
            g.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Secondary, Margin = new Thickness(2, 0, 0, 5), TextWrapping = TextWrapping.Wrap });
            var box = new Grid { Height = 34 };
            box.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = Surface, BorderBrush = B("#48484A"), BorderThickness = new Thickness(1) });
            var tb = new TextBox
            {
                Text = value.ToString("0.##", Num0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Label, CaretBrush = Blue, FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, suffix != null ? 34 : 10, 0), FocusVisualStyle = null,
            };
            tb.TextChanged += (s, e) =>
            {
                var t = tb.Text.Replace("$", "").Replace(",", "").Replace(" ", "").Trim();
                if (double.TryParse(t, NumberStyles.Float, Num0, out var v) && v >= 0) set(v);
            };
            box.Children.Add(tb);
            if (suffix != null) box.Children.Add(new TextBlock { Text = suffix, FontSize = 12.5, Foreground = Secondary, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            g.Children.Add(box);
            return g;
        }

        /// <summary>Kutuya yazdıkça eşleşen eşyaları gösteren seçici. pick: seçilen eşya.</summary>
        FrameworkElement ItemPicker<T>(string hint, IEnumerable<T> source, Func<T, string> name, Func<T, string> icon, Func<T, string> value, Action<T> pick)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            var box = new Grid { Height = 36 };
            box.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Surface, BorderBrush = B("#48484A"), BorderThickness = new Thickness(1) });
            box.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 12, Foreground = Secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
            var h = new TextBlock { Text = hint, FontSize = 13.5, Foreground = B("#6C6C70"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(34, 0, 0, 0), IsHitTestVisible = false };
            box.Children.Add(h);
            var tb = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Label, CaretBrush = Blue, FontSize = 13.5, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(32, 0, 10, 0), FocusVisualStyle = null };
            box.Children.Add(tb);
            sp.Children.Add(box);
            var results = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            sp.Children.Add(results);
            var all = source.ToList();
            tb.TextChanged += (s, e) =>
            {
                h.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                results.Children.Clear();
                var q = tb.Text.Trim().ToLower(Tr);
                if (q.Length < 2) return;
                foreach (var x in all.Where(x => (name(x) ?? "").ToLower(Tr).Contains(q)).OrderBy(x => (name(x) ?? "").ToLower(Tr).StartsWith(q) ? 0 : 1).Take(6))
                {
                    var item = x;
                    var row = new Grid { Height = 36, Cursor = Cursors.Hand, Background = Brushes.Transparent };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), Background = B("#3A3A3C"), Margin = new Thickness(6, 0, 0, 0), Child = new Image { Source = Wiki.Icon(icon(item)), Margin = new Thickness(2) } });
                    var n = new TextBlock { Text = name(item), FontSize = 13, Foreground = Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                    Grid.SetColumn(n, 1); row.Children.Add(n);
                    var v = new TextBlock { Text = value(item) ?? "", FontSize = 12, Foreground = Secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                    Grid.SetColumn(v, 2); row.Children.Add(v);
                    row.MouseEnter += (s2, e2) => row.Background = B("#1FFFFFFF");
                    row.MouseLeave += (s2, e2) => row.Background = Brushes.Transparent;
                    row.MouseLeftButtonUp += (s2, e2) => { pick(item); tb.Text = ""; };
                    results.Children.Add(row);
                }
            };
            return sp;
        }

        void ResultTiles(StackPanel result, params (string label, string value)[] tiles) => Tiles(result, tiles, Math.Min(3, tiles.Length));

        // ------------------------------------------------------------------ 1) tefeci

        void FenceCalc(StackPanel sp, StackPanel result)
        {
            Para(sp, "Tefeci, çalıntı eşyaları aranma seviyene göre farklı fiyattan alır (oyun: \"Tutar aranma seviyene ve ganimetin değerine bağlıdır\"). Çantandaki eşyaları ekle, seviyeye göre toplamı gör.", 14);
            void Recalc()
            {
                result.Children.Clear();
                if (sellList.Count == 0) { Para(result, "Henüz eşya eklemedin. Yukarıdaki kutuya eşya adı yaz.", 16); return; }
                long total = sellList.Sum(x => FencePrice(x.item, fenceLevel) * x.qty), low = sellList.Sum(x => FencePrice(x.item, 1) * x.qty), high = sellList.Sum(x => FencePrice(x.item, 5) * x.qty);
                ResultTiles(result, ($"{fenceLevel}. seviyede toplam", M(total)), ("1. seviyede", M(low)), ("5. seviyede", M(high)));
                Header(result, $"Çantam ({sellList.Sum(x => x.qty)} eşya)");
                var stack = new StackPanel();
                for (int i = 0; i < sellList.Count; i++)
                {
                    int idx = i; var (it, qty) = sellList[i];
                    var g = new Grid { Margin = new Thickness(12, 6, 10, 6) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.ColumnDefinitions.Add(new ColumnDefinition());
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.Children.Add(new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(7), Background = B("#3A3A3C"), Child = new Image { Source = Wiki.Icon(it.Icon), Margin = new Thickness(2) } });
                    var nm = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                    nm.Children.Add(new TextBlock { Text = it.Name, FontSize = 13.5, Foreground = Label, TextTrimming = TextTrimming.CharacterEllipsis });
                    nm.Children.Add(new TextBlock { Text = M(FencePrice(it, fenceLevel)) + " / adet", FontSize = 11.5, Foreground = Secondary });
                    Grid.SetColumn(nm, 1); g.Children.Add(nm);
                    var step = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    step.Children.Add(IconButton("", "Azalt", () => { if (sellList[idx].qty > 1) sellList[idx] = (it, sellList[idx].qty - 1); else sellList.RemoveAt(idx); Recalc(); }));
                    step.Children.Add(new TextBlock { Text = qty.ToString(), FontSize = 13.5, Foreground = Label, Width = 30, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    step.Children.Add(IconButton("", "Artır", () => { sellList[idx] = (it, sellList[idx].qty + 1); Recalc(); }));
                    Grid.SetColumn(step, 2); g.Children.Add(step);
                    var line = new TextBlock { Text = M(FencePrice(it, fenceLevel) * qty), FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Label, Width = 90, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(line, 3); g.Children.Add(line);
                    stack.Children.Add(g);
                    if (i < sellList.Count - 1) stack.Children.Add(new Rectangle { Height = 1, Fill = Sep, Margin = new Thickness(54, 0, 0, 0) });
                }
                result.Children.Add(new Border { Background = Surface, CornerRadius = new CornerRadius(12), Child = stack });
                Buttons(result, ("Çantayı boşalt", false, () => { sellList.Clear(); Recalc(); }));
            }
            sp.Children.Add(LevelPicker("Aranma seviyen (tefeci kademesi)", fenceLevel, v => { fenceLevel = v; Recalc(); }));
            sp.Children.Add(ItemPicker("Çantaya eşya ekle…", w.Items.Where(i => i.Fence.Count > 0 || i.Price > 0), i => i.Name, i => i.Icon, i => M(FencePrice(i, fenceLevel)),
                it => { int k = sellList.FindIndex(x => x.item == it); if (k >= 0) sellList[k] = (it, sellList[k].qty + 1); else sellList.Add((it, 1)); Recalc(); }));
            Recalc();
        }

        // ------------------------------------------------------------------ 2) kaçak kargo

        void CargoCalc(StackPanel sp, StackPanel result)
        {
            Para(sp, "Kaçak Kargo'yu alıp Kaçak Mal Tezgâhı'nda yeniden paketler, Kaçakçı Kamyoneti'ne satarsın. Varsayılan değerler wiki verisinden (ham paket $10.000, paketlenmiş $35.000–$50.000, ~30 dk); oyunda gördüğün rakamları yazarak kendi hesabını yap.", 14);
            var nodeCost = new Func<string, long>(id => w.NodeById.TryGetValue(id, out var n) ? (n.Research ?? 0) + (n.Price ?? 0) : 0);
            void Recalc()
            {
                result.Children.Clear();
                double perCycle = cargoSlots * (cargoSell * (1 + cargoBonus / 100.0) - cargoBuy);
                double perHour = cargoMinutes > 0 ? perCycle * 60.0 / cargoMinutes : 0;
                ResultTiles(result, ("Tur başına kâr", M(perCycle)), ("Saatlik kâr", M(perHour)), ("Tur başına yatırım", M(cargoSlots * cargoBuy)));
                if (perCycle <= 0) { Para(result, "Bu değerlerle zarar ediyorsun: satış değeri alış fiyatından yüksek olmalı.", 10); return; }
                double perSlotHour = cargoMinutes > 0 ? (cargoSell * (1 + cargoBonus / 100.0) - cargoBuy) * 60.0 / cargoMinutes : 0;
                Header(result, "Geliştirmeler kendini ne zaman öder?");
                var rows = new List<(string, string, string, Action)>();
                void Up(string id, string label, int extraSlots)
                {
                    long c = nodeCost(id);
                    if (c <= 0 || perSlotHour <= 0) return;
                    w.NodeById.TryGetValue(id, out var n);
                    rows.Add((n?.Icon, $"{label}: +{extraSlots} yuva, bedel {M(c)}", $"{c / (perSlotHour * extraSlots):0.#} saatte", Ref(n)));
                }
                Up("cargo_exchange_workbench_additional_slot_upgrade", "Kaçakçılık Ek Slot Modülü I", 1);
                Up("cargo_exchange_workbench_additional_double_slot_upgrade", "Kaçakçılık Çift Slot Modülü II", 2);
                if (rows.Count > 0) Card(result, rows);
                Para(result, "Etiket Yazıcı paketleme süresini kısaltır; kısalan süreyi \"Paketleme süresi\" kutusuna yazarak etkisini görebilirsin. Satış bonusu bulunduğun konuma ve aranma seviyene bağlıdır.", 12);
            }
            var wrap = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            wrap.Children.Add(NumberField("Ham kargo alış fiyatı", cargoBuy, v => { cargoBuy = v; Recalc(); }, "$"));
            wrap.Children.Add(NumberField("Paketlenmiş satış değeri", cargoSell, v => { cargoSell = v; Recalc(); }, "$"));
            wrap.Children.Add(NumberField("Paketleme süresi", cargoMinutes, v => { cargoMinutes = v; Recalc(); }, "dk"));
            wrap.Children.Add(NumberField("Satış bonusu", cargoBonus, v => { cargoBonus = v; Recalc(); }, "%"));
            sp.Children.Add(wrap);
            var slots = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            slots.Children.Add(new TextBlock { Text = "Tezgâhtaki kargo yuvası", FontSize = 12, Foreground = Secondary, Margin = new Thickness(2, 0, 0, 6) });
            var srow = new StackPanel { Orientation = Orientation.Horizontal };
            string grp = "slot" + Guid.NewGuid().ToString("N");
            foreach (var (n, label) in new[] { (1, "1 (temel)"), (2, "2 (Ek Slot I)"), (3, "3 (Çift Slot II)") })
            {
                var rb = new RadioButton { Content = label, GroupName = grp, IsChecked = n == cargoSlots, Style = (Style)Application.Current.FindResource("Chip") };
                int nn = n; rb.Checked += (s, e) => { cargoSlots = nn; Recalc(); };
                srow.Children.Add(rb);
            }
            slots.Children.Add(srow);
            sp.Children.Add(slots);
            Recalc();
        }

        // ------------------------------------------------------------------ 3) bina soygunu

        void HeistCalc(StackPanel sp, StackPanel result)
        {
            Para(sp, "Bir bina seç; içinde çıkabilen eşyaların tefeci fiyatlarına göre çantanı en değerli eşyalarla doldurursan ne kadar kazanacağını tahmin eder. Oyunda hangi eşyanın kaç tane çıktığı rastgeledir; bu bir üst sınır tahminidir.", 14);
            void Recalc()
            {
                result.Children.Clear();
                if (heistBuilding == null) { Para(result, "Henüz bina seçmedin. Yukarıdaki kutuya bina adı yaz (ör. Benzin İstasyonu, Kır Evi).", 16); return; }
                var items = heistBuilding.ItemIds.Select(id => w.ItemById.TryGetValue(id, out var it) ? it : null).Where(i => i != null && FencePrice(i, heistLevel) > 0)
                    .OrderByDescending(i => FencePrice(i, heistLevel)).ToList();
                if (items.Count == 0) { Para(result, "Bu binada fiyatı bilinen eşya yok.", 16); return; }
                var best = items.Take(heistSlots).ToList();
                double avg = items.Average(i => (double)FencePrice(i, heistLevel));
                double bestSum = best.Sum(i => (double)FencePrice(i, heistLevel)) + (heistSlots > best.Count ? (heistSlots - best.Count) * FencePrice(items[0], heistLevel) * 0 : 0);
                ResultTiles(result, ($"En iyi {Math.Min(heistSlots, items.Count)} eşya", M(bestSum)), ("Ortalama doluluk", M(avg * heistSlots)), ("Bina", heistBuilding.Name));
                Header(result, $"En değerli eşyalar ({heistLevel}. seviye fiyatı)");
                Card(result, best.Select(i => (i.Icon, i.Name, M(FencePrice(i, heistLevel)), Ref(i))));
                Para(result, "\"Ortalama doluluk\": binadaki eşyaların ortalama fiyatı × çanta yuvası. Gerçek kazanç genelde bu ikisinin arasındadır.", 12);
            }
            sp.Children.Add(ItemPicker("Bina seç…", w.Buildings, b => b.Name, b => b.ItemIds.Select(id => w.ItemById.TryGetValue(id, out var it) ? it.Icon : null).FirstOrDefault(x => x != null), b => b.ItemIds.Count + " eşya",
                b => { heistBuilding = b; Recalc(); }));
            var wrap = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            wrap.Children.Add(NumberField("Çanta yuvası", heistSlots, v => { heistSlots = Math.Max(1, Math.Min(200, (int)v)); Recalc(); }, "yuva"));
            sp.Children.Add(wrap);
            sp.Children.Add(LevelPicker("Satarken aranma seviyen", heistLevel, v => { heistLevel = v; Recalc(); }));
            Recalc();
        }
    }
}
