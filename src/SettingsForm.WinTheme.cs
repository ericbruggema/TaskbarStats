namespace TaskbarStats;

public sealed partial class SettingsForm
{
    /// <summary>Keuze "widget volgt Windows-thema" bovenaan het tabblad Kleuren; schakelt de eigen kleurvelden uit als het volgen aan staat.</summary>
    private void WinThemeSection(FlowLayoutPanel p, Control text, Control background, Control accent)
    {
        p.Controls.Add(Head(Loc.Pick("Windows-thema volgen (widget)", "Follow Windows theme (widget)")));
        p.Controls.Add(Seg(new (string, WinThemeMode)[]
        {
            (Loc.Pick("Uit", "Off"), WinThemeMode.Off),
            (Loc.Pick("Donker/licht", "Dark/light"), WinThemeMode.DarkLight),
            (Loc.Pick("Donker/licht + accent", "Dark/light + accent"), WinThemeMode.DarkLightAccent),
        }, () => _c.WidgetFollowWindows, v => _c.WidgetFollowWindows = v, 100));
        p.Controls.Add(Note(Loc.Pick(
            "Bij volgen zijn achtergrond en tekst automatisch (donker of licht, met goed contrast) en tellen de eigen velden Tekst en Achtergrond niet mee; bij \"+ accent\" ook Meter/balk niet. Drempelkleuren blijven. Geldt alleen voor het widget.",
            "When following, background and text are automatic (dark or light, with good contrast) and your own Text and Background fields are ignored; with \"+ accent\" so is Meter/bar. Threshold colors stay. Applies to the widget only.")));
        p.Controls.Add(Why(() => _c.WidgetFollowWindows == WinThemeMode.Off ? null
            : Loc.Pick("Tekst en Achtergrond hieronder zijn uitgeschakeld: het Windows-thema bepaalt ze" + (_c.WidgetFollowWindows == WinThemeMode.DarkLightAccent ? ", en ook Meter/balk." : ". Zet het volgen uit om ze zelf te kiezen."),
                       "Text and Background below are disabled: the Windows theme decides them" + (_c.WidgetFollowWindows == WinThemeMode.DarkLightAccent ? ", and Meter/bar too." : ". Turn following off to pick your own."))));
        Dep(() =>
        {
            text.Enabled = background.Enabled = _c.WidgetFollowWindows == WinThemeMode.Off;
            accent.Enabled = _c.WidgetFollowWindows != WinThemeMode.DarkLightAccent;
        });
    }
}
