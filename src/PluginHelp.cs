namespace TaskbarStats;

/// <summary>Een formulevoorbeeld voor het hulpvenster: <see cref="Where"/> = in welke sleutel je het typt (value of format), <see cref="Gives"/> = wat het oplevert op <see cref="PluginHelp.SampleJson"/>.</summary>
public sealed record PluginFormulaExample(string Where, string Text, string Gives, string Meaning);

/// <summary>Een compleet recept met het antwoord waarop het draait en wat je erdoor ziet.</summary>
public sealed record PluginRecipeExample(string Title, string Explain, string Reply, string Recipe, string Result);

/// <summary>
/// De uitleg en voorbeelden in het hulpvenster (Instellingen > Plugins > Help en voorbeelden). De voorbeelden worden in de tests van de
/// plugin-host echt uitgevoerd (<c>PluginHelpTests</c>): wat hier als resultaat staat, is wat de formuletaal werkelijk geeft.
/// De formules en recepten zelf zijn code en staan buiten de vertaling (// nolang); de uitleg eromheen wordt vertaald.
/// </summary>
public static class PluginHelp
{
    /// <summary>Het antwoord waarop alle formulevoorbeelden draaien (zo kan het antwoord van een webadres eruitzien).</summary>
    public const string SampleJson =   // nolang
        "{\"ads_percentage_today\": 62.5, \"status\": \"enabled\", \"free_gb\": 1234.567, \"title\": \"  Hello World  \",\n" +
        " \"queries\": [{\"name\": \"a\", \"ms\": 20}, {\"name\": \"b\", \"ms\": 40}], \"limit\": {\"used\": 32}}";

    private static PluginFormulaExample V(string text, string gives, string meaning) => new("value", text, gives, meaning);
    private static PluginFormulaExample F(string text, string gives, string meaning) => new("format", text, gives, meaning);

    /// <summary>Formules met het resultaat op <see cref="SampleJson"/>.</summary>
    public static List<PluginFormulaExample> Formulas() => new()
    {
        V("round(ads_percentage_today)", "63", Loc.T("Rounds to a whole number (.5 rounds up).")),
        V("pct(limit.used, 50)", "64", Loc.T("What percentage 32 is of 50.")),
        V("clamp(free_gb, 0, 1000)", "1000", Loc.T("Keeps a number between a lowest and a highest value.")),
        V("max(5, limit.used, 10)", "32", Loc.T("The largest of several numbers (min() gives the smallest).")),
        F("{ads_percentage_today:0.0}%", "62.5%", Loc.T("Text with a number: one decimal, then a percent sign.")),
        F("{free_gb:#,##0} GB", "1,235 GB", Loc.T("Thousands separators and no decimals.")),
        F("{status == 'enabled' ? 'on' : 'OFF'}", "on", Loc.T("A choice inside a text: this or that.")),
        V("upper(trim(title))", "HELLO WORLD", Loc.T("Removes spaces around the text and makes it capitals.")),
        V("replace(trim(title), 'World', 'there')", "Hello there", Loc.T("Replaces a piece of text.")),
        V("regex(trim(title), '(\\w+)$')", "World", Loc.T("Takes the last word with a pattern (regular expression).")),
        V("contains(status, 'able')", "true", Loc.T("Is a piece of text in there? (true or false)")),
        V("count(queries)", "2", Loc.T("How many items are in a list.")),
        V("avg(pluck(queries, 'ms'))", "30", Loc.T("pluck() takes one field from every item of a list, avg() averages them.")),
        V("join(pluck(queries, 'name'), ', ')", "a, b", Loc.T("Joins the items of a list into one text.")),
        V("queries[1].ms", "40", Loc.T("One item of a list (counting starts at 0), then one of its fields.")),
        V("coalesce(nothing_here, 'n/a')", "n/a", Loc.T("The first of the values that exists. Missing data is never an error.")),
        V("isnull(nothing_here)", "true", Loc.T("Is the data missing?")),
        V("if(ads_percentage_today > 50, 'high', 'low')", "high", Loc.T("if(condition, then, otherwise).")),
        V("bytes(1610612736)", "1.5 GB", Loc.T("A number of bytes as KB, MB or GB.")),
        V("duration(7800)", "2h 10m", Loc.T("A number of seconds as hours and minutes.")),
    };

