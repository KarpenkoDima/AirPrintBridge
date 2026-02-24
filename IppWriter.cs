// IppWriter.cs
namespace AirPrintBridge;

/// <summary>
/// Построитель бинарного IPP пакета.
/// IPP — это бинарный TLV (Tag-Length-Value) формат поверх HTTP.
/// Каждый атрибут кодируется как: [1 byte tag][2 bytes name-length][name][2 bytes value-length][value]
/// </summary>
public class IppWriter
{
    private readonly MemoryStream _ms = new();
    private readonly BinaryWriter _bw;

    public IppWriter()
    {
        // IPP использует big-endian порядок байт (network byte order)
        _bw = new BinaryWriter(_ms);
    }

    public void WriteVersion(short version) => WriteShort(version);

    public void WriteShort(short value)
    {
        _bw.Write((byte)(value >> 8));
        _bw.Write((byte)(value & 0xFF));
    }

    public void WriteInt(int value)
    {
        _bw.Write((byte)((value >> 24) & 0xFF));
        _bw.Write((byte)((value >> 16) & 0xFF));
        _bw.Write((byte)((value >> 8) & 0xFF));
        _bw.Write((byte)(value & 0xFF));
    }
    public void Write(byte[] data) => _bw.Write(data);
    public void WriteByte(byte value) => _bw.Write(value);

    /// Записывает строковый атрибут: tag + name + value
    public void WriteAttribute(byte valueTag, string name, string value)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var valueBytes = System.Text.Encoding.UTF8.GetBytes(value);

        _bw.Write(valueTag);
        WriteShort((short)nameBytes.Length);
        _bw.Write(nameBytes);
        WriteShort((short)valueBytes.Length);
        _bw.Write(valueBytes);
    }

    /// Дополнительное значение того же атрибута (без повторения имени — name-length = 0)
    /// IPP позволяет multi-value атрибуты именно так
    public void WriteAttributeAdditional(byte valueTag, string value)
    {
        var valueBytes = System.Text.Encoding.UTF8.GetBytes(value);
        _bw.Write(valueTag);
        WriteShort(0);           // name-length = 0 означает "то же имя что и предыдущий"
        WriteShort((short)valueBytes.Length);
        _bw.Write(valueBytes);
    }

    /// Записывает целочисленный атрибут (integer, enum, boolean)
    public void WriteIntAttribute(string name, byte valueTag, int value)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        _bw.Write(valueTag);
        WriteShort((short)nameBytes.Length);
        _bw.Write(nameBytes);
        WriteShort(4); // int всегда 4 байта в IPP
        WriteInt(value);
    }

    /// Дополнительное целочисленное значение (multi-value)
    public void WriteIntAttributeAdditional(byte valueTag, int value)
    {
        _bw.Write(valueTag);
        WriteShort(0);
        WriteShort(4);
        WriteInt(value);
    }

    public byte[] ToArray()
    {
        _bw.Flush();
        return _ms.ToArray();
    }
}