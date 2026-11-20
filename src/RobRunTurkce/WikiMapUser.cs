using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RobRunTurkce
{
    /// <summary>Haritadaki kişisel notlar ve rota (sağ tıklama menüsü, not düzenleyici, rota çizgisi).</summary>
    public sealed partial class WikiMap
    {
        UserData user;
        /// <summary>Notların ve rotanın saklandığı kişisel veri. Atanınca harita katmanı çizilir.</summary>
        public UserData User { get => user; set { user = value; RefreshUser(); } }

        readonly Canvas userLayer = new Canvas();
        Polyline routeLine;
        Border editor;
        object pendingRight;

        static readonly string[] NoteColors = { "#FF375F", "#FF9F0A", "#FFD60A", "#30D158", "#0A84FF", "#BF5AF2" };
        const string RouteColor = "#FFD60A";

        void InitUser()
        {
            world.Children.Add(userLayer);
            foreach (var (p, el) in pois) { var t = p; el.MouseRightButtonDown += (s, e) => pendingRight = t; }
            viewport.MouseRightButtonUp += (s, e) =>
            {
                var target = pendingRight; pendingRight = null;
                if (user == null) return;
                ShowContext(e.GetPosition(world), target);
                e.Handled = true;
            };
        }

        void UpdateUserScale(double s) { if (routeLine != null) routeLine.StrokeThickness = 5 / s; }

        /// <summary>Notları ve rotayı yeniden çizer.</summary>
        public void RefreshUser()
        {
            userLayer.Children.Clear();
            routeLine = null;
            if (user == null) return;
            // rota çizgisi ve numaralı duraklar
            if (user.Route.Count >= 2)
            {
                routeLine = new Polyline
                {
                    Stroke = new SolidColorBrush(C(RouteColor)), StrokeThickness = 5 / Math.Max(0.05, scale.ScaleX), StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Opacity = 0.9, IsHitTestVisible = false,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.6 },
                };
                foreach (var st in user.Route) routeLine.Points.Add(new Point(st.X, st.Y));
                userLayer.Children.Add(routeLine);
            }
            for (int i = 0; i < user.Route.Count; i++)
            {
                var st = user.Route[i];
                var g = new Grid { Width = 24, Height = 24, IsHitTestVisible = true };
                g.Children.Add(new Ellipse { Fill = new SolidColorBrush(C(RouteColor)), Stroke = new SolidColorBrush(C("#1C1C1E")), StrokeThickness = 2 });
                g.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(C("#1C1C1E")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                g.ToolTip = Tip($"{i + 1}. durak", st.Label);
                Place(g, st.X, st.Y);
                userLayer.Children.Add(g);
            }
            // notlar
            foreach (var n in user.Notes)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(12, 12, 12, 2), Background = new SolidColorBrush(C(n.Color)), BorderBrush = Brushes.White, BorderThickness = new Thickness(2),
                    Child = new TextBlock { Text = "", FontFamily = Icons, FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.6 },
                });
                if (!string.IsNullOrWhiteSpace(n.Text))
                    sp.Children.Add(new Border
                    {
                        Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(7, 2, 7, 3), CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromArgb(225, 28, 28, 30)), VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = n.Text.Length > 32 ? n.Text.Substring(0, 31) + "…" : n.Text, FontSize = 12, Foreground = Brushes.White },
                    });
                sp.ToolTip = Tip("Notum", n.Text);
                var note = n;
                sp.MouseRightButtonDown += (s, e) => pendingRight = note;
                // not, sol üst köşesi noktaya gelecek şekilde yerleşir; ters ölçek sol üst köşeden uygulanır
                Canvas.SetLeft(sp, n.X - 12); Canvas.SetTop(sp, n.Y - 12);
                sp.RenderTransformOrigin = new Point(0, 0);
                sp.RenderTransform = inv;
                userLayer.Children.Add(sp);
            }
        }

        // ------------------------------------------------------------------ sağ tıklama menüsü

        void ShowContext(Point at, object target)
        {
            var menu = new ContextMenu { Background = new SolidColorBrush(C("#2C2C2E")), BorderBrush = new SolidColorBrush(C("#48484A")), Foreground = Brushes.White, Padding = new Thickness(4) };
            MenuItem Item(string header, string glyph, Action a)
            {
                var mi = new MenuItem { Header = header, Foreground = Brushes.White, Padding = new Thickness(6, 5, 12, 5), Icon = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 12, Foreground = Brushes.White } };
                mi.Click += (s, e) => a();
                return mi;
            }
            if (target is WPoi p)
            {
                bool inRoute = user.Route.Any(r => r.PoiId == p.Id);
                menu.Items.Add(inRoute
                    ? Item($"Rotadan çıkar: {p.Name}", "", () => { user.Route.RemoveAll(r => r.PoiId == p.Id); user.Save(); })
                    : Item($"Rotaya ekle: {p.Name}", "", () => { AddRouteStop(p); }));
                menu.Items.Add(new Separator());
            }
            if (target is MapNote note)
            {
                menu.Items.Add(Item("Notu düzenle", "", () => OpenEditor(new Point(note.X, note.Y), note)));
                menu.Items.Add(Item("Notu sil", "", () => { user.Notes.Remove(note); user.Save(); }));
                menu.Items.Add(new Separator());
            }
            menu.Items.Add(Item("Buraya not ekle", "", () => OpenEditor(at, null)));
            menu.Items.Add(Item("Buraya rota durağı ekle", "", () => { user.Route.Add(new RouteStop { Label = "Durak", X = Math.Round(at.X, 1), Y = Math.Round(at.Y, 1) }); user.Save(); }));
            // en yakın rota durağı (ekranda ~18 px içinde) → çıkar
            var near = user.Route.Select((r, i) => (r, i, d: Math.Sqrt(Math.Pow(r.X - at.X, 2) + Math.Pow(r.Y - at.Y, 2)) * scale.ScaleX)).Where(x => x.d < 18).OrderBy(x => x.d).FirstOrDefault();
            if (near.r != null) menu.Items.Add(Item($"{near.i + 1}. durağı rotadan çıkar", "", () => { user.Route.RemoveAt(near.i); user.Save(); }));
            if (user.Route.Count > 0) menu.Items.Add(Item("Rotayı temizle", "", () => { user.Route.Clear(); user.Save(); }));
            menu.PlacementTarget = viewport;
            menu.IsOpen = true;
        }

        /// <summary>Bir binayı/noktayı rotanın sonuna ekler.</summary>
        public void AddRouteStop(WPoi p)
        {
            if (user == null || p == null || user.Route.Any(r => r.PoiId == p.Id)) return;
            user.Route.Add(new RouteStop { PoiId = p.Id, Label = p.Name, X = p.X, Y = p.Y });
            user.Save();
        }

        // ------------------------------------------------------------------ not düzenleyici

        void OpenEditor(Point worldPt, MapNote existing)
        {
            CloseEditor();
            var screen = new Point(worldPt.X * scale.ScaleX + pan.X, worldPt.Y * scale.ScaleX + pan.Y);
            string color = existing?.Color ?? NoteColors[0];
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = existing == null ? "Yeni not" : "Notu düzenle", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(C("#98989F")), Margin = new Thickness(0, 0, 0, 6) });
            var box = new TextBox
            {
                Text = existing?.Text ?? "", Width = 230, FontSize = 13.5, Foreground = Brushes.White, Background = new SolidColorBrush(C("#1C1C1E")), BorderBrush = new SolidColorBrush(C("#48484A")),
                CaretBrush = new SolidColorBrush(C("#0A84FF")), Padding = new Thickness(6, 5, 6, 5), MaxLength = 120,
            };
            sp.Children.Add(box);
            var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            void Paint()
            {
                foreach (Ellipse el in colors.Children) el.StrokeThickness = (string)el.Tag == color ? 2.5 : 0;
            }
            foreach (var c in NoteColors)
            {
                var el = new Ellipse { Width = 20, Height = 20, Fill = new SolidColorBrush(C(c)), Stroke = Brushes.White, Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, Tag = c };
                el.MouseLeftButtonUp += (s, e) => { color = (string)el.Tag; Paint(); e.Handled = true; };
                colors.Children.Add(el);
            }
            Paint();
            sp.Children.Add(colors);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var cancel = new Button { Content = "Vazgeç", Height = 30, MinWidth = 70, Style = (Style)Application.Current.FindResource("SecondaryButton") };
            var save = new Button { Content = "Kaydet", Height = 30, MinWidth = 70, Margin = new Thickness(8, 0, 0, 0), Style = (Style)Application.Current.FindResource("PrimaryButton") };
            void Save()
            {
                if (existing != null) { existing.Text = box.Text.Trim(); existing.Color = color; }
                else user.Notes.Add(new MapNote { X = Math.Round(worldPt.X, 1), Y = Math.Round(worldPt.Y, 1), Text = box.Text.Trim(), Color = color });
                CloseEditor(); user.Save();
            }
            cancel.Click += (s, e) => CloseEditor();
            save.Click += (s, e) => Save();
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) Save(); else if (e.Key == Key.Escape) CloseEditor(); };
            btns.Children.Add(cancel); btns.Children.Add(save);
            sp.Children.Add(btns);
            editor = new Border
            {
                Child = sp, Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(245, 44, 44, 46)),
                BorderBrush = new SolidColorBrush(C("#48484A")), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(Math.Max(8, Math.Min(screen.X + 14, Math.Max(8, ViewW - 270))), Math.Max(8, Math.Min(screen.Y - 20, viewport.ActualHeight - 160)), 0, 0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.5 },
            };
            editor.MouseLeftButtonDown += (s, e) => e.Handled = true;
            Children.Add(editor);
            Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
        }

        void CloseEditor() { if (editor != null) { Children.Remove(editor); editor = null; } }

        /// <summary>Not ya da durağın bulunduğu yere gider.</summary>
        public void Goto(double x, double y) => CenterOn(x, y, 0.9);
    }
}
