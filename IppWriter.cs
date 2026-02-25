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

    /// Записывает булев атрибут (tag 0x22). Размер значения строго 1 байт по RFC 8010!
    public void WriteBooleanAttribute(string name, bool value)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        _bw.Write((byte)0x22); // ValueTagBoolean
        WriteShort((short)nameBytes.Length);
        _bw.Write(nameBytes);
        WriteShort(1); // boolean всегда 1 байт в IPP
        _bw.Write((byte)(value ? 1 : 0));
    }

    /// Записывает атрибут типа rangeOfInteger (tag 0x33): lower + upper, 8 байт итого.
    /// Используется для copies-supported и подобных диапазонных атрибутов.
    public void WriteRangeAttribute(byte valueTag, string name, int lower, int upper)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        _bw.Write(valueTag);
        WriteShort((short)nameBytes.Length);
        _bw.Write(nameBytes);
        WriteShort(8); // 2 x 4 байта
        WriteInt(lower);
        WriteInt(upper);
    }

    public byte[] ToArray()
    {
        _bw.Flush();
        return _ms.ToArray();
    }
}