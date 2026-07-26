using SadPSX.Core.Bios;
using SadPSX.Core.Controllers;
using SadPSX.Core.Debugging;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Tests.Controllers;

public sealed class PostStatusRegisterTests
{
    [Fact]
    public void BiosPostWritesAreHandledAndObservable()
    {
        var bus = new Bus();
        byte? observedValue = null;
        bus.PostStatus.ValueChanged += value => observedValue = value;

        bus.Write8(PostStatusRegister.Address, 0x0D);

        Assert.Equal(0x0D, bus.PostStatus.Value);
        Assert.Equal(1ul, bus.PostStatus.WriteCount);
        Assert.Equal((byte)0x0D, observedValue);
        Assert.DoesNotContain(
            bus.Mmio.AccessSummaries,
            summary => !summary.Handled);
    }

    [Fact]
    public void RuntimeDiagnosticsIncludePostAndSioState()
    {
        var machine = new SadPSX.Core.PsxMachine();
        machine.Bus.Write8(PostStatusRegister.Address, 0x02);
        machine.Bus.Write16(Sio0.ControlAddress, 0x0003);
        using var diagnostics = new RuntimeDiagnostics(machine);

        RuntimeDiagnosticSnapshot snapshot = diagnostics.Capture();

        Assert.Equal(0x02, snapshot.PostStatus);
        Assert.Equal(1ul, snapshot.PostWriteCount);
        Assert.Equal(0x0003, snapshot.Sio0Control);
    }
}
