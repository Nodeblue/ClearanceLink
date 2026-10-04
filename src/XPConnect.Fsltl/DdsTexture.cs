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
    /// <param name="opaque">
    /// Make every texel fully opaque. MSFS ignores the albedo alpha of an opaque material and FSLTL liveries often
    /// leave it low or zero, which X-Plane's alpha test cuts out (a CRJ700 without wings, a 747 with only its fin).
    /// </param>
    public byte[] ToXPlane(int maxSize, bool opaque = false)
    {
        var level = 0;
        int w = Width, h = Height;
        while ((w > maxSize || h > maxSize) && level + 1 < Mips.Count)
        {
            level++;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        // A DXT texture with its full mip chain just drops its top levels. Others (BC7, or DXT shipped without mips,
        // like FSLTL's SF34 base livery, which X-Plane then draws as its background colour) are decoded, halved down
        // to size and re-encoded with a full chain.
        var fullChain = Mips.Count >= MipCount(Width, Height);
        if (Format is DdsFormat.Dxt1 or DdsFormat.Dxt3 or DdsFormat.Dxt5 && fullChain)
        {
            var kept = Mips.Skip(level).ToList();
            if (opaque && Format is DdsFormat.Dxt3 or DdsFormat.Dxt5) kept = [.. kept.Select(OpaqueAlpha)];
            return Write(Format, w, h, kept);
        }

        var source = Format switch
        {
            DdsFormat.Dxt1 => CompressionFormat.Bc1WithAlpha,
            DdsFormat.Dxt3 => CompressionFormat.Bc2,
            DdsFormat.Dxt5 => CompressionFormat.Bc3,
            DdsFormat.Bc7 => CompressionFormat.Bc7,
            _ => throw new NotSupportedException($"DDS format {Format} is not converted"),
        };
        var decoded = new BcDecoder().DecodeRaw(Mips[level], w, h, source);
        var bytes = new byte[decoded.Length * 4];
        for (var i = 0; i < decoded.Length; i++)
        {
            bytes[i * 4] = decoded[i].r;
            bytes[i * 4 + 1] = decoded[i].g;
            bytes[i * 4 + 2] = decoded[i].b;
            bytes[i * 4 + 3] = opaque ? (byte)255 : decoded[i].a;
        }

        while ((w > maxSize || h > maxSize) && w > 1 && h > 1) (bytes, w, h) = Halve(bytes, w, h);
        var encoder = new BcEncoder(CompressionFormat.Bc3);
        encoder.OutputOptions.GenerateMipMaps = true;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        var mips = encoder.EncodeToRawBytes(bytes, w, h, PixelFormat.Rgba32);
        return Write(DdsFormat.Dxt5, w, h, [.. mips]);
    }

    /// <summary>
    /// A DXT3/DXT5 mip level with every block's alpha set to 255: both store alpha in the first 8 bytes of each
    /// 16-byte block (DXT3 explicit 4-bit values, DXT5 two endpoints and indices; 255, 255 and index 0 is opaque).
    /// </summary>
    private byte[] OpaqueAlpha(byte[] level)
    {
        var copy = (byte[])level.Clone();
        for (var block = 0; block + 16 <= copy.Length; block += 16)
        {
            if (Format == DdsFormat.Dxt3)
            {
                Array.Fill(copy, (byte)0xFF, block, 8);
            }
            else
            {
                copy[block] = 0xFF;
                copy[block + 1] = 0xFF;
                Array.Clear(copy, block + 2, 6);
            }
        }

        return copy;
    }

    /// <summary>Levels in a full mip chain down to 1x1.</summary>
    public static int MipCount(int width, int height) => 1 + (int)Math.Floor(Math.Log2(Math.Max(1, Math.Max(width, height))));

    /// <summary>Halves an RGBA image by averaging each 2x2 block.</summary>
    private static (byte[] Pixels, int Width, int Height) Halve(byte[] src, int w, int h)
    {
        int nw = w / 2, nh = h / 2;
        var dst = new byte[nw * nh * 4];
        for (var y = 0; y < nh; y++)
        {
            for (var x = 0; x < nw; x++)
            {
                for (var c = 0; c < 4; c++)
                {
                    int At(int sx, int sy) => src[((sy * w) + sx) * 4 + c];
                    dst[((y * nw) + x) * 4 + c] = (byte)((At(2 * x, 2 * y) + At(2 * x + 1, 2 * y) + At(2 * x, 2 * y + 1) + At(2 * x + 1, 2 * y + 1) + 2) / 4);
                }
            }
        }

        return (dst, nw, nh);
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
