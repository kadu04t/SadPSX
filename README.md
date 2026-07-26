# SadPSX

SadPSX é um emulador experimental de PlayStation 1 escrito em C# e .NET.

O projeto ainda está em desenvolvimento inicial. O foco atual é construir uma
base correta para a CPU MIPS R3000A, o COP0 e o mapa de memória antes de
implementar os demais componentes do console.

## Estado atual

O SadPSX já consegue:

- Carregar uma imagem de BIOS de 512 KiB.
- Iniciar a CPU no vetor de reset `0xBFC00000`.
- Executar instruções reais da BIOS.
- Traduzir endereços entre KUSEG, KSEG0, KSEG1 e KSEG2.
- Acessar RAM, seus espelhos, scratchpad e BIOS.
- Tratar Expansion Region 1 como barramento flutuante.
- Executar branch e jump delay slots.
- Aplicar load delay em loads e `MFC0`.
- Executar `LWL`, `LWR`, `SWL` e `SWR`.
- Processar exceções através do COP0.
- Entregar interrupções mascaradas ao COP0.
- Executar os três root counters e suas IRQs.
- Responder aos handshakes básicos da GPU e da SPU.
- Executar DMA2 para a GPU e DMA6 para tabelas OTC.
- Gerar dotclock, HBlank, VBlank e IRQ0 em modos NTSC/PAL.
- Detectar overflow, acessos desalinhados e erros de barramento.
- Bloquear acessos de usuário aos segmentos do kernel.
- Contabilizar custos aproximados de acesso à memória.
- Produzir traces com endereço, instrução crua e disassembly.
- Validar uma execução da BIOS com métricas e critérios reproduzíveis.

Na validação atual, a BIOS SCPH-1001 executa pelo menos 20.000.000 de
instruções sem exceções inesperadas, usa DMA2/DMA6, recebe interrupções de
VBlank e alcança 7.474 PCs únicos. Dos 64.754 acessos MMIO, somente 15 ainda
não são tratados, todos na Expansion Region 2. Isso ainda não representa um
boot visual completo.

## Componentes implementados

### CPU

A implementação interpretada do R3000A inclui:

- Operações aritméticas com e sem overflow.
- Operações lógicas e comparações.
- Shifts imediatos e variáveis.
- Multiplicação e divisão com comportamento especial do hardware.
- Branches condicionais.
- `J`, `JAL`, `JR` e `JALR`.
- Loads e stores, incluindo acessos desalinhados com `LWL/LWR/SWL/SWR`.
- Branch delay e load delay.
- Registradores `HI`, `LO` e `$zero`.

As principais instruções ainda não implementadas são as operações do GTE/COP2.

### COP0

O COP0 atualmente possui:

- Registradores de controle, debug e identificação do processador.
- Exceções de syscall, breakpoint e overflow.
- Exceções de endereço e barramento.
- Exceção de instrução reservada.
- Identificação de exceções em branch delay slots.
- Seleção dos vetores de exceção por `SR.BEV`.
- Implementação de `MFC0`, `MTC0` e `RFE`.
- Permissões de leitura e escrita específicas por registrador.
- `PRID` compatível com o R3000A do PlayStation.
- Reset do estado gravável e restauração dos valores fixos.
- Interrupções de software e a linha de hardware em `CAUSE.bit10`.

### Interrupções

O controlador de interrupções implementa `I_STAT` e `I_MASK`, incluindo latch
das onze fontes, máscara, acknowledge por escrita de zero e propagação da linha
IRQ para o COP0. Interrupções habilitadas respeitam `SR.IEc`, os bits de máscara
do COP0 e a conclusão de branch delay slots.

### Timers

Os três root counters implementam contador, modo e target em
`0x1F801100-0x1F801128`, incluindo:

- Clock do sistema e divisor por oito do Timer 2.
- Reset por target ou overflow.
- IRQ por target ou overflow.
- Modos one-shot, repeat, pulse e toggle.
- Flags de target/overflow limpas após leitura.
- Sincronização básica com sinais de HBlank/VBlank e dotclock.

### DMA

O controlador DMA implementa os registradores dos sete canais, `DPCR` e
`DICR`, incluindo prioridades, master enable, flags de conclusão, acknowledge,
bus error e IRQ3. Os caminhos funcionais atuais são:

- DMA2 em modo manual ou por blocos entre RAM e GPU.
- DMA2 em linked-list para envio de listas de comandos GP0.
- DMA6/OTC para criação reversa da ordering table.
- Endereçamento DMA de 24 bits com espelhos da RAM.

Os canais MDEC, CD-ROM, SPU e PIO preservam seus registradores, mas ainda não
executam transferências.

### GPU

