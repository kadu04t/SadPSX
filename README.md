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
- Processar exceções através do COP0.
- Detectar overflow, acessos desalinhados e erros de barramento.
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
- Loads e stores básicos.
- Branch delay e load delay.
- Registradores `HI`, `LO` e `$zero`.

Algumas instruções ainda não estão implementadas, incluindo `LWL`, `LWR`,
`SWL`, `SWR` e as operações do GTE.

### COP0

O COP0 atualmente possui:

- Registradores `SR`, `CAUSE`, `EPC` e `BadVaddr`.
- Exceções de syscall, breakpoint e overflow.
- Exceções de endereço e barramento.
- Exceção de instrução reservada.
- Identificação de exceções em branch delay slots.
- Seleção dos vetores de exceção por `SR.BEV`.
- Implementação de `MFC0`, `MTC0` e `RFE`.
- Reset completo dos registradores.

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
| Cache Control | `0xFFFE0000-0xFFFE01FF` | Parcial |

Escritas destinadas à cache isolada são impedidas de alterar a RAM principal,
preservando o código carregado pela BIOS durante sua rotina de inicialização.

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
- Exceções e registradores do COP0.
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
- Temporização precisa por componente.
- Implementação completa da instruction cache.

Os registradores MMIO ainda são stubs. Por isso, a BIOS pode entrar em loops ou
receber valores diferentes dos apresentados pelo hardware real.

## Próximos passos

Antes de adicionar novos periféricos, a prioridade é:

1. Completar as instruções restantes do R3000A.
2. Refinar o comportamento do COP0.
3. Implementar corretamente memory control e cache control.
4. Criar testes de conformidade com pequenos programas MIPS.
5. Melhorar o diagnóstico de loops e acessos MMIO não tratados.

Depois dessa base, os próximos componentes naturais são controlador de
interrupções, timers, DMA e uma implementação mínima da GPU.

## Licença

Este projeto está disponível sob os termos descritos em [LICENSE](LICENSE).