    /// <summary>Complete recepten met het antwoord dat ze krijgen en het resultaat. Elk wordt in de tests uitgevoerd.</summary>
    public static List<PluginRecipeExample> Recipes() => new()
    {
        new(Loc.T("A number from a web address"),
            Loc.T("The simplest recipe: read one number from a web address and show it. 'warn' turns the value amber from 30 on."),
            "{\"ads_percentage_today\": 12.3, \"ads_blocked_today\": 4321, \"dns_queries_today\": 35120}",   // nolang
            "type = recipe\ntitle = Ad blocker\nurl = http://pi.hole/admin/api.php?summaryRaw\ninterval = 60\n\n" +   // nolang
            "[blocked]\nlabel = Blocked today\nshort = Ads\nvalue = ads_percentage_today\nunit = %\nformat = {value:0.0}%\n" +
            "detail = {ads_blocked_today:#,##0} of {dns_queries_today:#,##0} queries blocked\nwarn = 30\n",
            "Ads: 12.3%"),   // nolang
        new(Loc.T("Low is bad: free disk space"),
            Loc.T("With invert = true a LOW number is the problem. It turns red under 40 and, with notify = true, you get one notification."),
            "{\"disks\": [{\"name\": \"C:\", \"free_gb\": 38.4}, {\"name\": \"D:\", \"free_gb\": 820}]}",   // nolang
            "type = recipe\ntitle = Disk space\nurl = https://example.com/api/disks\n\n" +   // nolang
            "[c_free]\nlabel = Free space on {disks[0].name}\nshort = C free\nvalue = disks[0].free_gb\nunit = GB\nmin = 0\nmax = 476\n" +
            "format = {value:0} GB\ninvert = true\nwarn = 100\ncrit = 40\nnotify = true\n",
            "C free: 38 GB (crit)"),   // nolang
        new(Loc.T("A status word with its own colour"),
            Loc.T("The value is text, so you choose the colour yourself with 'level': a formula that gives 'ok', 'warn' or 'crit'."),
            "{\"state\": \"degraded\", \"components\": [{\"name\": \"API\", \"status\": \"operational\"}]}",   // nolang
            "type = recipe\ntitle = Service status\nurl = https://example.com/status.json\n\n" +   // nolang
            "[state]\nlabel = Service status\nshort = Status\nvalue = state\nformat = {upper(state)}\n" +
            "level = state == 'operational' ? 'ok' : state == 'degraded' ? 'warn' : 'crit'\nnotify = true\n",
            "Status: DEGRADED (warn)"),   // nolang
        new(Loc.T("Several values, one only when needed"),
            Loc.T("One reply can feed many values. 'when' hides a value while its condition is false: the rain value only shows when it rains."),
            "{\"temp\": 21.4, \"rain_mm\": 0, \"wind_kmh\": 14}",   // nolang
            "type = recipe\ntitle = Garden station\nurl = https://example.com/garden.json\n\n" +   // nolang
            "[temp]\nlabel = Temperature\nshort = Temp\nvalue = temp\nformat = {value:0}°\n\n" +
            "[rain]\nlabel = Rain\nshort = Rain\nvalue = rain_mm\nformat = {value:0.0} mm\nwhen = rain_mm > 0\n\n" +
            "[wind]\nlabel = Wind\nshort = Wind\nvalue = wind_kmh\nformat = {value:0} km/h\nwidget = false\n",
            "Temp: 21°"),   // nolang
        new(Loc.T("With an API key (a secret)"),
            Loc.T("Never type a key into a recipe. Save it once under Secrets… and refer to it by name: {secret:homeassistant}. It also shows up in the approval list."),
            "{\"state\": \"on\", \"attributes\": {\"friendly_name\": \"Hallway light\"}}",   // nolang
            "type = recipe\ntitle = Home Assistant lamp\nurl = http://homeassistant.local:8123/api/states/light.hallway\n" +   // nolang
            "header = Authorization: Bearer {secret:homeassistant}\nhelp = Save the token first: Settings > Plugins > Secrets, name 'homeassistant'.\n\n" +
            "[lamp]\nlabel = {attributes.friendly_name}\nshort = Hall\nvalue = state\nformat = {state == 'on' ? 'on' : 'off'}\n",
            "Hall: on"),   // nolang
        new(Loc.T("A limit that resets by itself"),
            Loc.T("'reset' gives the moment the value drops back (a time as text or Unix seconds). TaskbarStats then tells you shortly before that moment (Settings > Plugins > Notifications)."),
            "{\"quota\": {\"used_percent\": 82, \"resets_at\": \"2026-11-20T18:00:00Z\"}}",   // nolang
            "type = recipe\ntitle = Monthly quota\nurl = https://example.com/quota.json\n\n" +   // nolang
            "[quota]\nlabel = Monthly quota\nshort = Quota\nvalue = quota.used_percent\nunit = %\nformat = {value:0}%\n" +
            "warn = 70\ncrit = 90\nnotify = true\nreset = quota.resets_at\n",
            "Quota: 82% (warn)"),   // nolang
    };

