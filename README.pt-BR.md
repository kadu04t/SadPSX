# SadPSX

<p align="center">
  <img src="docs/assets/sadpsx-logo.png" alt="SadPSX — A PlayStation Emulator" width="360">
</p>

[English](README.md) | **Português (Brasil)**

SadPSX é um emulador experimental de PlayStation 1 escrito em C# e .NET.

O projeto ainda está em desenvolvimento inicial. O foco atual é melhorar a
precisão, a compatibilidade e a organização sobre uma base que já inicializa
jogos comerciais.

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
- Transferir dados pelo COP2 e executar comandos geométricos essenciais da GTE.
- Processar exceções através do COP0.
- Entregar interrupções mascaradas ao COP0.
- Executar os três root counters e suas IRQs.
- Responder aos handshakes básicos da GPU e da SPU.
- Executar DMA2 temporizado para a GPU e DMA6 para tabelas OTC.
- Gerar dotclock, HBlank, VBlank e IRQ0 em modos NTSC/PAL.
- Apresentar a saída de vídeo da GPU em uma janela SDL3 redimensionável.
- Consultar um controle digital pelo SIO0 e usar teclado ou gamepad SDL3.
- Abrir um launcher básico para selecionar BIOS e imagens BIN/CUE.
- Exibir o progresso POST da BIOS no console de diagnóstico.
- Detectar overflow, acessos desalinhados e erros de barramento.
- Bloquear acessos de usuário aos segmentos do kernel.
- Contabilizar custos aproximados de acesso à memória.
- Produzir traces com endereço, instrução crua e disassembly.
- Validar uma execução da BIOS com métricas e critérios reproduzíveis.

Na validação atual, a BIOS SCPH-1001 chega ao menu do console e o Rayman inicia,
reproduz sua abertura, reconhece controles SDL3 e entra em gameplay. A
compatibilidade ainda é experimental: existem defeitos visuais, falhas de
áudio e jogos que podem não iniciar ou travar.

## Jogos testados

Os resultados de compatibilidade descrevem sessões específicas de teste e não
garantem suporte a todas as regiões, revisões ou imagens de disco.

| Jogo | Estado | Comportamento observado |
| --- | --- | --- |
| Rayman | Jogável | Inicia pela BIOS, reproduz a abertura, chega ao gameplay e aceita controles SDL3. Os gráficos ainda apresentam problemas visíveis de precisão e o áudio pode falhar ou engasgar. |

<p align="center">
  <img src="docs/screenshots/rayman-gameplay.png" alt="Rayman executando no SadPSX" width="900">
</p>

<p align="center"><em>Rayman executando no SadPSX durante um teste de compatibilidade.</em></p>

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

### GTE/COP2

O COP2 implementa `MFC2`, `MTC2`, `CFC2`, `CTC2`, `LWC2` e `SWC2`, incluindo
load delay nas transferências para a CPU. A GTE possui FIFOs e semântica dos
registradores de dados/controle, além dos comandos `RTPS`, `RTPT`, `NCLIP`,
`OP`, `MVMVA`, `SQR`, `AVSZ3`, `AVSZ4`, `NCS`, `NCT`, `NCCS`, `NCCT`,
`NCDS` e `NCDT`.

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

- DMA0 por blocos da RAM para o MDEC.
- DMA1 por blocos do MDEC para a RAM.
- DMA2 incremental em modo manual ou por blocos entre RAM e GPU, respeitando
  direção, DREQ, estado ocupado e atualização de `MADR`/`BCR`.
- DMA2 linked-list processado por nós para envio de listas de comandos GP0.
- DMA3 por blocos do FIFO do CD-ROM para a RAM.
- DMA4 por blocos entre RAM e SPU.
- DMA6/OTC para criação reversa da ordering table.
- Endereçamento DMA de 24 bits com espelhos da RAM.

O canal PIO preserva seus registradores, mas ainda não executa transferências.

### CD-ROM

