using System.Text;

namespace LSLib.Rcon;

public enum DosPacketId : byte
{
    DosUnknown87 = 0x87,
    DosSendConsoleCommand = 0x88,
    DosDisconnectConsole = 0x89,
    DosConsoleResponse = 0x8A,
    DosEnumerationList = 0x8B
};

public class DosUnknown87 : IPacket
{
    public void Read(BinaryReaderBE Reader)
    {
    }

    public void Write(BinaryWriterBE Writer)
    {
        throw new NotImplementedException();
    }
}

public class DosEnumeration
{
    public string Name { get; set; } = string.Empty;
    public byte Type { get; set; }
    public List<string> Values { get; set; } = [];
}

public class DosEnumerationList : IPacket
{
    public List<DosEnumeration> Enumerations { get; set; } = [];

    private static string ReadString(BinaryReaderBE reader)
    {
        int length = reader.ReadInt32();
        if (length <= 0) return string.Empty;

        ReadOnlySpan<byte> strBytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(strBytes);
    }

    public void Read(BinaryReaderBE reader)
    {
        uint numEnums = reader.ReadUInt32();
        Enumerations = new List<DosEnumeration>((int)numEnums);
        for (var i = 0; i < numEnums; i++)
        {
            var enumeration = new DosEnumeration
            {
                Name = ReadString(reader),
                Type = reader.ReadByte(),
            };

            var numElems = reader.ReadUInt32();
            enumeration.Values = new List<string>((int)numElems);

            for (var j = 0; j < numElems; j++)
            {
                enumeration.Values.Add(ReadString(reader));
            }

            Enumerations.Add(enumeration);
        }
    }

    public void Write(BinaryWriterBE writer)
    {
        throw new NotImplementedException();
    }
}

public class DosDisconnectConsole : IPacket
{
    public void Read(BinaryReaderBE reader)
    {
        throw new NotImplementedException();
    }

    public void Write(BinaryWriterBE writer)
    {
        writer.Write((Byte)DosPacketId.DosDisconnectConsole);
        byte[] pkt =
        [
            0x00, 0x00, 0x00, 0x60,
            0x00, 0x08, 0x0A, 0x00,
            0x00, 0x09, 0x00, 0x00,
            0x00, 0x15
        ];
        writer.Write(pkt);
    }
}

public class DosSendConsoleCommand : IPacket
{
    public string Command { get; set; } = string.Empty;
    public string[] Arguments { get; set; } = [];

    public void Read(BinaryReaderBE reader)
    {
        throw new NotImplementedException();
    }

    public void Write(BinaryWriterBE writer)
    {
        writer.Write((byte)DosPacketId.DosSendConsoleCommand);
        writer.Write((uint)1);

        byte[] cmd = Encoding.UTF8.GetBytes(Command);
        writer.Write((uint)cmd.Length);
        writer.Write(cmd);
        writer.Write((byte)0);

        if (Arguments is null || Arguments.Length == 0)
        {
            writer.Write((uint)0);
        }
        else
        {
            writer.Write((uint)Arguments.Length);
            foreach (string t in Arguments)
            {
                byte[] arg = Encoding.UTF8.GetBytes(t);
                writer.Write((uint)arg.Length);
                writer.Write(arg);
                writer.Write((byte)0);
            }
        }

        writer.Write((ushort)0);
    }
}

public class DosConsoleResponse : IPacket
{
    public class ConsoleLine
    {
        public string Line { get; set; } = string.Empty;
        public uint Level { get; set; }
    }

    public ConsoleLine[] Lines { get; set; } = [];

    public void Read(BinaryReaderBE reader)
    {
        uint linesCount = reader.ReadUInt32();
        Lines = new ConsoleLine[linesCount];

        for (int i = 0; i < linesCount; i++)
        {
            uint length = reader.ReadUInt32();
            _ = reader.ReadByte();
            _ = reader.ReadUInt32();

            ReadOnlySpan<byte> lineBytes = reader.ReadBytes((int)length);

            Lines[i] = new ConsoleLine
            {
                Level = reader.ReadUInt32(),
                Line = Encoding.UTF8.GetString(lineBytes)
            };
        }
    }

    public void Write(BinaryWriterBE writer)
    {
        throw new NotImplementedException();
    }
}
