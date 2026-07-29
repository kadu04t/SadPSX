namespace SadPSX.Core.Controllers;

public enum Sio0PeripheralKind
{
    Unknown,
    Controller,
    MemoryCard,
}

public readonly record struct Sio0TransferTrace(
    ulong Sequence,
    ulong Transaction,
    int ByteIndex,
    ulong StartCycle,
    ulong EndCycle,
    ulong? AcknowledgeCycle,
    int Port,
    Sio0PeripheralKind Peripheral,
    bool Connected,
    bool Queued,
    byte Transmit,
    byte Receive,
    bool Acknowledge,
    ushort Control,
    ushort Mode,
    ushort Baud);
