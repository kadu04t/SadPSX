using SadPSX.Core.Cpu;
using SystemBus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Core;

/// <summary>
/// Orquestra os componentes de hardware do PlayStation 1 (CPU, barramento
/// de memória, BIOS) e expõe uma API de alto nível para carregar uma BIOS,
/// executar ciclos de CPU, e inspecionar/redefinir o estado da máquina.
///
/// Esta classe não implementa nenhum periférico por si só — isso continua
/// sendo responsabilidade do <see cref="Bus"/> e das classes de memória que
/// ele agrega (Ram, Scratchpad, BiosRom, Mmio). PsxMachine apenas mantém
/// essas peças juntas e fornece o ponto de entrada natural para rodar o
/// emulador.
/// </summary>
public sealed class PsxMachine
{
    private readonly List<IClockedDevice> _clockedDevices = new();

    public SystemBus Bus { get; }
    public R3000A Cpu { get; }
    public ulong ClockCycles => Cpu.ClockCycles;

    public PsxMachine()
    {
        Bus = new SystemBus();
        Cpu = new R3000A(Bus);
        RegisterClockedDevice(Bus.VideoTiming);
        RegisterClockedDevice(Bus.RootCounters);
        RegisterClockedDevice(Bus.Spu);
    }

    public PsxMachine(SystemBus bus)
    {
        Bus = bus ?? throw new ArgumentNullException(nameof(bus));
        Cpu = new R3000A(Bus);
        RegisterClockedDevice(Bus.VideoTiming);
        RegisterClockedDevice(Bus.RootCounters);
        RegisterClockedDevice(Bus.Spu);
    }

    /// <summary>
    /// Carrega uma imagem de BIOS (512 KiB) no barramento e reinicia a CPU
    /// para o vetor de reset padrão (0xBFC0_0000), de onde a execução da
    /// BIOS real começaria no hardware físico.
    /// </summary>
    public void LoadBios(byte[] biosImage)
    {
        Bus.Bios.Load(biosImage);
        Reset();
    }

    /// <summary>
    /// Carrega uma imagem de BIOS a partir de um arquivo no disco e reinicia
    /// a CPU para o vetor de reset padrão.
    /// </summary>
    public void LoadBios(string biosFilePath)
    {
        byte[] image = File.ReadAllBytes(biosFilePath);
        LoadBios(image);
    }

    /// <summary>
    /// Executa uma única instrução, incluindo busca e execução (equivalente a
    /// <see cref="R3000A.Step"/>).
    /// </summary>
    public void Step()
    {
        Cpu.Step();

        foreach (IClockedDevice device in _clockedDevices)
            device.Tick(Cpu.LastStepCycles);
    }

    public void RegisterClockedDevice(IClockedDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!_clockedDevices.Contains(device))
            _clockedDevices.Add(device);
    }

    /// <summary>
    /// Executa exatamente <paramref name="stepCount"/> instruções da CPU em
    /// sequência. Útil para testes e depuração, onde rodar indefinidamente
    /// não é prático.
    /// </summary>
    public void Run(ulong stepCount)
    {
        for (ulong i = 0; i < stepCount; i++)
            Step();
    }

    /// <summary>
    /// Executa instruções até que <paramref name="stopCondition"/> retorne
    /// verdadeiro (avaliado após cada Step), ou até
    /// <paramref name="maxSteps"/> ciclos serem executados — o que ocorrer
    /// primeiro. Retorna verdadeiro se a condição de parada foi atingida, e
    /// falso se o limite de passos foi alcançado sem que ela se tornasse
    /// verdadeira (útil para detectar loops infinitos em testes).
    /// </summary>
    public bool RunUntil(Func<PsxMachine, bool> stopCondition, ulong maxSteps)
    {
        ArgumentNullException.ThrowIfNull(stopCondition);

        for (ulong i = 0; i < maxSteps; i++)
        {
            Step();

            if (stopCondition(this))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reinicia a CPU para o vetor de reset padrão (0xBFC0_0000). O conteúdo
    /// da RAM, scratchpad e BIOS não é apagado — apenas o estado da CPU
    /// (registradores, Pc/NextPc, Hi/Lo, COP0 e contadores) volta ao
    /// estado inicial, replicando o comportamento de um reset de hardware.
    /// </summary>
    public void Reset()
    {
        Bus.InterruptController.Reset();
        Bus.Dma.Reset();
        Bus.RootCounters.Reset();
        Bus.Spu.Reset();
        Bus.Gpu.Reset();
        Bus.VideoTiming.Reset();
        Cpu.Reset();
    }
}
