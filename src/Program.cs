namespace TaskbarStats;

internal static class Program
{
    /// <summary>Wordt afgevuurd (op een achtergrondthread) als een tweede start van de app vraagt om zichtbaar te worden.</summary>
    internal static event Action? ShowRequested;

    [STAThread]
    private static int Main(string[] args)
    {
        // Gebruikt door de installer/uninstaller (draait als administrator):
        //   TaskbarStats.exe --autostart-on | --autostart-off
        if (args.Length > 0)
        {
            switch (args[0].ToLowerInvariant())
            {
                case "--autostart-on": StartupManager.Set(true); return 0;
                case "--autostart-off": StartupManager.Set(false); return 0;
            }
        }

        // Slechts één instantie tegelijk (voorkomt dubbel starten bij autostart + handmatig).
        // TASKBARSTATS_INSTANCE = achtervoegsel voor de mutex, zodat een testkopie naast de echte app kan draaien.
        string suffix = Environment.GetEnvironmentVariable("TASKBARSTATS_INSTANCE") ?? "";
        // Een benoemde gebeurtenis (geen venstersbericht: het widget is eigendom van de taakbalk en krijgt geen broadcasts).
        string showName = "TaskbarStats_Show" + suffix;
        using var mutex = new Mutex(true, "TaskbarStats_SingleInstance" + suffix, out bool created);
        if (!created)
        {
            if (args.Contains("--restarted"))
            {
                // na een crash: even wachten tot het vorige proces echt weg is
                try { if (!mutex.WaitOne(8000)) return 0; } catch (AbandonedMutexException) { }
            }
            else
            {
                try { EventWaitHandle.OpenExisting(showName).Set(); } catch (Exception dex) { Diag.Swallow(dex); }   // de draaiende app opent zijn instellingen: "hij draait al"
                return 0;
            }
        }
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showName);
        new Thread(() => { while (showEvent.WaitOne()) ShowRequested?.Invoke(); }) { IsBackground = true, Name = "show-request" }.Start();

        Diag.Init();
        ApplicationConfiguration.Initialize();
        var settings = AppSettings.Load();
        Application.Run(new WidgetForm(settings));
        return 0;
    }
}
