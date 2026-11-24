using System.Diagnostics;

namespace TaskbarStats;

/// <summary>Oproep om het project een ster op GitHub te geven: dezelfde opvallende knop in het welkomstscherm en in Over.</summary>
public static class StarLink
{
    /// <summary>Opent de GitHub-pagina van het project (daar staat de Star-knop rechtsboven).</summary>
    public static void Open()
    {
        try { Process.Start(new ProcessStartInfo(AppInfo.Url) { UseShellExecute = true }); }
        catch (Exception dex) { Diag.Swallow(dex); }
    }

    /// <summary>Gele knop met een ster; een klik opent de projectpagina.</summary>
    public static Button CreateButton(Rectangle bounds)
    {
        var b = new Button
        {
            Text = Loc.T("★  Like TaskbarStats? Give it a star on GitHub"),
            Bounds = bounds,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0xF5, 0xC5, 0x18),
            ForeColor = Color.FromArgb(0x1F, 0x29, 0x37),
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xFF, 0xD8, 0x4D);
        b.Click += (_, _) => Open();
        new ToolTip().SetToolTip(b, Loc.T("Opens the project page on GitHub. Click Star at the top right: it takes one second and helps other people find the program."));
        return b;
    }
}