O controlador possui bancos de registradores, FIFOs, IRQ2, comandos de status,
busca e leitura. `ReadN`/`ReadS` entregam setores continuamente em velocidade
simples ou dupla, com buffers, `DataReady`, `DataEnd`, pausa e DMA3. Imagens CUE
podem descrever múltiplas faixas de dados e áudio, usadas por `GetTN`, `GetTD`,
`GetlocL` e `GetlocP`. `Init`, `GetID`, `SetSession`, `SeekL`, `SeekP` e
`ReadTOC` modelam motor, busca e respostas secundárias temporizadas. Ao montar
um disco, o leitor ISO9660 localiza `SYSTEM.CNF` e confirma o caminho, LBA e
tamanho do executável de boot. As respostas de comandos respeitam uma latência
mínima do controlador para evitar que a BIOS perca o IRQ antes de preparar a
espera assíncrona. O comando `Play` avança faixas CD-DA no clock de 75 setores
por segundo, envia PCM estéreo à SPU e gera `INT4` ao terminar a faixa com
AutoPause.

### GPU

A GPU implementa os ports `GP0/GPUREAD` e `GP1/GPUSTAT`, uma VRAM de
`1024x512` pixels de 16 bits e o parser de pacotes GP0 enviados pela CPU ou
DMA2. Estão disponíveis preenchimento e cópia da VRAM, transferências
CPU↔VRAM, polígonos, linhas, polylines e retângulos flat, Gouraud e
texturizados, incluindo CLUT de 4/8 bits, texturas de 15 bits, clipping,
offset de desenho, texture window, flips de sprites, máscara e
semitransparência por texel. Polígonos usam a regra top-left e dithering 4x4
para Gouraud e modulação. Primitivas que excedem os limites físicos são
descartadas, e `Texpage` e os bits de prontidão de `GPUSTAT` acompanham o
estado do parser usado pelo DMA2.

O gerador de vídeo converte clocks da CPU para o domínio da GPU, percorre
scanlines NTSC/PAL, respeita as faixas de display configuradas por GP1, atualiza
o campo par/ímpar de `GPUSTAT`, gera IRQ0 no início de VBlank e alimenta os
root counters com dotclock e HBlank.

### SPU

A SPU preserva os registradores MMIO, aplica o modo de `SPUCNT` em `SPUSTAT`,
fornece 512 KiB de RAM de som e transfere dados manualmente ou por DMA4. As 24
vozes decodificam blocos ADPCM, aplicam pitch, loop, key on/off, ADSR e volumes
estéreo. A mistura também recebe o PCM das faixas CD-DA, respeitando volumes e
o enable de áudio do CD. O frontend envia a saída de 44,1 kHz para um stream
SDL3.

### MDEC

O Macroblock Decoder recebe comandos pela CPU ou DMA0, carrega tabelas de
quantização e escala e decodifica blocos RLE com IDCT. A saída monocromática de
4/8 bpp e colorida de 15/24 bpp fica disponível no FIFO e pode retornar à RAM
por DMA1.

### Memória

O barramento implementa as seguintes regiões:

| Região | Endereço físico | Estado |
| --- | --- | --- |
| RAM principal | `0x00000000-0x001FFFFF` | Implementada |
| Espelhos da RAM | `0x00200000-0x007FFFFF` | Implementados |
| Expansion Region 1 | `0x1F000000-0x1F7FFFFF` | Stub com leituras `0xFF` |
| Scratchpad | `0x1F800000-0x1F8003FF` | Implementado |
| I/O Ports | `0x1F801000-0x1F801FFF` | Parcial |
| Expansion Region 2 | `0x1F802000-0x1F803FFF` | POST da BIOS; restante em stub |
| BIOS ROM | `0x1FC00000-0x1FC7FFFF` | Implementada |
| Memory Control | `0x1F801000-0x1F801020`, `0x1F801060` | Implementado |
| SIO0 | `0x1F801040-0x1F80104F` | Controle digital, timing e IRQ7 |
| Interrupt Control | `0x1F801070-0x1F801077` | Implementado |
| DMA | `0x1F801080-0x1F8010FF` | DMA0-DMA4 e DMA6 funcionais |
| Root Counters | `0x1F801100-0x1F801128` | Implementados |
| GPU Ports | `0x1F801810-0x1F801817` | GP0/GP1 e VRAM funcionais |
| MDEC | `0x1F801820-0x1F801827` | Comandos, tabelas, RLE/IDCT e FIFO |
| CD-ROM Ports | `0x1F801800-0x1F801803` | Comandos, FIFOs, IRQ2 e setores |
| SPU Registers | `0x1F801C00-0x1F801DFF` | Vozes, ADPCM, ADSR, RAM e DMA |
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