    /// <summary>Een voorbeeld van wat een eigen programma in <c>plugins\&lt;naam&gt;.json</c> schrijft (met <c>type = external</c> in het configbestand).</summary>
    public const string OwnProgramJson =   // nolang
        "{\n  \"version\": 1,\n  \"plugin\": \"mytool\",\n  \"title\": \"My tool\",\n  \"updated\": \"2026-11-07T12:00:00Z\",\n  \"ttl\": 900,\n  \"status\": \"ok\",\n" +
        "  \"items\": [\n    {\"key\": \"queue\", \"label\": \"Jobs in the queue\", \"short\": \"Jobs\", \"value\": 12, \"min\": 0, \"max\": 50, \"unit\": \"\", \"text\": \"12\", \"level\": \"warn\"}\n  ]\n}\n";

    /// <summary>De stappen in gewone woorden.</summary>
    public static string HowItWorks() => string.Join("\r\n\r\n", new[]
    {
        Loc.T("1. Add. A plugin only exists after you add it: Settings > Plugins > Add plugin…. Pick one from the gallery (built in, or a ready-made recipe) or import a recipe file. Plugins you have not added are not shown anywhere."),
        Loc.T("2. Fill in and test. Open Edit config… to fill in what the plugin needs (an address, a name). Save and test runs it at once and shows the result below the editor."),
        Loc.T("3. Approve. Before a recipe runs you see exactly which internet addresses, files, commands and secrets it will use. It only runs after you press Allow, and you are asked again if you change it so it can do more."),
        Loc.T("4. Switch on and choose. Tick the plugin to switch it on. Under Values you choose which values appear in the widget, as text, a bar or a ring, and in which colour. The same values appear in the tooltip, the dashboard and the fullscreen cockpit."),
        Loc.T("5. Be told. With notify = true a value that turns critical gives one notification (repeating is optional). A value with a reset time also tells you shortly before it resets."),
        Loc.T("A plugin runs in a separate helper program without administrator rights; TaskbarStats itself only reads the results. The formulas can only calculate: they cannot read files, use the network or start programs."),
    });

    /// <summary>Het resultaat van een formulevoorbeeld voor de tests: de tekst die <c>{formule}</c> geeft.</summary>
    public static string TemplateOf(PluginFormulaExample e) => e.Where == "value" ? "{" + e.Text + "}" : e.Text;
}
