using SadPSX.Core.Interrupts;

namespace SadPSX.Core.Gpu;

public sealed partial class Gpu
{
    private const int MaximumPolylineWords = 4096;

    private readonly List<uint> _gp0Packet = [];

    private int _expectedGp0Words;
    private bool _isPolylinePacket;
    private bool _cpuToVramActive;
    private int _cpuToVramX;
    private int _cpuToVramY;
    private int _cpuToVramWidth;
    private int _cpuToVramPixelsRemaining;
    private int _cpuToVramPixelIndex;
    private bool _vramToCpuActive;
    private int _vramToCpuX;
    private int _vramToCpuY;
    private int _vramToCpuWidth;
    private int _vramToCpuPixelsRemaining;
    private int _vramToCpuPixelIndex;

    private void ExecuteGp0(uint value)
    {
        LastGp0Command = value;
        Gp0CommandCount++;

        if (_cpuToVramActive)
        {
            WriteCpuToVramWord(value);
            FinishGp0Word();
            return;
        }

        if (_gp0Packet.Count == 0)
        {
            _expectedGp0Words = GetGp0PacketLength(value);
            _isPolylinePacket = _expectedGp0Words < 0;
        }
        else if (_isPolylinePacket && IsPolylineTerminator(value))
        {
            ExecuteGp0Packet();
            FinishGp0Word();
            return;
        }

        _gp0Packet.Add(value);

        if (_gp0Packet.Count == _expectedGp0Words)
            ExecuteGp0Packet();
        else if (_gp0Packet.Count >= MaximumPolylineWords)
            ResetPacketCollection();

        FinishGp0Word();
    }

    private static int GetGp0PacketLength(uint commandWord)
    {
        int group = (int)(commandWord >> 29);
        byte command = (byte)(commandWord >> 24);

        return group switch
        {
            0 => command == 0x02 ? 3 : 1,
            1 => GetPolygonPacketLength(commandWord),
            2 => (commandWord & (1u << 27)) != 0
                ? -1
                : (commandWord & (1u << 28)) != 0 ? 4 : 3,
            3 => GetRectanglePacketLength(commandWord),
            4 => 4,
            5 or 6 => 3,
            7 => 1,
            _ => 1,
        };
    }

    private static int GetPolygonPacketLength(uint commandWord)
    {
        int vertexCount = (commandWord & (1u << 27)) != 0 ? 4 : 3;
        bool textured = (commandWord & (1u << 26)) != 0;
        bool gouraud = (commandWord & (1u << 28)) != 0;

        return 1 +
               vertexCount * (textured ? 2 : 1) +
               (gouraud ? vertexCount - 1 : 0);
    }

    private static int GetRectanglePacketLength(uint commandWord)
    {
        bool textured = (commandWord & (1u << 26)) != 0;
        bool variableSize = ((commandWord >> 27) & 3) == 0;
        return 2 + (textured ? 1 : 0) + (variableSize ? 1 : 0);
    }

    private bool IsPolylineTerminator(uint value)
    {
        if ((value & 0xF000_F000) != 0x5000_5000)
            return false;

        bool gouraud = (_gp0Packet[0] & (1u << 28)) != 0;
        if (!gouraud)
            return _gp0Packet.Count >= 3;

        return _gp0Packet.Count >= 4 && (_gp0Packet.Count & 1) == 0;
    }

    private void ExecuteGp0Packet()
    {
        uint commandWord = _gp0Packet[0];
        int group = (int)(commandWord >> 29);

        switch (group)
        {
            case 0:
                ExecuteMiscellaneousCommand(commandWord);
                break;

            case 1:
                DrawPolygon(_gp0Packet);
                break;

            case 2:
                DrawLine(_gp0Packet);
                break;

            case 3:
                DrawRectangle(_gp0Packet);
                break;

            case 4:
                CopyVramToVram(_gp0Packet);
                break;

            case 5:
                StartCpuToVramTransfer(_gp0Packet);
                break;

            case 6:
                StartVramToCpuTransfer(_gp0Packet);
                break;

            case 7:
                ExecuteEnvironmentCommand(commandWord);
                break;
        }

        ResetPacketCollection();
    }

