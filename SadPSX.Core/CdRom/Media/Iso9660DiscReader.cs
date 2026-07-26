using System.Buffers.Binary;
using System.Text;

namespace SadPSX.Core.CdRom.Media;

internal static class Iso9660DiscReader
{
    private const int SectorSize = 2048;
    private const int FirstVolumeDescriptorSector = 16;
    private const int MaximumVolumeDescriptorSector = 31;
    private const int RootDirectoryRecordOffset = 156;
    private const int MaximumDirectorySize = 4 * 1024 * 1024;
    private const int MaximumSystemConfigSize = 64 * 1024;

    public static bool TryGetBootInfo(
        DiscImage disc,
        out DiscBootInfo? bootInfo)
    {
        ArgumentNullException.ThrowIfNull(disc);
        bootInfo = null;

        try
        {
            if (!TryReadRootDirectory(disc, out DirectoryEntry root) ||
                !TryFindEntry(
                    disc,
                    root,
                    "SYSTEM.CNF",
                    out DirectoryEntry systemConfig) ||
                systemConfig.IsDirectory ||
                systemConfig.Size > MaximumSystemConfigSize)
            {
                return false;
            }

            byte[] configBytes = ReadExtent(
                disc,
                systemConfig.Extent,
                systemConfig.Size);
            string? executablePath = ParseBootPath(configBytes);
            if (executablePath is null ||
                !TryResolvePath(
                    disc,
                    root,
                    executablePath,
                    out DirectoryEntry executable) ||
                executable.IsDirectory)
            {
                return false;
            }

            bootInfo = new DiscBootInfo(
                executablePath,
                executable.Extent,
                executable.Size);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            ArgumentOutOfRangeException or
            OverflowException)
        {
            return false;
        }
    }

    private static bool TryReadRootDirectory(
        DiscImage disc,
        out DirectoryEntry root)
    {
        var sector = new byte[SectorSize];
        int lastSector = Math.Min(
            MaximumVolumeDescriptorSector,
            disc.SectorCount - 1);
        for (int logicalBlockAddress = FirstVolumeDescriptorSector;
             logicalBlockAddress <= lastSector;
             logicalBlockAddress++)
        {
            disc.ReadUserDataSector(logicalBlockAddress, sector);
            if (!sector.AsSpan(1, 5).SequenceEqual("CD001"u8))
                continue;
            if (sector[0] == 255)
                break;
            if (sector[0] != 1 || sector[6] != 1)
                continue;

            return TryParseEntry(
                sector,
                RootDirectoryRecordOffset,
                out root);
        }

        root = default;
        return false;
    }

    private static bool TryResolvePath(
        DiscImage disc,
        DirectoryEntry root,
        string path,
        out DirectoryEntry entry)
    {
        string[] parts = path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            entry = default;
            return false;
        }

        DirectoryEntry current = root;
        foreach (string part in parts)
        {
            if (!current.IsDirectory ||
                !TryFindEntry(disc, current, part, out current))
            {
                entry = default;
                return false;
            }
        }

        entry = current;
        return true;
    }

    private static bool TryFindEntry(
        DiscImage disc,
        DirectoryEntry directory,
        string name,
        out DirectoryEntry entry)
    {
        if (!directory.IsDirectory ||
            directory.Size <= 0 ||
            directory.Size > MaximumDirectorySize)
        {
            entry = default;
            return false;
        }

        byte[] bytes = ReadExtent(
            disc,
            directory.Extent,
            directory.Size);
        int offset = 0;
        while (offset < bytes.Length)
        {
            int recordLength = bytes[offset];
            if (recordLength == 0)
            {
                offset = ((offset / SectorSize) + 1) * SectorSize;
                continue;
            }

            if (offset + recordLength > bytes.Length ||
                !TryParseEntry(bytes, offset, out DirectoryEntry candidate))
            {
                break;
            }

            if (NamesEqual(candidate.Name, name))
            {
                entry = candidate;
                return true;
            }

            offset += recordLength;
        }

        entry = default;
        return false;
    }

    private static bool TryParseEntry(
        ReadOnlySpan<byte> bytes,
        int offset,
        out DirectoryEntry entry)
    {
        entry = default;
        if ((uint)offset >= bytes.Length)
            return false;

        int recordLength = bytes[offset];
        if (recordLength < 34 || offset + recordLength > bytes.Length)
            return false;

        ReadOnlySpan<byte> record = bytes.Slice(offset, recordLength);
        int nameLength = record[32];
        if (33 + nameLength > record.Length)
            return false;

        string name = nameLength == 1 && record[33] <= 1
            ? record[33] == 0 ? "." : ".."
            : Encoding.ASCII.GetString(record.Slice(33, nameLength));
        entry = new DirectoryEntry(
            checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                record.Slice(2, 4))),
            checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                record.Slice(10, 4))),
            (record[25] & 2) != 0,
            name);
        return entry.Extent >= 0 && entry.Size >= 0;
    }

    private static byte[] ReadExtent(
        DiscImage disc,
        int extent,
        int size)
    {
        if (extent < 0 ||
            size < 0 ||
            extent >= disc.SectorCount ||
            (long)extent + Math.Max(1, (size + SectorSize - 1) / SectorSize) >
                disc.SectorCount)
        {
            throw new InvalidDataException(
                "A extensão ISO9660 aponta para fora da imagem.");
        }

        byte[] result = new byte[size];
        var sector = new byte[SectorSize];
        int copied = 0;
        while (copied < size)
        {
            disc.ReadUserDataSector(
                extent + copied / SectorSize,
                sector);
            int count = Math.Min(SectorSize, size - copied);
            sector.AsSpan(0, count).CopyTo(result.AsSpan(copied));
            copied += count;
        }

        return result;
    }

    private static string? ParseBootPath(ReadOnlySpan<byte> configBytes)
    {
        string config = Encoding.ASCII.GetString(configBytes);
        foreach (string line in config.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = line.IndexOf('=');
            if (separator < 0 ||
                !line[..separator].Trim().Equals(
                    "BOOT",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = line[(separator + 1)..].Trim().Trim('"');
            if (value.StartsWith("cdrom:", StringComparison.OrdinalIgnoreCase))
                value = value[6..];

            value = value.TrimStart('\\', '/').Trim();
            int argumentSeparator = value.IndexOfAny([' ', '\t']);
            if (argumentSeparator >= 0)
                value = value[..argumentSeparator];
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    private static bool NamesEqual(string isoName, string requestedName) =>
        StripVersion(isoName).Equals(
            StripVersion(requestedName),
            StringComparison.OrdinalIgnoreCase);

    private static string StripVersion(string value)
    {
        int separator = value.LastIndexOf(';');
        return separator >= 0 ? value[..separator] : value;
    }

    private readonly record struct DirectoryEntry(
        int Extent,
        int Size,
        bool IsDirectory,
        string Name);
}
