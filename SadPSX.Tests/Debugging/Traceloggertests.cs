using SadPSX.Core;
using SadPSX.Core.Bios;
using SadPSX.Core.Cpu;
using SadPSX.Core.Debugging;
using Xunit;

namespace SadPSX.Tests.Debugging;

public sealed class TraceLoggerTests
{
    private static byte[] CreateBiosImageWithInstructions(params uint[] instructions)
    {
        var image = new byte[BiosRom.SizeInBytes];

        for (int i = 0; i < instructions.Length; i++)
        {
            int offset = i * 4;
            uint word = instructions[i];

            image[offset] = (byte)word;
            image[offset + 1] = (byte)(word >> 8);
            image[offset + 2] = (byte)(word >> 16);
            image[offset + 3] = (byte)(word >> 24);
        }

        return image;
    }

    [Fact]
    public void StepRecordsOneEntryAndStillExecutesTheInstruction()
    {
        var machine = new PsxMachine();
        machine.LoadBios(CreateBiosImageWithInstructions(0x2408_0001)); // addiu $t0, zero, 1

        var tracer = new TraceLogger(machine);
        tracer.Step();

        Assert.Single(tracer.Entries);
        Assert.Equal(1u, machine.Cpu.GetRegister(8));
    }

    [Fact]
    public void EntryContainsCorrectPcRawInstructionAndDisassembly()
    {
        var machine = new PsxMachine();
        machine.LoadBios(CreateBiosImageWithInstructions(0x2408_0001)); // addiu $t0, zero, 1

        var tracer = new TraceLogger(machine);
        tracer.Step();

        var entry = tracer.Entries[0];
        Assert.Equal(0xBFC0_0000u, entry.Pc);
        Assert.Equal(0x2408_0001u, entry.RawInstruction);
        Assert.Equal("addiu    $t0, $zero, 1", entry.Disassembly);
    }

    [Fact]
    public void RunTracesMultipleInstructionsInOrder()
    {
        var machine = new PsxMachine();
        machine.LoadBios(CreateBiosImageWithInstructions(
            0x2408_0001, // addiu $t0, zero, 1
            0x2408_0002, // addiu $t0, zero, 2
            0x2408_0003  // addiu $t0, zero, 3
        ));

        var tracer = new TraceLogger(machine);
        tracer.Run(3);

        Assert.Equal(3, tracer.Entries.Count);
        Assert.Equal(0xBFC0_0000u, tracer.Entries[0].Pc);
        Assert.Equal(0xBFC0_0004u, tracer.Entries[1].Pc);
        Assert.Equal(0xBFC0_0008u, tracer.Entries[2].Pc);
        Assert.Equal(3u, machine.Cpu.GetRegister(8));
    }

    [Fact]
    public void MaxEntriesDiscardsOldestEntriesLikeACircularBuffer()
    {
        var machine = new PsxMachine();
        machine.LoadBios(CreateBiosImageWithInstructions(
            0x2408_0001, 0x2408_0002, 0x2408_0003, 0x2408_0063 // 4 instruções
        ));

        var tracer = new TraceLogger(machine) { MaxEntries = 2 };
        tracer.Run(4);

        // Só as 2 mais recentes devem sobrar.
        Assert.Equal(2, tracer.Entries.Count);
        Assert.Equal(0xBFC0_0008u, tracer.Entries[0].Pc);
        Assert.Equal(0xBFC0_000Cu, tracer.Entries[1].Pc);
    }

    [Fact]
    public void RunUntilStopsAsSoonAsConditionBecomesTrueAndTracesOnlyThoseSteps()
    {
        var machine = new PsxMachine();
        machine.LoadBios(CreateBiosImageWithInstructions(
            0x2408_0001, // addiu $t0, zero, 1
            0x2408_0063, // addiu $t0, zero, 99 <- parar aqui
            0x2408_0002  // não deveria rodar nem ser traceada
        ));

        var tracer = new TraceLogger(machine);
        bool reached = tracer.RunUntil(m => m.Cpu.GetRegister(8) == 99, maxSteps: 10);

        Assert.True(reached);
        Assert.Equal(2, tracer.Entries.Count);
        Assert.Equal(99u, machine.Cpu.GetRegister(8));
    }

    [Fact]
    public void ClearRemovesAllAccumulatedEntries()
    {
        var machine = new PsxMachine();
        machine.LoadBios(CreateBiosImageWithInstructions(0x2408_0001, 0x2408_0002));

        var tracer = new TraceLogger(machine);
        tracer.Run(2);
        Assert.Equal(2, tracer.Entries.Count);

        tracer.Clear();
        Assert.Empty(tracer.Entries);
    }

    [Fact]
    public void OutputWriterReceivesOneLinePerStep()
    {
        var machine = new PsxMachine();
        machine.LoadBios(CreateBiosImageWithInstructions(0x2408_0001, 0x2408_0002));

        using var writer = new StringWriter();
        var tracer = new TraceLogger(machine) { Output = writer };
        tracer.Run(2);

        string output = writer.ToString();
        int lineCount = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        Assert.Equal(2, lineCount);
        Assert.Contains("addiu", output);
    }

    [Fact]
    public void UnmappedFetchIsTracedAndHandledByTheCpu()
    {
        var machine = new PsxMachine();
        machine.Cpu.Reset(0xE000_0000);
        var tracer = new TraceLogger(machine);

        tracer.Step();

        Assert.Single(tracer.Entries);
        Assert.Contains("barramento", tracer.Entries[0].Disassembly);
        Assert.Equal(
            (uint)ExceptionCode.InstructionBusError,
            (machine.Cpu.Cop0.Cause >> 2) & 0x1F);
    }

    [Fact]
    public void UnalignedFetchIsTracedAndHandledByTheCpu()
    {
        var machine = new PsxMachine();
        machine.Cpu.Reset(0x0000_0002);
        var tracer = new TraceLogger(machine);

        tracer.Step();

        Assert.Single(tracer.Entries);
        Assert.Contains("alinhamento", tracer.Entries[0].Disassembly);
        Assert.Equal(
            (uint)ExceptionCode.AddressErrorLoad,
            (machine.Cpu.Cop0.Cause >> 2) & 0x1F);
    }
}
