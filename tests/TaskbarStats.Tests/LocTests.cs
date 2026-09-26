using Xunit;

namespace TaskbarStats.Tests;

public class LocTests : IDisposable
{
    private readonly TempData _data = new();
    public void Dispose() => _data.Dispose();

    /// <summary>Zet een eigen taalbestand neer en schakel Loc erop over.</summary>
    private void UseLang(string code, string json)
    {
        _data.Write(Path.Combine("lang", code + ".json"), json);
        Loc.ExternalDir = Path.Combine(_data.Dir, "lang");
        Loc.Lang = code;
    }

    [Fact]
    public void Engels_geeft_de_sleutel_terug_zonder_context()
    {
        Loc.Lang = "en";
        Assert.Equal("Display", Loc.T("Display@@screen"));
        Assert.Equal("Plain", Loc.T("Plain"));
    }

    [Fact]
    public void T_met_argumenten_vult_in()
    {
        Loc.Lang = "en";
        Assert.Equal("Hello Eric, 3", Loc.T("Hello {0}, {1}", "Eric", 3));
    }

    [Fact]
    public void Vertaling_wordt_gebruikt_en_context_blijft_werken()
    {
        UseLang("xx", """{ "Save": "Opslaan", "Display@@screen": "Scherm" }""");
        Assert.Equal("Opslaan", Loc.T("Save"));
        Assert.Equal("Scherm", Loc.T("Display@@screen"));
    }

    [Fact]
    public void Ontbrekende_vertaling_valt_terug_op_Engels_en_wordt_gemeld()
    {
        UseLang("xx", """{ "Save": "Opslaan" }""");
        string key = "Unknown text " + Guid.NewGuid().ToString("N");
        Assert.Equal(key, Loc.T(key));
        Assert.Contains(key, Loc.Missing);
        Assert.Equal("Cancel", Loc.T("Cancel@@dialog"));   // context wordt ook bij terugval niet getoond
    }

    [Fact]
    public void Lege_vertaling_telt_als_nog_niet_vertaald()
    {
        UseLang("xx", """{ "Save": "" }""");
        Assert.Equal("Save", Loc.T("Save"));
    }

    [Fact]
    public void Foute_vertaling_met_kapotte_accolade_valt_terug_op_Engels()
    {
        UseLang("xx", """{ "Value {0}": "Waarde {0" }""");
        Assert.Equal("Value 5", Loc.T("Value {0}", 5));
    }

    [Fact]
    public void Kapot_taalbestand_laat_Engels_werken()
    {
        UseLang("xx", "{ dit is geen json");
        Assert.Equal("Save", Loc.T("Save"));
    }

    [Fact]
    public void Meta_sleutels_zijn_geen_teksten()
    {
        UseLang("xx", """{ "_name": "Testtaal", "_culture": "nl-NL", "_plural": "other", "Save": "Opslaan" }""");
        Assert.Equal("Opslaan", Loc.T("Save"));
        // De meta-waarden staan niet als tekst in de tabel: de sleutel zelf komt terug
        Assert.Equal("_plural", Loc.T("_plural"));
        Assert.Equal("_culture", Loc.T("_culture"));
        Assert.Equal("_name", Loc.T("_name"));
    }

    [Fact]
    public void Cultuur_uit_taalbestand_bepaalt_getalopmaak()
    {
        UseLang("xx", """{ "_culture": "nl-NL", "Rate {0}": "Snelheid {0}" }""");
        Assert.Equal("Snelheid 1,5", Loc.T("Rate {0}", 1.5));
    }

    [Theory]
    [InlineData("one-other", 0, 1)]
    [InlineData("one-other", 1, 0)]
    [InlineData("one-other", 2, 1)]
    [InlineData("zero-or-one-other", 0, 0)]
    [InlineData("zero-or-one-other", 1, 0)]
    [InlineData("zero-or-one-other", 2, 1)]
    [InlineData("other", 1, 0)]
    [InlineData("other", 7, 0)]
    [InlineData("one-few-many-other", 1, 0)]
    [InlineData("one-few-many-other", 21, 0)]
    [InlineData("one-few-many-other", 11, 2)]
    [InlineData("one-few-many-other", 2, 1)]
    [InlineData("one-few-many-other", 24, 1)]
    [InlineData("one-few-many-other", 12, 2)]
    [InlineData("one-few-many-other", 14, 2)]
    [InlineData("one-few-many-other", 5, 2)]
    [InlineData("one-few-many-other", 0, 2)]
    [InlineData("one-few-many-other", 112, 2)]
    [InlineData("one-few-many-other", -1, 0)]   // negatief telt als absolute waarde
    [InlineData("onbekende-regel", 1, 0)]         // onbekend = one-other
    [InlineData("onbekende-regel", 5, 1)]
    public void PluralForm_volgt_de_regel(string rule, long n, int expected)
        => Assert.Equal(expected, Loc.PluralForm(rule, n));

