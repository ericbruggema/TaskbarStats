using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TaskbarStats;

/// <summary>Hoe de achtergrondafbeelding in het venster past.</summary>
public enum BgMode { Fill, Stretch, Fit, Tile, Center }

/// <summary>
/// Achtergrondafbeelding voor één venster (widget, dashboard of fullscreen).
/// De afbeelding wordt async geladen (via een stream, dus geen bestandsvergrendeling), verkleind tot <c>maxSrc</c>
/// en daarna één keer op de doelmaat gebakken (incl. dekking). <see cref="Get"/> geeft dat bitmapje terug of
/// <c>null</c> (geen pad, nog aan het laden, ontbrekend of corrupt bestand: dan geldt gewoon de normale achtergrond).
/// Tekenen kost daardoor één blit; herbouwen gebeurt alleen bij wijziging van pad/modus/dekking/maat.
/// </summary>
public sealed class BgLayer : IDisposable
{
    private readonly Control _owner;
    private readonly int _maxSrc;
    private readonly Action _redraw;
    private readonly bool _keepSource;

    private string _path = "";
    private int _version;
    private bool _loading, _failed, _disposed;
    private Bitmap? _src;      // verkleinde bron (PArgb)
    private Bitmap? _baked;    // op doelmaat, met dekking
    private (BgMode mode, int opacity, int w, int h) _bakedKey;

    /// <param name="maxSrc">Langste zijde waartoe de bron wordt verkleind (houdt het geheugen laag).</param>
    /// <param name="keepSource">false: bron na het bakken weggooien (bij herbouw wordt opnieuw geladen); scheelt geheugen bij het fullscreen-scherm.</param>
    /// <param name="redraw">Wordt op de UI-thread aangeroepen zodra een afbeelding klaar is met laden.</param>
    public BgLayer(Control owner, int maxSrc, Action redraw, bool keepSource = true)
    {
        _keepSource = keepSource;
        _owner = owner;
        _maxSrc = maxSrc;
        _redraw = redraw;
    }

    public Bitmap? Get(string? path, BgMode mode, int opacity, int w, int h)
    {
        path ??= "";
        if (_disposed) return null;
        if (string.IsNullOrWhiteSpace(path)) { Clear(); return null; }
        if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase))
        {
            Clear();
            _path = path;
        }
        if (w < 1 || h < 1 || opacity <= 0 || _failed) return null;
        var key = (mode, Math.Clamp(opacity, 0, 100), w, h);
        if (_baked is not null && _bakedKey == key) return _baked;
        if (_src is null)
        {
            // bron (nog) niet aanwezig: laden; intussen blijft de vorige versie zichtbaar
            if (!_loading) StartLoad();
            return _baked;
        }
        _baked?.Dispose();
        _baked = null;
        try { _baked = Bake(_src, key.Item1, key.Item2, w, h); _bakedKey = key; }
        catch { _baked?.Dispose(); _baked = null; _failed = true; }
        if (!_keepSource) { _src.Dispose(); _src = null; }   // groot scherm: geen tweede kopie in het geheugen houden
        return _baked;
    }

    /// <summary>Tekent de afbeelding op (0,0) in pixels; de aanroeper zet eventueel eerst een clip.</summary>
    public void Draw(Graphics g, string? path, BgMode mode, int opacity, int w, int h)
    {
        var b = Get(path, mode, opacity, w, h);
        if (b is null) return;
        var old = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceOver;
        g.DrawImage(b, new Rectangle(0, 0, b.Width, b.Height), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel);
        g.CompositingMode = old;
    }

    private void StartLoad()
    {
        _loading = true;
        int ver = ++_version;
        string path = _path;
        Task.Run(() =>
        {
            Bitmap? bmp = null;
            try { bmp = Load(path, _maxSrc); } catch { bmp = null; }
            void done()
            {
                if (_disposed || ver != _version) { bmp?.Dispose(); return; }
                _loading = false;
                if (bmp is null) { _failed = true; return; }   // stil terugvallen op de gewone achtergrond
                _src = bmp;
                _redraw();
            }
            try
            {
                if (_disposed || !_owner.IsHandleCreated || _owner.IsDisposed) { bmp?.Dispose(); return; }
                _owner.BeginInvoke(new Action(done));
            }
            catch { bmp?.Dispose(); }
        });
    }

    private static Bitmap? Load(string path, int maxSrc)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists || fi.Length == 0 || fi.Length > 64L * 1024 * 1024) return null;
        byte[] data;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            data = new byte[fs.Length];
            int read = 0;
            while (read < data.Length) { int n = fs.Read(data, read, data.Length - read); if (n <= 0) break; read += n; }
        }
        using var ms = new MemoryStream(data);
        using var img = Image.FromStream(ms, false, true);
        if (img.Width < 1 || img.Height < 1 || (long)img.Width * img.Height > 120_000_000L) return null;
        double k = Math.Min(1.0, (double)maxSrc / Math.Max(img.Width, img.Height));
        int w = Math.Max(1, (int)Math.Round(img.Width * k)), h = Math.Max(1, (int)Math.Round(img.Height * k));
        var res = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(res);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(img, new Rectangle(0, 0, w, h), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel);
        return res;
    }

    private static Bitmap Bake(Bitmap src, BgMode mode, int opacity, int w, int h)
    {
        var res = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(res);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var ia = new ImageAttributes();
        ia.SetColorMatrix(new ColorMatrix { Matrix33 = opacity / 100f }, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        ia.SetWrapMode(WrapMode.TileFlipXY);   // geen randlijntjes bij schalen
        void draw(RectangleF dest) =>
            g.DrawImage(src, Rectangle.Round(dest), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);

        float sw = src.Width, sh = src.Height;
        switch (mode)
        {
            case BgMode.Stretch:
                draw(new RectangleF(0, 0, w, h));
                break;
            case BgMode.Fill:
            {
                float s = Math.Max(w / sw, h / sh);
                draw(new RectangleF((w - sw * s) / 2, (h - sh * s) / 2, sw * s, sh * s));
                break;
            }
            case BgMode.Fit:
            {
                float s = Math.Min(w / sw, h / sh);
                draw(new RectangleF((w - sw * s) / 2, (h - sh * s) / 2, sw * s, sh * s));
                break;
            }
            case BgMode.Center:
                draw(new RectangleF((w - sw) / 2, (h - sh) / 2, sw, sh));
                break;
            case BgMode.Tile:
                for (int y = 0; y < h; y += src.Height)
                    for (int x = 0; x < w; x += src.Width)
                        g.DrawImage(src, new Rectangle(x, y, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                break;
        }
        return res;
    }

    private void Clear()
    {
        _version++;   // lopende load negeren
        _loading = false;
        _failed = false;
        _src?.Dispose(); _src = null;
        _baked?.Dispose(); _baked = null;
        _path = "";
    }

    public void Dispose()
    {
        _disposed = true;
        Clear();
    }

    /// <summary>Bestandskiezer voor afbeeldingen; geeft null bij annuleren.</summary>
    public static string? Pick(string? current)
    {
        using var dlg = new OpenFileDialog
        {
            Title = Loc.T("Choose a background image"),
            Filter = Loc.T("Images (png, jpg, bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*"),
        };
        try { if (!string.IsNullOrEmpty(current)) dlg.InitialDirectory = Path.GetDirectoryName(current); } catch { }
        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }
}