A interface inicial da GPU implementa os ports `GP0/GPUREAD` e `GP1/GPUSTAT`,
estado de reset, bits de prontidão, direção de DMA, controle do display,
registradores internos básicos, atributos de desenho e IRQ1. Comandos de
rasterização e VRAM ainda não são processados.

O gerador de vídeo converte clocks da CPU para o domínio da GPU, percorre
scanlines NTSC/PAL, respeita as faixas de display configuradas por GP1, atualiza
o campo par/ímpar de `GPUSTAT`, gera IRQ0 no início de VBlank e alimenta os
root counters com dotclock e HBlank.

### SPU

A camada inicial da SPU preserva os registradores MMIO, aplica o modo de
`SPUCNT` em `SPUSTAT`, modela os requests de transferência e fornece RAM de som
e FIFO para escritas manuais. Síntese de vozes e saída de áudio ainda não estão
implementadas.

### Memória

O barramento implementa as seguintes regiões:

| Região | Endereço físico | Estado |
| --- | --- | --- |
| RAM principal | `0x00000000-0x001FFFFF` | Implementada |
| Espelhos da RAM | `0x00200000-0x007FFFFF` | Implementados |
| Expansion Region 1 | `0x1F000000-0x1F7FFFFF` | Stub com leituras `0xFF` |
| Scratchpad | `0x1F800000-0x1F8003FF` | Implementado |
| I/O Ports | `0x1F801000-0x1F801FFF` | Parcial |
| Expansion Region 2 | `0x1F802000-0x1F803FFF` | Stub |
| BIOS ROM | `0x1FC00000-0x1FC7FFFF` | Implementada |
| Memory Control | `0x1F801000-0x1F801020`, `0x1F801060` | Implementado |
| Interrupt Control | `0x1F801070-0x1F801077` | Implementado |
| DMA | `0x1F801080-0x1F8010FF` | DMA2/DMA6 funcionais |
| Root Counters | `0x1F801100-0x1F801128` | Implementados |
| GPU Ports | `0x1F801810-0x1F801817` | Interface inicial |
| SPU Registers | `0x1F801C00-0x1F801DFF` | Interface inicial |
| Cache Control | `0xFFFE0130` | Registrador implementado |

Escritas destinadas à cache isolada são impedidas de alterar a RAM principal,
preservando o código carregado pela BIOS durante sua rotina de inicialização.

### Temporização

`Cycles` continua representando a quantidade de instruções executadas.
`ClockCycles` contabiliza um custo aproximado de clock para instruction fetch,
loads e stores, diferenciando RAM em cache, RAM sem cache, scratchpad, MMIO,
Expansion 1 e BIOS.

Dispositivos implementam `IClockedDevice` e são registrados na `PsxMachine`;
eles recebem os ciclos decorridos após cada instrução. O timing de vídeo usa
acumuladores inteiros para converter clocks da CPU em clocks NTSC/PAL sem
depender de ponto flutuante. Este modelo ainda não representa contenção de
barramento, instruction cache completa ou timings internos de todas as
instruções e transferências.

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Uma imagem de BIOS válida do PlayStation 1 com exatamente 512 KiB

Por motivos legais, nenhuma BIOS é distribuída com o projeto. Utilize uma
imagem extraída de um console que você possui e coloque-a, por exemplo, em
`BiosPS1/SCPH1001.BIN`. O diretório `BiosPS1/` é ignorado pelo Git.

## Compilação

Na raiz do repositório:

```powershell
dotnet build SadPSX.slnx
```

## Executando a BIOS

```powershell
dotnet run --project SadPSX.Cli -- caminho\para\BIOS.BIN
```

Por padrão, a CLI executa 100 instruções. Para escolher outra quantidade:

```powershell
dotnet run --project SadPSX.Cli -- caminho\para\BIOS.BIN 1000000
```

Para imprimir todas as instruções executadas:

```powershell
dotnet run --project SadPSX.Cli -- caminho\para\BIOS.BIN 1000 --trace
```

Sem `--trace`, a CLI mantém um buffer e mostra apenas as últimas instruções ao
final da execução.

### Ferramentas de diagnóstico

A CLI também oferece breakpoints, checkpoints, detecção simples de loops e
relatórios de MMIO:

```powershell
dotnet run --project SadPSX.Cli -- caminho\para\BIOS.BIN 1000000 `
  --checkpoint 0xBFC00000 `
  --break-pc 0x80000080 `
  --break-memory 0x1F801060 `
  --loop-threshold 100000 `
  --mmio-log `
  --dump-registers
```

- `--break-pc` para antes de executar o endereço indicado.
- `--break-memory` para depois de uma leitura ou escrita de dados.
- `--checkpoint` registra o primeiro ciclo em que um PC é alcançado.
- `--loop-threshold` para quando um mesmo PC é visitado muitas vezes.
- `--mmio-log` mostra os primeiros acessos MMIO e um resumo por endereço.
- `--dump-registers` imprime GPRs, `HI/LO`, PC e registradores do COP0.
- `--validate` executa um smoke test e resume clocks, PCs, MMIO e exceções.

