using SadPSX.Core.Cpu;

namespace SadPSX.Core.Debugging;

/// <summary>
/// Converte instruções MIPS-I codificadas (32 bits) em texto assembly
/// legível, no estilo convencional (mnemônico + operandos, nomes de
/// registrador como $t0/$ra/etc.).
///
/// Cobre apenas as instruções que o <see cref="R3000A"/> já implementa.
/// Instruções desconhecidas produzem um texto de diagnóstico em vez de
/// lançar exceção — isso é intencional, já que o disassembler é uma
/// ferramenta de depuração e não deve travar ao encontrar algo inesperado.
/// </summary>
public static class Disassembler
{
    private static readonly string[] RegisterNames =
    {
        "zero", "at", "v0", "v1", "a0", "a1", "a2", "a3",
        "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7",
        "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7",
        "t8", "t9", "k0", "k1", "gp", "sp", "fp", "ra",
    };

    /// <summary>
    /// Desmonta uma instrução para texto assembly. <paramref name="pc"/> é
    /// usado apenas para calcular o endereço absoluto de branches e jumps
    /// no texto exibido (não afeta a decodificação em si).
    /// </summary>
    public static string Disassemble(Instruction instruction, uint pc = 0)
    {
        return instruction.Opcode switch
        {
            0x00 => DisassembleSpecial(instruction),
            0x01 => DisassembleRegImm(instruction, pc),
            0x02 => $"j        0x{JumpTarget(instruction, pc):X8}",
            0x03 => $"jal      0x{JumpTarget(instruction, pc):X8}",
            0x04 => Branch("beq", instruction, pc),
            0x05 => Branch("bne", instruction, pc),
            0x06 => BranchNoRt("blez", instruction, pc),
            0x07 => BranchNoRt("bgtz", instruction, pc),
            0x08 => ImmOp("addi", instruction),
            0x09 => ImmOp("addiu", instruction),
            0x0A => ImmOp("slti", instruction),
            0x0B => ImmOpUnsignedMnemonic("sltiu", instruction),
            0x0C => ImmOpUnsigned("andi", instruction),
            0x0D => ImmOpUnsigned("ori", instruction),
            0x0E => ImmOpUnsigned("xori", instruction),
            0x0F => $"lui      ${Reg(instruction.Rt)}, 0x{instruction.Immediate:X4}",
            0x10 => DisassembleCop0(instruction),
            0x12 => DisassembleCop2(instruction),
            0x20 => LoadStore("lb", instruction),
            0x21 => LoadStore("lh", instruction),
            0x22 => LoadStore("lwl", instruction),
            0x23 => LoadStore("lw", instruction),
            0x24 => LoadStore("lbu", instruction),
            0x25 => LoadStore("lhu", instruction),
            0x26 => LoadStore("lwr", instruction),
            0x28 => LoadStore("sb", instruction),
            0x29 => LoadStore("sh", instruction),
            0x2A => LoadStore("swl", instruction),
            0x2B => LoadStore("sw", instruction),
            0x2E => LoadStore("swr", instruction),
            0x32 => LoadStoreCop2("lwc2", instruction),
            0x3A => LoadStoreCop2("swc2", instruction),
            _ => Unknown(instruction),
        };
    }

    private static string DisassembleSpecial(Instruction instruction)
    {
        return instruction.Function switch
        {
            0x00 when instruction.Value == 0 => "nop",
            0x00 => $"sll      ${Reg(instruction.Rd)}, ${Reg(instruction.Rt)}, {instruction.ShiftAmount}",
            0x02 => $"srl      ${Reg(instruction.Rd)}, ${Reg(instruction.Rt)}, {instruction.ShiftAmount}",
            0x03 => $"sra      ${Reg(instruction.Rd)}, ${Reg(instruction.Rt)}, {instruction.ShiftAmount}",
            0x04 => $"sllv     ${Reg(instruction.Rd)}, ${Reg(instruction.Rt)}, ${Reg(instruction.Rs)}",
            0x06 => $"srlv     ${Reg(instruction.Rd)}, ${Reg(instruction.Rt)}, ${Reg(instruction.Rs)}",
            0x07 => $"srav     ${Reg(instruction.Rd)}, ${Reg(instruction.Rt)}, ${Reg(instruction.Rs)}",
            0x08 => $"jr       ${Reg(instruction.Rs)}",
            0x09 when instruction.Rd == 31 => $"jalr     ${Reg(instruction.Rs)}",
            0x09 => $"jalr     ${Reg(instruction.Rd)}, ${Reg(instruction.Rs)}",
            0x0C => "syscall",
            0x0D => DisassembleBreak(instruction),
            0x10 => $"mfhi     ${Reg(instruction.Rd)}",
            0x11 => $"mthi     ${Reg(instruction.Rs)}",
            0x12 => $"mflo     ${Reg(instruction.Rd)}",
            0x13 => $"mtlo     ${Reg(instruction.Rs)}",
            0x18 => $"mult     ${Reg(instruction.Rs)}, ${Reg(instruction.Rt)}",
            0x19 => $"multu    ${Reg(instruction.Rs)}, ${Reg(instruction.Rt)}",
            0x1A => $"div      ${Reg(instruction.Rs)}, ${Reg(instruction.Rt)}",
            0x1B => $"divu     ${Reg(instruction.Rs)}, ${Reg(instruction.Rt)}",
            0x20 => RegOp("add", instruction),
            0x21 => RegOp("addu", instruction),
            0x22 => RegOp("sub", instruction),
            0x23 => RegOp("subu", instruction),
            0x24 => RegOp("and", instruction),
            0x25 => RegOp("or", instruction),
            0x26 => RegOp("xor", instruction),
            0x27 => RegOp("nor", instruction),
            0x2A => RegOp("slt", instruction),
            0x2B => RegOp("sltu", instruction),
            _ => Unknown(instruction),
        };
    }