    private void ExecuteMiscellaneousCommand(uint commandWord)
    {
        switch ((byte)(commandWord >> 24))
        {
            case 0x02:
                FillVram(_gp0Packet);
                break;

            case 0x1F:
                _status |= InterruptRequestBit;
                _interruptController.Request(InterruptSource.Gpu);
                break;
        }
    }

    private void ExecuteEnvironmentCommand(uint commandWord)
    {
        switch ((byte)(commandWord >> 24))
        {
            case 0xE1:
                _status = (_status & ~0x0000_07FFu) |
                          (commandWord & 0x0000_07FFu);
                _internalRegisters[0] = commandWord & 0x0000_3FFFu;
                break;

            case 0xE2:
                _internalRegisters[2] = commandWord & 0x000F_FFFF;
                break;

            case 0xE3:
                _internalRegisters[3] = commandWord & 0x000F_FFFF;
                break;

            case 0xE4:
                _internalRegisters[4] = commandWord & 0x000F_FFFF;
                break;

            case 0xE5:
                _internalRegisters[5] = commandWord & 0x003F_FFFF;
                break;

            case 0xE6:
                _status = (_status & ~0x0000_1800u) |
                          ((commandWord & 3u) << 11);
                _internalRegisters[6] = commandWord & 3;
                break;
        }
    }

    private void FillVram(IReadOnlyList<uint> packet)
    {
        ushort color = ConvertColor(packet[0]);
        int startX = (int)(packet[1] & 0x3F0);
        int startY = (int)((packet[1] >> 16) & 0x1FF);
        int width = (int)(((packet[2] & 0x3FF) + 0x0F) & ~0x0Fu);
        int height = (int)((packet[2] >> 16) & 0x1FF);

        if (width == 0 || height == 0)
            return;

        for (int offsetY = 0; offsetY < height; offsetY++)
        {
            for (int offsetX = 0; offsetX < width; offsetX++)
                Vram.WritePixel(startX + offsetX, startY + offsetY, color);
        }
    }

    private void CopyVramToVram(IReadOnlyList<uint> packet)
    {
        DecodeTransferPosition(packet[1], out int sourceX, out int sourceY);
        DecodeTransferPosition(packet[2], out int destinationX, out int destinationY);
        DecodeTransferSize(packet[3], out int width, out int height);

        var pixels = new ushort[width * height];
        for (int offsetY = 0; offsetY < height; offsetY++)
        {
            for (int offsetX = 0; offsetX < width; offsetX++)
            {
                pixels[offsetY * width + offsetX] =
                    Vram.ReadPixel(sourceX + offsetX, sourceY + offsetY);
            }
        }

        for (int offsetY = 0; offsetY < height; offsetY++)
        {
            for (int offsetX = 0; offsetX < width; offsetX++)
            {
                WriteTransferredPixel(
                    destinationX + offsetX,
                    destinationY + offsetY,
                    pixels[offsetY * width + offsetX]);
            }
        }
    }

    private void StartCpuToVramTransfer(IReadOnlyList<uint> packet)
    {
        DecodeTransferPosition(packet[1], out _cpuToVramX, out _cpuToVramY);
        DecodeTransferSize(packet[2], out _cpuToVramWidth, out int height);
        _cpuToVramPixelsRemaining = _cpuToVramWidth * height;
        _cpuToVramPixelIndex = 0;
        _cpuToVramActive = _cpuToVramPixelsRemaining > 0;
    }

    private void WriteCpuToVramWord(uint value)
    {
        WriteCpuToVramPixel((ushort)value);

        if (_cpuToVramActive)
            WriteCpuToVramPixel((ushort)(value >> 16));
    }

    private void WriteCpuToVramPixel(ushort pixel)
    {
        int pixelX =
            _cpuToVramX + (_cpuToVramPixelIndex % _cpuToVramWidth);
        int pixelY =
            _cpuToVramY + (_cpuToVramPixelIndex / _cpuToVramWidth);
        WriteTransferredPixel(pixelX, pixelY, pixel);

        _cpuToVramPixelIndex++;
        _cpuToVramPixelsRemaining--;
        _cpuToVramActive = _cpuToVramPixelsRemaining > 0;
    }

