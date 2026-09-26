using System.Runtime.CompilerServices;

// GdiCache is niet thread-veilig; de tests draaien daarom na elkaar (niet parallel) en zonder de UI-thread-controle.

namespace TaskbarStats.Tests;

internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Init() => GdiCache.ThreadCheck = false;
}