O projeto também não distribui jogos, imagens de disco, chaves ou arquivos
proprietários necessários à execução. Use somente dumps obtidos legalmente de
mídias que você possui. Capturas de compatibilidade pertencem aos respectivos
detentores dos direitos e são exibidas apenas para documentar o comportamento
do emulador. SadPSX não é afiliado, associado ou endossado pela Sony
Interactive Entertainment.

## Compilação

Na raiz do repositório:

```powershell
dotnet build SadPSX.slnx
```

## Executando o frontend

O frontend SDL3 inicia a BIOS e apresenta a região ativa da VRAM em uma janela:

```powershell
dotnet run -c Release --project SadPSX.Frontend -- .\BiosPS1\SCPH1001.BIN
```

Uma imagem de disco BIN ou CUE pode ser conectada na inicialização:

```powershell
dotnet run -c Release --project SadPSX.Frontend -- .\BiosPS1\SCPH1001.BIN `
  --disc .\GamesPS1\Jogo.cue
```

Ao abrir `SadPSX.exe` sem argumentos, uma janela simples permite selecionar uma
BIOS de 512 KiB, escolher opcionalmente uma imagem `.cue` ou `.bin` e iniciar o
emulador pelo botão **Start**. Os argumentos de linha de comando continuam
disponíveis para desenvolvimento e automação.

Quando a imagem possui uma estrutura inicializável, o console mostra o
executável encontrado em `SYSTEM.CNF`. O relatório `F1` inclui o último comando
do CD-ROM, quantidade de comandos, LBA atual e estado de leitura.

O terminal funciona como console de diagnóstico durante a execução. Alertas de
exceções inesperadas, loops curtos, falta de progresso de vídeo e acessos MMIO
não tratados são exibidos automaticamente. O relatório completo também fica em
`SadPSX.Frontend/bin/<configuração>/net10.0/Logs/SadPSX.log`.

Atalhos de diagnóstico:

- `F1`: estado geral da CPU, vídeo, IRQ, CD-ROM, DMA e MMIO.
- `F2`: instrução atual e registradores da CPU.
- `F3`: acessos MMIO ainda não implementados.
- `F4`: exceções recentes da CPU.

A entrada do controle digital usa:

- Gamepads Xbox, PlayStation e genéricos reconhecidos pelo SDL3, inclusive com
  conexão e remoção durante a execução.
- Setas: direcional.
- `Z`/`X`: cruz/círculo.
- `A`/`S`: quadrado/triângulo.
- `Q`/`W`: L1/R1; `E`/`D`: L2/R2.
- `Enter`: Start; `Backspace`: Select.

Nos gamepads, os botões de face, direcionais, Start/Select, L1/R1, L2/R2 e
L3/R3 seguem o mapeamento padronizado do SDL3. O teclado continua funcionando
ao mesmo tempo e serve como fallback.

A emulação roda continuamente e a primeira tela da BIOS pode levar algum tempo
para aparecer enquanto o interpretador executa a inicialização. Atalhos:

- `Espaço`: pausa ou continua a emulação.
- `R`: reinicia o console.
- `F11`: alterna tela cheia.
- `Esc`: encerra o frontend.

Use `--batch N` para ajustar quantas instruções são executadas entre eventos da
janela. `--paused` inicia pausado e `--frames N` limita a apresentação para
diagnósticos automatizados.

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
- `--disc` conecta uma imagem BIN ou CUE para testes de boot sem frontend.
- `--stop-on-unexpected` interrompe na primeira exceção inesperada e mostra
  opcode, memória próxima e registradores.

Os endereços podem ser informados em decimal ou hexadecimal com prefixo `0x`.

Para investigar o boot de um jogo em velocidade de `Release`:

```powershell
dotnet run -c Release --project SadPSX.Cli -- `
  .\BiosPS1\SCPH1001.BIN 600000000 `
  --disc ".\GamesPS1\Jogo\Jogo.cue" `
  --stop-on-unexpected
```

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
- Transferências COP2, registradores e comandos geométricos da GTE.
- Proteção entre modo usuário e segmentos do kernel.
- Contabilização de ciclos e sincronização de dispositivos.
- Controlador de interrupções e entrega ao COP0.
- Timers, targets, divisores e geração de IRQ.
- Handshakes, comandos de controle e status da GPU.
- Pacotes GP0, transferências de VRAM e primitivas gráficas básicas.
- Timing NTSC/PAL, dotclock, HBlank, VBlank e IRQ0.
- DMA0-DMA4 por blocos, DMA2 linked-list, DMA6/OTC e IRQ3.
- MDEC com tabelas, RLE, IDCT e saídas de 4/8/15/24 bpp.
- SPU com 24 vozes, ADPCM, pitch, ADSR, mistura estéreo e DMA4.
- Reprodução CD-DA, AutoPause, `INT4` e mistura das faixas na SPU.
- Saída de áudio SDL3 em 44,1 kHz no frontend.
- SIO0, protocolo do controle digital, IRQ e POST da BIOS.
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
│   ├── Gte/          # COP2 e Geometry Transformation Engine
│   ├── Gpu/          # Interface e estado da GPU
│   ├── Memory/       # RAM, scratchpad e regiões de memória
│   ├── Bus/          # Barramento e roteamento MMIO
│   ├── Bios/         # ROM e carregamento da BIOS
│   ├── CdRom/        # Subsistema de CD-ROM
│   ├── Dma/          # Canais e controle de DMA
│   ├── Mdec/         # Decodificação de vídeo comprimido
│   ├── Timers/       # Root counters
│   ├── Interrupts/   # I_STAT, I_MASK e fontes de IRQ
│   ├── Spu/          # Interface e RAM de som
│   ├── Controllers/  # Controles e memory cards
│   ├── Debugging/    # Disassembler, debugger e validação
│   └── PsxMachine.cs
├── SadPSX.Cli/       # Executor de BIOS por linha de comando
├── SadPSX.Frontend/  # Janela SDL3 e loop interativo
├── SadPSX.Tests/
│   ├── Cpu/          # Testes da CPU
│   ├── Memory/       # Testes de memória e MMIO
│   ├── Gpu/          # Testes da GPU
│   ├── Dma/          # Testes de DMA
│   ├── Mdec/         # Testes do MDEC e DMA0/1
│   ├── Gte/          # Testes da GTE e COP2
│   └── Controllers/  # Testes do SIO0 e controle digital
├── BiosPS1/          # Dumps locais de BIOS, ignorados pelo Git
├── GamesPS1/         # Imagens locais de jogos, ignoradas pelo Git
├── docs/
│   ├── assets/       # Logo do projeto e ícone do aplicativo
│   └── screenshots/  # Capturas de compatibilidade
├── scripts/          # Validação e empacotamento
└── SadPSX.slnx
```

## Limitações

O SadPSX ainda não possui:

- Interface completa de configurações, persistência e biblioteca de jogos.
- Sincronização de velocidade e execução da CPU em thread dedicada.
- Rasterização completamente pixel-perfect e temporização do FIFO da GPU.
- Transferências DMA do canal PIO.
- Chopping, arbitragem entre canais, contenção de barramento e timing dos
  canais DMA além do DMA2.
- Comandos de iluminação/cor restantes e precisão completa da GTE.
- Compatibilidade após a entrada do executável ainda está em validação com
  imagens comerciais.
- Relatórios periódicos de `Play`, matriz de volume do CD-ROM e áudio XA-ADPCM.
- Reverb, noise, pitch modulation e precisão completa dos envelopes da SPU.
- Precisão bit a bit da IDCT e temporização dos FIFOs do MDEC.
- Protocolo DualShock, controles analógicos e memory cards.
- Temporização precisa por componente e contenção de barramento.
- Implementação completa da instruction cache.

Os demais periféricos MMIO ainda são stubs. O timing de vídeo cobre os sinais
necessários à BIOS e aos timers, mas ainda aproxima detalhes de entrelaçamento,
meias scanlines e diferenças físicas entre clocks de consoles PAL/NTSC.

## Próximos passos

Os próximos passos naturais são ampliar os testes de compatibilidade após o
salto da BIOS para o `PS-X EXE`, refinar FIFO/timing da GPU, completar os
comandos de cor restantes da GTE e adicionar memory cards. O bloco futuro de
otimização mantém benchmarks,
execução da CPU em lotes, caminhos rápidos do barramento, scheduler de eventos
e separação entre diagnóstico normal e trace completo.

## Licença

Este projeto está disponível sob os termos descritos em [LICENSE](LICENSE).
As licenças das dependências distribuídas estão em
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
O histórico de versões está em [CHANGELOG.md](CHANGELOG.md).
