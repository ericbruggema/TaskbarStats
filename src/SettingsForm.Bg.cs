using System.Drawing.Drawing2D;

namespace TaskbarStats;

public sealed partial class SettingsForm
{
    /// <summary>
    /// Blok "Achtergrondafbeelding" voor één venster: bestandskiezer + Wissen + miniatuur, modus en dekking.
    /// Wordt vanuit de tabbladen Widget, Dashboard en Fullscreen met één regel aangeroepen.
    /// </summary>
    private void BgSection(Control parent, Func<string?> getPath, Action<string?> setPath,
                           Func<BgMode> getMode, Action<BgMode> setMode, Func<int> getOpacity, Action<int> setOpacity)
    {
        void add(Control c)
        {
            if (parent is FlowLayoutPanel f) f.Controls.Add(c); else parent.Controls.Add(c);
        }
        add(Head(Loc.Pick("Achtergrondafbeelding", "Background image")));

        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        var thumb = new PictureBox { Width = 80, Height = 45, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 8, 0) };
        var name = new Label { AutoSize = false, Width = 170, Height = 22, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 0, 2) };
        var pick = Btn(Loc.Pick("Kies…", "Choose…"), 80);
        var clear = Btn(Loc.Pick("Wissen", "Clear"), 80);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
        buttons.Controls.Add(pick);
        buttons.Controls.Add(clear);
        var side = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        side.Controls.Add(name);
        side.Controls.Add(buttons);
        row.Controls.Add(thumb);
        row.Controls.Add(side);

        string shown = "";
        void refresh()
        {
            var p = getPath();
            bool has = !string.IsNullOrWhiteSpace(p);
            name.Text = has ? Path.GetFileName(p) : Loc.Pick("(geen afbeelding)", "(no image)");
            clear.Enabled = has;
            if (p == shown) return;
            shown = p ?? "";
            var old = thumb.Image; thumb.Image = null; old?.Dispose();
            if (!has) return;
            string want = shown;
            var ui = SynchronizationContext.Current;   // de controls in andere tabbladen hebben nog geen handle
            Task.Run(() => MakeThumb(want)).ContinueWith(t =>
            {
                var img = t.Result;
                if (img is null) return;
                void set()
                {
                    if (thumb.IsDisposed || want != shown) { img.Dispose(); return; }
                    var o = thumb.Image; thumb.Image = img; o?.Dispose();
                }
                if (ui is null) img.Dispose(); else ui.Post(_ => set(), null);
            }, TaskScheduler.Default);
        }
        pick.Click += (_, _) =>
        {
            var f = BgLayer.Pick(getPath());
            if (f is null) return;
            setPath(f);
            refresh();
            Changed();
        };
        clear.Click += (_, _) => { setPath(null); refresh(); Changed(); };
        add(row);
        refresh();
        row.Disposed += (_, _) => { var o = thumb.Image; thumb.Image = null; o?.Dispose(); };

        var modes = new (string, BgMode)[]
        {
            (Loc.Pick("Uitgerekt", "Stretch"), BgMode.Stretch), (Loc.Pick("Vullen", "Fill"), BgMode.Fill), (Loc.Pick("Passen", "Fit"), BgMode.Fit),
            (Loc.Pick("Tegelen", "Tile"), BgMode.Tile), (Loc.Pick("Gecentreerd", "Center"), BgMode.Center),
        };
        var seg = Seg(modes, getMode, setMode, 56);
        seg.MaximumSize = new Size(440, 0);
        var op = Slider(0, 100, 5, getOpacity, setOpacity, "%");
        add(Row(Loc.Pick("Modus", "Mode"), seg, 60));
        add(Row(Loc.Pick("Dekking", "Opacity"), op, 60));
        Dep(() => { seg.Enabled = op.Enabled = !string.IsNullOrWhiteSpace(getPath()); });
    }

    /// <summary>Kleine miniatuur (max. 160 px) voor het instellingenvenster; null bij een fout.</summary>
    private static Bitmap? MakeThumb(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            using var ms = new MemoryStream(data);
            using var img = Image.FromStream(ms, false, false);
            double k = Math.Min(1.0, 160.0 / Math.Max(img.Width, img.Height));
            var bmp = new Bitmap(Math.Max(1, (int)(img.Width * k)), Math.Max(1, (int)(img.Height * k)));
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, 0, 0, bmp.Width, bmp.Height);
            return bmp;
        }
        catch { return null; }
    }
}
