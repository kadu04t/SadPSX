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
- Detectar overflow, acessos desalinhados e erros de barramento.
- Bloquear acessos de usuário aos segmentos do kernel.
- Contabilizar custos aproximados de acesso à memória.
- Produzir traces com endereço, instrução crua e disassembly.

Na validação atual, a BIOS SCPH-1001 executa pelo menos 1.000.000 de
instruções sem provocar uma exceção do runtime do .NET.

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

### Memória

O barramento implementa as seguintes regiões:

| Região | Endereço físico | Estado |
| --- | --- | --- |
| RAM principal | `0x00000000-0x001FFFFF` | Implementada |
| Espelhos da RAM | `0x00200000-0x007FFFFF` | Implementados |
| Expansion Region 1 | `0x1F000000-0x1F7FFFFF` | Stub com leituras `0xFF` |
| Scratchpad | `0x1F800000-0x1F8003FF` | Implementado |
| I/O Ports | `0x1F801000-0x1F801FFF` | Stub |
| Expansion Region 2 | `0x1F802000-0x1F803FFF` | Stub |
| BIOS ROM | `0x1FC00000-0x1FC7FFFF` | Implementada |
| Memory Control | `0x1F801000-0x1F801020`, `0x1F801060` | Implementado |
| Cache Control | `0xFFFE0130` | Registrador implementado |

Escritas destinadas à cache isolada são impedidas de alterar a RAM principal,
preservando o código carregado pela BIOS durante sua rotina de inicialização.

### Temporização

`Cycles` continua representando a quantidade de instruções executadas.
`ClockCycles` contabiliza um custo aproximado de clock para instruction fetch,
loads e stores, diferenciando RAM em cache, RAM sem cache, scratchpad, MMIO,
Expansion 1 e BIOS.

Dispositivos futuros podem implementar `IClockedDevice` e ser registrados na
`PsxMachine`; eles recebem os ciclos decorridos após cada instrução. Este modelo
é uma base de escalonamento e ainda não representa contenção de barramento,
instruction cache completa ou timings internos de todas as instruções.

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Uma imagem de BIOS válida do PlayStation 1 com exatamente 512 KiB

Por motivos legais, nenhuma BIOS é distribuída com o projeto. Utilize uma
imagem extraída de um console que você possui.

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

Os endereços podem ser informados em decimal ou hexadecimal com prefixo `0x`.

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
- Tradução e roteamento do barramento.
- RAM, scratchpad, BIOS e Expansion Region 1.
- Disassembler e trace logger.
- Integração entre CPU, barramento e máquina.

## Estrutura do projeto

```text
SadPSX/
├── SadPSX.Core/
│   ├── Cpu/          # R3000A, COP0 e decodificação
│   ├── Memory/       # Barramento e regiões de memória
│   ├── Debugging/    # Disassembler e trace logger
│   └── PsxMachine.cs
├── SadPSX.Cli/       # Executor de BIOS por linha de comando
├── SadPSX.Tests/     # Testes unitários e de integração
└── SadPSX.slnx
```

## Limitações

O SadPSX ainda não possui:

- GPU e saída de vídeo.
- DMA.
- Controlador de interrupções.
- Timers.
- GTE/COP2.
- CD-ROM e carregamento de jogos.
- SPU e saída de áudio.
- MDEC.
- Controles e memory cards.
- Temporização precisa por componente e contenção de barramento.
- Implementação completa da instruction cache.

Os periféricos MMIO ainda são stubs. Memory Control e Cache Control preservam
seus valores, mas timings e efeitos completos ainda não são emulados.

## Próximos passos

Antes de adicionar periféricos complexos, a prioridade é:

1. Ampliar testes de conformidade com pequenos programas MIPS.
2. Refinar timings internos da CPU, cache e memory control.
3. Usar os diagnósticos para mapear os próximos acessos MMIO da BIOS.

Depois dessa base, os próximos componentes naturais são controlador de
interrupções, timers, DMA e uma implementação mínima da GPU.

## Licença

Este projeto está disponível sob os termos descritos em [LICENSE](LICENSE).
