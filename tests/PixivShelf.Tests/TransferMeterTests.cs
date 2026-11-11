using PixivShelf.Core.Services;
using Xunit;

namespace PixivShelf.Tests;

public class TransferMeterTests
{
    [Fact]
    public void Accumulates_bytes()
    {
        TransferMeter.Reset();
        TransferMeter.Add(100);
        TransferMeter.Add(50);
        Assert.Equal(150, TransferMeter.Read());
    }

    [Fact]
    public void Ignores_non_positive_adds()
    {
        TransferMeter.Reset();
        TransferMeter.Add(0);
        TransferMeter.Add(-5);
        Assert.Equal(0, TransferMeter.Read());
    }
}