    [Fact]
    public void P_in_het_Engels_kiest_enkelvoud_of_meervoud()
    {
        Loc.Lang = "en";
        Assert.Equal("1 day", Loc.P("{0} day|{0} days", 1));
        Assert.Equal("0 days", Loc.P("{0} day|{0} days", 0));
        Assert.Equal("5 days", Loc.P("{0} day|{0} days", 5));
    }

    [Fact]
    public void P_met_extra_waarden()
    {
        Loc.Lang = "en";
        Assert.Equal("2 files in X", Loc.P("{0} file in {1}|{0} files in {1}", 2, "X"));
    }

    [Fact]
    public void P_zonder_vertaling_valt_terug_op_Engelse_vormen()
    {
        UseLang("xx", """{ "_plural": "one-few-many-other" }""");
        Assert.Equal("1 day", Loc.P("{0} day|{0} days", 1));
        Assert.Equal("5 days", Loc.P("{0} day|{0} days", 5));
    }

    [Fact]
    public void P_met_vertaling_gebruikt_de_pluralregel_van_de_taal()
    {
        UseLang("xx", """{ "_plural": "one-few-many-other", "{0} day|{0} days": "{0} dzien|{0} dni|{0} dni_many" }""");
        Assert.Equal("1 dzien", Loc.P("{0} day|{0} days", 1));
        Assert.Equal("3 dni", Loc.P("{0} day|{0} days", 3));
        Assert.Equal("5 dni_many", Loc.P("{0} day|{0} days", 5));
    }

    [Fact]
    public void P_met_minder_vormen_dan_de_regel_gebruikt_de_laatste()
    {
        UseLang("xx", """{ "_plural": "one-other", "{0} day|{0} days": "{0} dag" }""");
        Assert.Equal("4 dag", Loc.P("{0} day|{0} days", 4));
    }

    [Fact]
    public void Pseudotaal_omhult_tekst_en_houdt_plaatsaanduidingen_heel()
    {
        Loc.Lang = Loc.Pseudo;
        string s = Loc.T("Hello {0}");
        Assert.StartsWith("[", s);
        Assert.EndsWith("!]", s);
        Assert.Contains("{0}", s);
        Assert.NotEqual("Hello {0}", s);
        Assert.Contains("Eric", Loc.T("Hello {0}", "Eric"));
        Assert.Equal(Loc.T("Save"), Loc.T("Save@@ctx"));   // context wordt eerst gestript
        Assert.True(Loc.T("A long English sentence here").Length > "A long English sentence here".Length);
        Assert.Contains("!]", Loc.P("{0} day|{0} days", 2));
    }

    [Fact]
    public void Regiotaal_vult_de_hoofdtaal_aan()
    {
        _data.Write(Path.Combine("lang", "pt.json"), """{ "_plural": "zero-or-one-other", "Hello": "Ola", "Color": "Cor" }""");
        _data.Write(Path.Combine("lang", "pt-br.json"), """{ "Color": "Cor (BR)", "_culture": "pt-BR" }""");
        Loc.ExternalDir = Path.Combine(_data.Dir, "lang");

        Loc.Lang = "pt";
        Assert.Equal("Cor", Loc.T("Color"));

        Loc.Lang = "pt-br";
        Assert.Equal("Cor (BR)", Loc.T("Color"));   // regio overschrijft
        Assert.Equal("Ola", Loc.T("Hello"));         // rest komt van pt
    }

    [Fact]
    public void Lang_setter_normaliseert_en_leeg_wordt_Engels()
    {
        Loc.Lang = "  NL ";
        Assert.Equal("nl", Loc.Lang);
        Loc.Lang = "";
        Assert.Equal("en", Loc.Lang);
    }

    [Fact]
    public void Ingebedde_taal_nl_wordt_geladen()
    {
        Loc.Lang = "nl";
        Assert.Equal("Instellingen", Loc.T("Settings"));
    }

    [Fact]
    public void N_en_Pick()
    {
        Loc.Lang = "nl";
        Assert.Equal("x", Loc.N("x"));
        Assert.Equal("ja", Loc.Pick("ja", "yes"));
        Loc.Lang = "en";
        Assert.Equal("yes", Loc.Pick("ja", "yes"));
    }
}
