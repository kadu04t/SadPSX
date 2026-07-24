using SadPSX.Core.Cpu;
using SadPSX.Core.Memory;
using Xunit;

namespace SadPSX.Tests.Cpu;

public sealed class BranchTests
{
    private const uint TestProgramAddress = 0x0000_0000;

    [Fact]
    public void BeqTakenExecutesDelaySlotBeforeJumping()
    {
        var (bus, cpu) = CreateCpu();

        /*
         * addiu $t0, $zero, 1     ; 0x00
         * addiu $t1, $zero, 1     ; 0x04
         * beq   $t0, $t1, target  ; 0x08  (tomado: 1 == 1)
         * addiu $v0, $zero, 99    ; 0x0C  delay slot -> SEMPRE executa
         * addiu $v0, $zero, 1     ; 0x10  NÃO deve executar (pulada pelo branch)
         * target:
         * addiu $v1, $zero, 42    ; 0x14
         */
        bus.Write32(0x00, 0x2408_0001); // addiu $t0, $zero, 1
        bus.Write32(0x04, 0x2409_0001); // addiu $t1, $zero, 1
        bus.Write32(0x08, 0x1109_0002); // beq $t0, $t1, offset=2 -> target=0x0C+8=0x14
        bus.Write32(0x0C, 0x2402_0063); // addiu $v0, $zero, 99  (delay slot)
        bus.Write32(0x10, 0x2402_0001); // addiu $v0, $zero, 1   (pulada)
        bus.Write32(0x14, 0x2403_002A); // addiu $v1, $zero, 42  (target)

        cpu.Step(); // addiu t0
        cpu.Step(); // addiu t1
        cpu.Step(); // beq (agenda o branch)
        cpu.Step(); // delay slot: v0 = 99

        Assert.Equal(99u, cpu.GetRegister(2));

        cpu.Step(); // deve executar o alvo (0x14), pulando 0x10

        Assert.Equal(42u, cpu.GetRegister(3));
        Assert.Equal(99u, cpu.GetRegister(2)); // não foi sobrescrito por 0x10
    }

    [Fact]
    public void BneNotTakenFallsThroughNormally()
    {
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x2408_0005); // addiu $t0, $zero, 5
        bus.Write32(0x04, 0x2409_0005); // addiu $t1, $zero, 5
        bus.Write32(0x08, 0x1509_000A); // bne $t0, $t1, offset=10 (NÃO tomado: iguais)
        bus.Write32(0x0C, 0x2402_0007); // addiu $v0, $zero, 7  (delay slot, sempre roda)
        bus.Write32(0x10, 0x2403_0008); // addiu $v1, $zero, 8  (deve rodar: fall-through)

        for (int i = 0; i < 5; i++)
            cpu.Step();

