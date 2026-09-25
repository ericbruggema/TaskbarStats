namespace TaskbarStats;

public sealed partial class SettingsForm
{
    /// <summary>Keuze "widget volgt Windows-thema" bovenaan het tabblad Kleuren; schakelt de eigen kleurvelden uit als het volgen aan staat.</summary>
    private void WinThemeSection(FlowLayoutPanel p, Control text, Control background, Control accent)
    {
        p.Controls.Add(Head(Loc.T("Follow Windows theme (widget)")));
        p.Controls.Add(Seg(new (string, WinThemeMode)[]
        {
            (Loc.T("Off"), WinThemeMode.Off),
            (Loc.T("Dark/light"), WinThemeMode.DarkLight),
            (Loc.T("Dark/light + accent"), WinThemeMode.DarkLightAccent),
        }, () => _c.WidgetFollowWindows, v => _c.WidgetFollowWindows = v, 100));
        p.Controls.Add(Note(Loc.T("When following, background and text are automatic (dark or light, with good contrast) and your own Text and Background fields are ignored; with \"+ accent\" so is Meter/bar. Threshold colors stay. Applies to the widget only.")));
        p.Controls.Add(Why(() => _c.WidgetFollowWindows switch
        {
            WinThemeMode.Off => null,
            WinThemeMode.DarkLightAccent => Loc.T("Text and Background below are disabled: the Windows theme decides them, and Meter/bar too."),
            _ => Loc.T("Text and Background below are disabled: the Windows theme decides them. Turn following off to pick your own."),
        }));
        Dep(() =>
        {
            text.Enabled = background.Enabled = _c.WidgetFollowWindows == WinThemeMode.Off;
            accent.Enabled = _c.WidgetFollowWindows != WinThemeMode.DarkLightAccent;
        });
    }
}
