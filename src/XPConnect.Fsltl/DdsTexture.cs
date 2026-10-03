using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace XPConnect.Fsltl;

public enum DdsFormat
{
    Unknown,
    Dxt1,
    Dxt3,
    Dxt5,
    Bc7,
    Bc5,
}

/// <summary>A block-compressed DDS texture with its full mip chain, as MSFS ships them.</summary>
public sealed class DdsTexture
{
    private const int Magic = 0x20534444;  // "DDS "

    public required DdsFormat Format { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Each mip level's compressed bytes, largest first.</summary>
    public required List<byte[]> Mips { get; init; }

    public static DdsFormat ReadFormat(string path)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[148];
        return stream.Read(header, 0, header.Length) >= 128 ? Header(header).Format : DdsFormat.Unknown;
    }

    public static DdsTexture Load(string path) => Parse(File.ReadAllBytes(path));

    public static DdsTexture Parse(byte[] bytes)
    {
        if (bytes.Length < 128 || BitConverter.ToInt32(bytes, 0) != Magic) throw new InvalidDataException("not a DDS file");
        var (format, width, height, mipCount, dataStart) = Header(bytes);
        var mips = new List<byte[]>();
        var offset = dataStart;
        int w = width, h = height;
        for (var level = 0; level < Math.Max(1, mipCount) && format != DdsFormat.Unknown; level++)
        {
            var size = LevelSize(format, w, h);
            if (offset + size > bytes.Length) break;
            mips.Add(bytes.AsSpan(offset, size).ToArray());
            offset += size;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        return new DdsTexture { Format = format, Width = width, Height = height, Mips = mips };
    }

    private static (DdsFormat Format, int Width, int Height, int MipCount, int DataStart) Header(byte[] b)
    {
        var height = BitConverter.ToInt32(b, 12);
        var width = BitConverter.ToInt32(b, 16);
        var mipCount = BitConverter.ToInt32(b, 28);
        var fourCc = System.Text.Encoding.ASCII.GetString(b, 84, 4);
        var format = fourCc switch
        {
            "DXT1" => DdsFormat.Dxt1,
            "DXT2" or "DXT3" => DdsFormat.Dxt3,
            "DXT4" or "DXT5" => DdsFormat.Dxt5,
            "ATI2" or "BC5U" or "BC5S" => DdsFormat.Bc5,
            "DX10" when b.Length >= 148 => BitConverter.ToInt32(b, 128) switch
            {
                70 or 71 or 72 => DdsFormat.Dxt1,
                73 or 74 or 75 => DdsFormat.Dxt3,
                76 or 77 or 78 => DdsFormat.Dxt5,
                82 or 83 or 84 => DdsFormat.Bc5,
                97 or 98 or 99 => DdsFormat.Bc7,
                _ => DdsFormat.Unknown,
            },
            _ => DdsFormat.Unknown,
        };
        return (format, width, height, mipCount, fourCc == "DX10" ? 148 : 128);
    }

    public static int LevelSize(DdsFormat format, int width, int height) =>
        Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * (format == DdsFormat.Dxt1 ? 8 : 16);

    /// <summary>
    /// This texture as an X-Plane-friendly DDS (DXT1 or DXT5) no larger than <paramref name="maxSize"/>. DXT textures
    /// just drop their top mip levels; BC7 is decoded at the right level and re-encoded as DXT5.
    /// </summary>
    public byte[] ToXPlane(int maxSize)
    {
        var level = 0;
        int w = Width, h = Height;
        while ((w > maxSize || h > maxSize) && level + 1 < Mips.Count)
        {
            level++;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        switch (Format)
        {
            case DdsFormat.Dxt1:
            case DdsFormat.Dxt3:
            case DdsFormat.Dxt5:
                return Write(Format, w, h, Mips.Skip(level).ToList());
            case DdsFormat.Bc7:
                var pixels = new BcDecoder().DecodeRaw(Mips[level], w, h, CompressionFormat.Bc7);
                var encoder = new BcEncoder(CompressionFormat.Bc3);
                encoder.OutputOptions.GenerateMipMaps = true;
                encoder.OutputOptions.Quality = CompressionQuality.Balanced;
                var bytes = new byte[pixels.Length * 4];
                for (var i = 0; i < pixels.Length; i++)
                {
                    bytes[i * 4] = pixels[i].r;
                    bytes[i * 4 + 1] = pixels[i].g;
                    bytes[i * 4 + 2] = pixels[i].b;
                    bytes[i * 4 + 3] = pixels[i].a;
                }

                var mips = encoder.EncodeToRawBytes(bytes, w, h, PixelFormat.Rgba32);
                return Write(DdsFormat.Dxt5, w, h, [.. mips]);
            default:
                throw new NotSupportedException($"DDS format {Format} is not converted");
        }
    }

    /// <summary>A classic (non-DX10) DDS file: 128-byte header, then the mip levels.</summary>
    public static byte[] Write(DdsFormat format, int width, int height, IReadOnlyList<byte[]> mips)
    {
        var fourCc = format switch
        {
            DdsFormat.Dxt1 => "DXT1",
            DdsFormat.Dxt3 => "DXT3",
            DdsFormat.Dxt5 => "DXT5",
            _ => throw new NotSupportedException($"cannot write {format}"),
        };
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Magic);
        w.Write(124);                                         // header size
        w.Write(0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000); // caps, height, width, pixel format, mip count, linear size
        w.Write(height);
        w.Write(width);
        w.Write(mips.Count > 0 ? mips[0].Length : 0);
        w.Write(0);                                           // depth
        w.Write(mips.Count);
        for (var i = 0; i < 11; i++) w.Write(0);
        w.Write(32);                                          // pixel format size
        w.Write(0x4);                                         // DDPF_FOURCC
        w.Write(System.Text.Encoding.ASCII.GetBytes(fourCc));
        for (var i = 0; i < 5; i++) w.Write(0);
        w.Write(0x1000 | 0x8 | 0x400000);                     // texture, complex, mipmap
        for (var i = 0; i < 4; i++) w.Write(0);
        foreach (var mip in mips) w.Write(mip);
        w.Flush();
        return ms.ToArray();
    }
}