    private static string DisassembleRegImm(Instruction instruction, uint pc)
    {
        // O campo Rt diferencia BLTZ/BGEZ/BLTZAL/BGEZAL dentro do opcode REGIMM.
        string mnemonic = (instruction.Rt & 0x1F) switch
        {
            0x00 => "bltz",
            0x01 => "bgez",
            0x10 => "bltzal",
            0x11 => "bgezal",
            _ => null!,
        };

        if (mnemonic is null)
            return Unknown(instruction);

        uint target = unchecked(pc + 4 + ((uint)instruction.SignedImmediate << 2));
        return $"{mnemonic,-8} ${Reg(instruction.Rs)}, 0x{target:X8}";
    }

    private static string DisassembleCop0(Instruction instruction)
    {
        return instruction.Rs switch
        {
            0x00 => $"mfc0     ${Reg(instruction.Rt)}, cop0r{instruction.Rd}",
            0x04 => $"mtc0     ${Reg(instruction.Rt)}, cop0r{instruction.Rd}",
            0x10 when instruction.Function == 0x10 => "rfe",
            _ => Unknown(instruction),
        };
    }

    private static string DisassembleBreak(Instruction instruction)
    {
        uint code = (instruction.Value >> 6) & 0x000F_FFFF;
        return code == 0 ? "break" : $"break    0x{code:X5}";
    }

    private static string DisassembleCop2(Instruction instruction)
    {
        return instruction.Rs switch
        {
            0x00 => $"mfc2     ${Reg(instruction.Rt)}, cop2d{instruction.Rd}",
            0x02 => $"cfc2     ${Reg(instruction.Rt)}, cop2c{instruction.Rd}",
            0x04 => $"mtc2     ${Reg(instruction.Rt)}, cop2d{instruction.Rd}",
            0x06 => $"ctc2     ${Reg(instruction.Rt)}, cop2c{instruction.Rd}",
            >= 0x10 => $"gte      0x{instruction.Value & 0x01FF_FFFF:X7}",
            _ => Unknown(instruction),
        };
    }

    private static string RegOp(string mnemonic, Instruction i) =>
        $"{mnemonic,-8} ${Reg(i.Rd)}, ${Reg(i.Rs)}, ${Reg(i.Rt)}";

    private static string ImmOp(string mnemonic, Instruction i) =>
        $"{mnemonic,-8} ${Reg(i.Rt)}, ${Reg(i.Rs)}, {i.SignedImmediate}";

    // ANDI/ORI/XORI convencionalmente mostram o imediato em hexadecimal
    // (são operações lógicas, não aritméticas — não faz sentido de sinal).
    private static string ImmOpUnsigned(string mnemonic, Instruction i) =>
        $"{mnemonic,-8} ${Reg(i.Rt)}, ${Reg(i.Rs)}, 0x{i.Immediate:X4}";

    // SLTIU também trata o imediato como valor sign-extended internamente,
    // mas por convenção de disassembly costuma ser exibido como o SLTI:
    // o valor "como foi escrito" no código-fonte original (decimal, com
    // sinal), já que é isso que um assembler normalmente aceitaria de volta.
    private static string ImmOpUnsignedMnemonic(string mnemonic, Instruction i) =>
        $"{mnemonic,-8} ${Reg(i.Rt)}, ${Reg(i.Rs)}, {i.SignedImmediate}";

    private static string LoadStore(string mnemonic, Instruction i) =>
        $"{mnemonic,-8} ${Reg(i.Rt)}, {i.SignedImmediate}(${Reg(i.Rs)})";

    private static string LoadStoreCop2(string mnemonic, Instruction i) =>
        $"{mnemonic,-8} cop2d{i.Rt}, {i.SignedImmediate}(${Reg(i.Rs)})";

    private static string Branch(string mnemonic, Instruction i, uint pc)
    {
        uint target = unchecked(pc + 4 + ((uint)i.SignedImmediate << 2));
        return $"{mnemonic,-8} ${Reg(i.Rs)}, ${Reg(i.Rt)}, 0x{target:X8}";
    }

    private static string BranchNoRt(string mnemonic, Instruction i, uint pc)
    {
        uint target = unchecked(pc + 4 + ((uint)i.SignedImmediate << 2));
        return $"{mnemonic,-8} ${Reg(i.Rs)}, 0x{target:X8}";
    }

    private static uint JumpTarget(Instruction i, uint pc) =>
        ((pc + 4) & 0xF000_0000) | (i.JumpTarget << 2);

    private static string Unknown(Instruction i) =>
        $".word    0x{i.Value:X8}  ; instrução desconhecida ou não implementada";

    private static string Reg(int index) => RegisterNames[index];
}
