using Xunit;

namespace TaskbarStats.Tests;

/// <summary>Rooktest: de meetronde (Metrics.Update) blijft werkende waarden geven na het opsplitsen in deelmethoden.</summary>
public class MetricsSmokeTests
{
    [Fact]
    public void Update_geeft_geldige_waarden()
    {
        using var m = new Metrics();
        m.Update();
        System.Threading.Thread.Sleep(600);   // tellers hebben een tweede meting nodig
        m.Update();
        Assert.InRange(m.CpuPercent, 0, 100);
        Assert.InRange(m.MemPercent, 1, 100);   // er is altijd geheugen in gebruik
        Assert.True(m.MemTotalBytes > 0);
        Assert.True(m.NetDownBytesPerSec >= 0 && m.NetUpBytesPerSec >= 0);
        Assert.True(m.DiskReadBytesPerSec >= 0 && m.DiskWriteBytesPerSec >= 0);
    }
}