        Assert.Equal(7u, cpu.GetRegister(2));
        Assert.Equal(8u, cpu.GetRegister(3));
    }

    [Fact]
    public void JalSetsReturnAddressAfterDelaySlotAndJumpsToTarget()
    {
        var (bus, cpu) = CreateCpu();

        /*
         * jal   target            ; 0x00
         * addiu $v0, $zero, 1     ; 0x04  delay slot -> sempre executa
         * addiu $v1, $zero, 2     ; 0x08  NÃO deve executar
         * addiu $a0, $zero, 3     ; 0x0C  NÃO deve executar
         * target:
         * addiu $a1, $zero, 99    ; 0x10
         */
        bus.Write32(0x00, 0x0C00_0004); // jal 0x10 (target = 0x10 >> 2 = 4)
        bus.Write32(0x04, 0x2402_0001); // addiu $v0, $zero, 1  (delay slot)
        bus.Write32(0x08, 0x2403_0002); // addiu $v1, $zero, 2  (pulada)
        bus.Write32(0x0C, 0x2404_0003); // addiu $a0, $zero, 3  (pulada)
        bus.Write32(0x10, 0x2405_0063); // addiu $a1, $zero, 99 (target)

        cpu.Step(); // jal
        cpu.Step(); // delay slot: v0 = 1

        // $ra (registrador 31) deve apontar para a instrução após o delay
        // slot: 0x04 + 4 = 0x08.
        Assert.Equal(0x08u, cpu.GetRegister(31));
        Assert.Equal(1u, cpu.GetRegister(2));

        cpu.Step(); // deve pular para 0x10, não executar 0x08

        Assert.Equal(99u, cpu.GetRegister(5));
        Assert.Equal(0u, cpu.GetRegister(3)); // 0x08 nunca rodou
    }

    [Fact]
    public void JrReturnsToAddressSavedByJal()
    {
        var (bus, cpu) = CreateCpu();

        /*
         * jal   function           ; 0x00
         * addiu $t0, $zero, 111    ; 0x04  delay slot
         * addiu $t1, $zero, 222    ; 0x08  deve rodar após o retorno
         * (nada)                   ; 0x0C
         * function:
         * jr    $ra                ; 0x10
         * addiu $t2, $zero, 333    ; 0x14  delay slot do jr
         */
        bus.Write32(0x00, 0x0C00_0004); // jal 0x10
        bus.Write32(0x04, 0x2408_006F); // addiu $t0, $zero, 111
        bus.Write32(0x08, 0x2409_00DE); // addiu $t1, $zero, 222
        bus.Write32(0x0C, 0x0000_0000); // nop
        bus.Write32(0x10, 0x03E0_0008); // jr $ra
        bus.Write32(0x14, 0x240A_014D); // addiu $t2, $zero, 333

        cpu.Step(); // jal
        cpu.Step(); // delay slot do jal: t0 = 111
        Assert.Equal(0x08u, cpu.GetRegister(31));

        cpu.Step(); // executa em 0x10: jr $ra (agenda retorno para 0x08)
        cpu.Step(); // delay slot do jr (0x14): t2 = 333
        Assert.Equal(333u, cpu.GetRegister(10));

        cpu.Step(); // deve executar a instrução em 0x08 (retorno)
        Assert.Equal(222u, cpu.GetRegister(9));
    }

    [Fact]
    public void BltzalSetsReturnAddressEvenWhenBranchNotTaken()
    {
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x2408_0005); // addiu $t0, $zero, 5 (positivo)
        bus.Write32(0x04, 0x0510_0064); // bltzal $t0, offset=100 (não tomado: 5 >= 0)
        bus.Write32(0x08, 0x2402_0009); // addiu $v0, $zero, 9  (delay slot, sempre roda)
        bus.Write32(0x0C, 0x2403_000A); // addiu $v1, $zero, 10 (deve rodar: fall-through)

        cpu.Step(); // addiu t0
        cpu.Step(); // bltzal (não tomado, mas $ra é setado mesmo assim)

        Assert.Equal(0x0Cu, cpu.GetRegister(31));

        cpu.Step(); // delay slot: v0 = 9
        cpu.Step(); // fall-through normal: v1 = 10

        Assert.Equal(10u, cpu.GetRegister(3));
    }

    [Fact]
    public void JUnconditionalJumpAlwaysTaken()
    {
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x0800_0004); // j 0x10
        bus.Write32(0x04, 0x2402_0005); // addiu $v0, $zero, 5  (delay slot)
        bus.Write32(0x08, 0x2403_0006); // addiu $v1, $zero, 6  (pulada)
        bus.Write32(0x0C, 0x0000_0000); // nop
        bus.Write32(0x10, 0x2404_0058); // addiu $a0, $zero, 88 (target)

        cpu.Step(); // j
        cpu.Step(); // delay slot: v0 = 5
        Assert.Equal(5u, cpu.GetRegister(2));

        cpu.Step(); // deve pular para 0x10
        Assert.Equal(88u, cpu.GetRegister(4));
        Assert.Equal(0u, cpu.GetRegister(3)); // 0x08 nunca rodou
    }

    [Fact]
    public void BlezTakenWhenRegisterIsZero()
    {
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x2408_0000); // addiu $t0, $zero, 0
        bus.Write32(0x04, 0x1900_0002); // blez $t0, offset=2 -> target=0x08+8=0x10
        bus.Write32(0x08, 0x2402_0001); // addiu $v0, $zero, 1  (delay slot)
        bus.Write32(0x0C, 0x2403_0001); // addiu $v1, $zero, 1  (pulada)
        bus.Write32(0x10, 0x2404_0001); // addiu $a0, $zero, 1  (target)

        for (int i = 0; i < 4; i++)
            cpu.Step();

        Assert.Equal(1u, cpu.GetRegister(4));
        Assert.Equal(0u, cpu.GetRegister(3));
    }

    [Fact]
    public void BeqNotTakenFallsThroughAndPcAdvancesNormally()
    {
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x2408_0001); // addiu $t0, $zero, 1
        bus.Write32(0x04, 0x2409_0002); // addiu $t1, $zero, 2
        bus.Write32(0x08, 0x1109_000A); // beq $t0, $t1, offset=10 (NÃO tomado: 1 != 2)
        bus.Write32(0x0C, 0x2402_0007); // addiu $v0, $zero, 7 (delay slot, sempre roda)
        bus.Write32(0x10, 0x2403_0008); // addiu $v1, $zero, 8 (deve rodar: fall-through)

        for (int i = 0; i < 5; i++)
            cpu.Step();

        Assert.Equal(7u, cpu.GetRegister(2));
        Assert.Equal(8u, cpu.GetRegister(3));
        Assert.Equal(0x14u, cpu.Pc);
        Assert.Equal(0x18u, cpu.NextPc);
    }

    [Fact]
    public void BneTakenSkipsInstructionAfterDelaySlot()
    {
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x2408_0001); // addiu $t0, $zero, 1
        bus.Write32(0x04, 0x2409_0002); // addiu $t1, $zero, 2
        bus.Write32(0x08, 0x1509_0002); // bne $t0, $t1, offset=2 (tomado: 1 != 2) -> target=0x0C+8=0x14
        bus.Write32(0x0C, 0x2402_0007); // addiu $v0, $zero, 7 (delay slot)
        bus.Write32(0x10, 0x2403_0008); // addiu $v1, $zero, 8 (pulada)
        bus.Write32(0x14, 0x2404_0037); // addiu $a0, $zero, 55 (target)

        for (int i = 0; i < 4; i++)
            cpu.Step();

        Assert.Equal(0x14u, cpu.Pc);

        cpu.Step();

        Assert.Equal(55u, cpu.GetRegister(4));
        Assert.Equal(0u, cpu.GetRegister(3));
    }

    [Fact]
    public void BgtzTakenWhenRegisterIsPositive()
    {
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x2408_0005); // addiu $t0, $zero, 5
        bus.Write32(0x04, 0x1D00_0002); // bgtz $t0, offset=2 (tomado: 5 > 0)
        bus.Write32(0x08, 0x2402_0001); // addiu $v0, $zero, 1 (delay slot)
        bus.Write32(0x0C, 0x2403_0001); // addiu $v1, $zero, 1 (pulada)
        bus.Write32(0x10, 0x2404_004D); // addiu $a0, $zero, 77 (target)

        for (int i = 0; i < 4; i++)
            cpu.Step();

        Assert.Equal(77u, cpu.GetRegister(4));
        Assert.Equal(0u, cpu.GetRegister(3));
    }

    [Fact]
    public void BgtzNotTakenWhenRegisterIsZero()
    {
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x2408_0000); // addiu $t0, $zero, 0
        bus.Write32(0x04, 0x1D00_000A); // bgtz $t0, offset=10 (NÃO tomado: 0 não é > 0)
        bus.Write32(0x08, 0x2402_0001); // addiu $v0, $zero, 1 (delay slot)
        bus.Write32(0x0C, 0x2403_0002); // addiu $v1, $zero, 2 (deve rodar: fall-through)

        for (int i = 0; i < 4; i++)
            cpu.Step();

        Assert.Equal(2u, cpu.GetRegister(3));
    }

    [Fact]
    public void BgezTakenWhenRegisterIsZero()
    {
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x2408_0000); // addiu $t0, $zero, 0
        bus.Write32(0x04, 0x0501_0002); // bgez $t0, offset=2 (tomado: 0 >= 0)
        bus.Write32(0x08, 0x2402_0001); // addiu $v0, $zero, 1 (delay slot)
        bus.Write32(0x0C, 0x2403_0001); // addiu $v1, $zero, 1 (pulada)
        bus.Write32(0x10, 0x2404_0058); // addiu $a0, $zero, 88 (target)

        for (int i = 0; i < 4; i++)
            cpu.Step();

        Assert.Equal(88u, cpu.GetRegister(4));
        Assert.Equal(0u, cpu.GetRegister(3));
    }

    [Fact]
    public void JrJumpsToAddressInRegisterWithoutPriorJal()
    {
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x2408_0010); // addiu $t0, $zero, 0x10
        bus.Write32(0x04, 0x0100_0008); // jr $t0
        bus.Write32(0x08, 0x2402_0001); // addiu $v0, $zero, 1 (delay slot)
        bus.Write32(0x0C, 0x2403_0002); // addiu $v1, $zero, 2 (pulada)
        bus.Write32(0x10, 0x2404_0063); // addiu $a0, $zero, 99 (target)

        for (int i = 0; i < 3; i++)
            cpu.Step();

        Assert.Equal(1u, cpu.GetRegister(2));

        cpu.Step();

        Assert.Equal(99u, cpu.GetRegister(4));
        Assert.Equal(0u, cpu.GetRegister(3));
    }

    [Fact]
    public void JalrReadsTargetBeforeOverwritingWhenRdEqualsRs()
    {
        // Caso de borda crítico: jalr $t0, $t0 (rd == rs). O endereço alvo
        // precisa ser lido ANTES do link register ser escrito, senão a CPU
        // pularia para o próprio endereço de retorno em vez do alvo original.
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x2408_0010); // addiu $t0, $zero, 0x10 (alvo original)
        bus.Write32(0x04, 0x0100_4009); // jalr $t0, $t0 (rd = rs = $t0)
        bus.Write32(0x08, 0x2402_0001); // addiu $v0, $zero, 1 (delay slot)
        bus.Write32(0x0C, 0x2403_0002); // addiu $v1, $zero, 2 (pulada)
        bus.Write32(0x10, 0x2404_0063); // addiu $a0, $zero, 99 (target)

        cpu.Step(); // addiu $t0, $zero, 0x10
        cpu.Step(); // jalr $t0, $t0

        // $t0 (rd) agora deve conter o endereço de retorno (0x08 + 4 = 0x0C),
        // não mais 0x10 — mas o desvio deve ir para o alvo ORIGINAL (0x10).
        Assert.Equal(0x0Cu, cpu.GetRegister(8));

        cpu.Step(); // delay slot: v0 = 1
        Assert.Equal(1u, cpu.GetRegister(2));

        cpu.Step(); // deve pular para 0x10 (alvo original, lido antes do overwrite)

        Assert.Equal(99u, cpu.GetRegister(4));
        Assert.Equal(0u, cpu.GetRegister(3)); // 0x0C não deveria ter rodado
    }

    [Fact]
    public void BackwardBranchWithNegativeOffsetImplementsLoop()
    {
        /*
         * Loop clássico com offset NEGATIVO, decrementando um contador:
         *
         * addiu $t0, $zero, 3      ; 0x00: contador = 3
         * loop:
         * addiu $t0, $t0, -1       ; 0x04: contador--
         * bne   $t0, $zero, loop   ; 0x08: volta se != 0 (offset negativo)
         * addiu $v0, $zero, 1      ; 0x0C: delay slot (roda toda iteração)
         * addiu $v1, $zero, 99     ; 0x10: só roda quando o loop termina
         */
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x2408_0003); // addiu $t0, $zero, 3
        bus.Write32(0x04, 0x2508_FFFF); // addiu $t0, $t0, -1     (loop:)
        bus.Write32(0x08, 0x1500_FFFE); // bne $t0, $zero, offset=-2 -> volta para 0x04
        bus.Write32(0x0C, 0x2402_0001); // addiu $v0, $zero, 1    (delay slot, toda iteração)
        bus.Write32(0x10, 0x2403_0063); // addiu $v1, $zero, 99   (só ao sair do loop)

        cpu.Step(); // addiu $t0, $zero, 3

        // Iteração 1: t0 = 3 -> 2, bne tomado (volta para 0x04)
        cpu.Step(); // addiu $t0, $t0, -1 -> t0 = 2
        cpu.Step(); // bne (tomado, agenda volta para 0x04)
        cpu.Step(); // delay slot: v0 = 1; branch aplica -> Pc volta para 0x04

        Assert.Equal(0x04u, cpu.Pc);
        Assert.Equal(2u, cpu.GetRegister(8));

        // Iteração 2: t0 = 2 -> 1, bne tomado de novo
        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(0x04u, cpu.Pc);
        Assert.Equal(1u, cpu.GetRegister(8));

        // Iteração 3: t0 = 1 -> 0, bne NÃO tomado (0 == 0), sai do loop
        cpu.Step(); // addiu $t0, $t0, -1 -> t0 = 0
        cpu.Step(); // bne (NÃO tomado)
        cpu.Step(); // delay slot: v0 = 1 (roda mesmo assim), fall-through normal

        Assert.Equal(0u, cpu.GetRegister(8));
        Assert.Equal(0x10u, cpu.Pc);

        cpu.Step(); // executa 0x10: v1 = 99

        Assert.Equal(99u, cpu.GetRegister(3));
    }

    private static (Bus Bus, R3000A Cpu) CreateCpu()
    {
        var ram = new Ram();
        var bus = new Bus(ram);
        var cpu = new R3000A(bus);

        cpu.Reset(TestProgramAddress);

        return (bus, cpu);
    }
}