    private void StartVramToCpuTransfer(IReadOnlyList<uint> packet)
    {
        DecodeTransferPosition(packet[1], out _vramToCpuX, out _vramToCpuY);
        DecodeTransferSize(packet[2], out _vramToCpuWidth, out int height);
        _vramToCpuPixelsRemaining = _vramToCpuWidth * height;
        _vramToCpuPixelIndex = 0;
        _vramToCpuActive = _vramToCpuPixelsRemaining > 0;

        if (_vramToCpuActive)
            _status |= ReadyForVramReadBit;
    }

    private uint ReadGpuRead(bool advance)
    {
        if (!_vramToCpuActive)
            return _gpuRead;

        int pixelIndex = _vramToCpuPixelIndex;
        ushort low = ReadVramTransferPixel(pixelIndex);
        ushort high = _vramToCpuPixelsRemaining > 1
            ? ReadVramTransferPixel(pixelIndex + 1)
            : (ushort)0;
        uint value = low | ((uint)high << 16);

        if (!advance)
            return value;

        int consumed = Math.Min(2, _vramToCpuPixelsRemaining);
        _vramToCpuPixelIndex += consumed;
        _vramToCpuPixelsRemaining -= consumed;
        _vramToCpuActive = _vramToCpuPixelsRemaining > 0;
        _gpuRead = value;

        if (!_vramToCpuActive)
            _status &= ~ReadyForVramReadBit;

        UpdateDmaRequest();
        return value;
    }

    private ushort ReadVramTransferPixel(int pixelIndex)
    {
        int pixelX = _vramToCpuX + (pixelIndex % _vramToCpuWidth);
        int pixelY = _vramToCpuY + (pixelIndex / _vramToCpuWidth);
        return Vram.ReadPixel(pixelX, pixelY);
    }

    private void WriteTransferredPixel(int pixelX, int pixelY, ushort pixel)
    {
        ushort current = Vram.ReadPixel(pixelX, pixelY);
        if ((_internalRegisters[6] & 2) != 0 && (current & 0x8000) != 0)
            return;

        if ((_internalRegisters[6] & 1) != 0)
            pixel |= 0x8000;

        Vram.WritePixel(pixelX, pixelY, pixel);
    }

    private static void DecodeTransferPosition(
        uint value,
        out int pixelX,
        out int pixelY)
    {
        pixelX = (int)(value & 0x3FF);
        pixelY = (int)((value >> 16) & 0x1FF);
    }

    private static void DecodeTransferSize(uint value, out int width, out int height)
    {
        width = (int)(((value & 0x3FF) - 1) & 0x3FF) + 1;
        height = (int)((((value >> 16) & 0x1FF) - 1) & 0x1FF) + 1;
    }

    private static ushort ConvertColor(uint value)
    {
        int red = (int)(value & 0xFF) >> 3;
        int green = (int)((value >> 8) & 0xFF) >> 3;
        int blue = (int)((value >> 16) & 0xFF) >> 3;
        return (ushort)(red | (green << 5) | (blue << 10));
    }

    private void FinishGp0Word()
    {
        _status |= ReadyForCommandBit | ReadyForDmaBlockBit;
        UpdateDmaRequest();
    }

    private void ResetPacketCollection()
    {
        _gp0Packet.Clear();
        _expectedGp0Words = 0;
        _isPolylinePacket = false;
    }

    private void ResetGp0CommandBuffer()
    {
        ResetPacketCollection();
        _cpuToVramActive = false;
        _cpuToVramPixelsRemaining = 0;
        _cpuToVramPixelIndex = 0;
        _vramToCpuActive = false;
        _vramToCpuPixelsRemaining = 0;
        _vramToCpuPixelIndex = 0;
        _status |= ReadyForCommandBit | ReadyForDmaBlockBit;
        _status &= ~ReadyForVramReadBit;
        UpdateDmaRequest();
    }
}