Os endereços podem ser informados em decimal ou hexadecimal com prefixo `0x`.

### Validação automatizada

Para compilar, executar todos os testes e validar um milhão de instruções da
BIOS em um único comando:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate.ps1
```

Também é possível escolher outra imagem e quantidade de instruções:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate.ps1 `
  -BiosPath .\BiosPS1\SCPH1001.BIN `
  -Instructions 2000000
```

Use `-NoRestore` quando os pacotes já estiverem disponíveis e a máquina estiver
offline.

O relatório é aprovado quando a quantidade solicitada é concluída sem falha do
runtime e sem instrução reservada, coprocessador inutilizável ou erro de
barramento. Syscalls e outras exceções emuladas continuam sendo contabilizadas,
mas não são consideradas automaticamente uma falha.

## Testes

Execute a suíte completa com:

```powershell
dotnet test SadPSX.slnx
```

Os testes cobrem:

- Decodificação de instruções.
- Aritmética, lógica e shifts.
- Branches, jumps e delay slots.
- Multiplicação e divisão.
- Loads, stores e load delay.
- Loads e stores desalinhados em todos os offsets.
- Exceções e registradores do COP0.
- Proteção entre modo usuário e segmentos do kernel.
- Contabilização de ciclos e sincronização de dispositivos.
- Controlador de interrupções e entrega ao COP0.
- Timers, targets, divisores e geração de IRQ.
- Handshakes, comandos de controle e status da GPU.
- Timing NTSC/PAL, dotclock, HBlank, VBlank e IRQ0.
- DMA2 por blocos/linked-list, DMA6/OTC e IRQ3.
- Registradores, status, FIFO e RAM de som da SPU.
- Tradução e roteamento do barramento.
- RAM, scratchpad, BIOS e Expansion Region 1.
- Disassembler e trace logger.
- Integração entre CPU, barramento e máquina.
- Programas MIPS completos na suíte de conformidade.
- Relatórios de validação com sucesso, exceções, MMIO e falhas do host.

## Estrutura do projeto

```text
SadPSX/
├── SadPSX.Core/
│   ├── Cpu/          # R3000A, COP0 e decodificação
│   ├── Gpu/          # Interface e estado da GPU
│   ├── Memory/       # RAM, scratchpad e regiões de memória
│   ├── Bus/          # Barramento e roteamento MMIO
│   ├── Bios/         # ROM e carregamento da BIOS
│   ├── CdRom/        # Subsistema de CD-ROM
│   ├── Dma/          # Canais e controle de DMA
│   ├── Timers/       # Root counters
│   ├── Interrupts/   # I_STAT, I_MASK e fontes de IRQ
│   ├── Spu/          # Interface e RAM de som
│   ├── Controllers/  # Controles e memory cards
│   ├── Debugging/    # Disassembler, debugger e validação
│   └── PsxMachine.cs
├── SadPSX.Cli/       # Executor de BIOS por linha de comando
├── SadPSX.Tests/
│   ├── Cpu/          # Testes da CPU
│   ├── Memory/       # Testes de memória e MMIO
│   ├── Gpu/          # Testes da GPU
│   └── Dma/          # Testes de DMA
├── BiosPS1/          # Dumps locais de BIOS, ignorados pelo Git
├── scripts/          # Validação automatizada
└── SadPSX.slnx
```

## Limitações

O SadPSX ainda não possui:

- Rasterização da GPU, VRAM funcional e saída de vídeo.
- Transferências DMA dos canais MDEC, CD-ROM, SPU e PIO.
- Chopping, contenção de barramento e duração assíncrona das transferências DMA.
- GTE/COP2.
- CD-ROM e carregamento de jogos.
- Síntese da SPU e saída de áudio.
- MDEC.
- Controles e memory cards.
- Temporização precisa por componente e contenção de barramento.
- Implementação completa da instruction cache.

Os demais periféricos MMIO ainda são stubs. O timing de vídeo cobre os sinais
necessários à BIOS e aos timers, mas ainda aproxima detalhes de entrelaçamento,
meias scanlines e diferenças físicas entre clocks de consoles PAL/NTSC.

## Próximos passos

O próximo componente natural é a VRAM com parser de pacotes GP0 e comandos
gráficos básicos, aproveitando o DMA2 já funcional. Depois dele, CD-ROM,
controles/memory cards e síntese da SPU poderão avançar sobre uma base de
interrupções e temporização já funcional.

## Licença

Este projeto está disponível sob os termos descritos em [LICENSE](LICENSE).
