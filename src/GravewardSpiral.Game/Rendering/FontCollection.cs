using System.Buffers.Binary;

namespace GravewardSpiral.Game.Rendering;

/// <summary>
/// .ttc(複数のフォントをまとめたファイル)から、1つのフォントを .ttf の形で取り出す。
/// FontStashSharp は .ttc を読めないので、Windows のフォントを使うときに通す。
/// </summary>
public static class FontCollection
{
    public static byte[] ToSingleFont(byte[] data, int index = 0)
    {
        if (data.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(data) != 0x74746366) // 'ttcf'
            return data;

        uint count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        int dir = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12 + index * 4));

        int numTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(dir + 4));
        int headerSize = 12 + numTables * 16;

        var tables = new (int recordAt, int offset, int length)[numTables];
        int total = headerSize;
        for (int i = 0; i < numTables; i++)
        {
            int rec = dir + 12 + i * 16;
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 12));
            tables[i] = (rec, offset, length);
            total += (length + 3) & ~3;
        }

        var result = new byte[total];
        data.AsSpan(dir, headerSize).CopyTo(result);
        int write = headerSize;
        for (int i = 0; i < numTables; i++)
        {
            var (_, offset, length) = tables[i];
            data.AsSpan(offset, length).CopyTo(result.AsSpan(write));
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12 + i * 16 + 8), (uint)write);
            write += (length + 3) & ~3;
        }
        return result;
    }
}
