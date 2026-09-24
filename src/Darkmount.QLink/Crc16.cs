namespace Darkmount.QLink;

public static class Crc16
{
    /// <summary>CRC-16/MODBUS (poly 0xA001 reflected, init 0xFFFF, no final xor), as used by QLink.</summary>
    public static ushort Modbus(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }
}
