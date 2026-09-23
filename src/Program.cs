namespace TaskbarStats;

internal static class Program
{
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
        using var mutex = new Mutex(true, "TaskbarStats_SingleInstance" + Environment.GetEnvironmentVariable("TASKBARSTATS_INSTANCE"), out bool created);
        if (!created) return 0;

        ApplicationConfiguration.Initialize();
        var settings = AppSettings.Load();
        Application.Run(new WidgetForm(settings));
        return 0;
    }
}
