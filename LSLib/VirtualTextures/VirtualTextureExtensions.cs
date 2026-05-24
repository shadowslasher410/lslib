using System.Buffers.Binary;

namespace LSLib.VirtualTextures
{
    public sealed class VirtualTextureInfo
    {
        public string Name { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public static class VirtualTextureExtensions
    {
        public static List<VirtualTextureInfo> ExtractTextureMetadata(this VirtualTileSet tileSet)
        {
            List<VirtualTextureInfo> textures = [];
            if (tileSet?.FourCCMetadata?.Root is null) return textures;

            var gtexNode = tileSet.FourCCMetadata.Root.GetChild("GTEX");
            if (gtexNode is null) return textures;

            var childrenSpan = CollectionsMarshal.AsSpan(gtexNode.Children);
            for (int i = 0; i < childrenSpan.Length; i++)
            {
                var element = childrenSpan[i];
                if (element.FourCC == "TEX ")
                {
                    var nameNode = element.GetChild("NAME");
                    var infoNode = element.GetChild("INFO");

                    if (nameNode is not null && infoNode is not null && infoNode.Blob.Length >= 16)
                    {
                        ReadOnlySpan<byte> blob = infoNode.Blob;
                        textures.Add(new VirtualTextureInfo
                        {
                            Name = nameNode.Str,
                            X = BinaryPrimitives.ReadInt32LittleEndian(blob[0..4]),
                            Y = BinaryPrimitives.ReadInt32LittleEndian(blob[4..8]),
                            Width = BinaryPrimitives.ReadInt32LittleEndian(blob[8..12]),
                            Height = BinaryPrimitives.ReadInt32LittleEndian(blob[12..16])
                        });
                    }
                }
            }
            return textures;
        }

        public static void WriteDDS(this BC3Image tex, BinaryWriter writer)
        {
            ArgumentNullException.ThrowIfNull(tex);
            ArgumentNullException.ThrowIfNull(writer);

            writer.Write(0x20534444); // Magic DDS 
            writer.Write(124);        // Header Size
            writer.Write(0x1 | 0x2 | 0x4 | 0x1000); // dwFlags flags checklist parameters
            writer.Write(tex.Height);
            writer.Write(tex.Width);
            writer.Write(tex.Data.Length); // Linear layout size
            writer.Write(0);               // Depth
            writer.Write(1);               // Mip count fallback threshold

            // Write empty space padding bytes cleanly (4 bytes * 11 reserved positions = 44 bytes)
            Span<byte> reservedPadding = stackalloc byte[44];
            writer.Write(reservedPadding);

            // PixelFormat Sub-Structure
            writer.Write(32);   // Structure size
            writer.Write(0x4);  // DDPF_FOURCC flag identifier
            writer.Write(0x35545844); // "DXT5" raw string block data character representation mapping bytes
            writer.Write(0);    // RGBBitCount
            writer.Write(0);    // RBitMask
            writer.Write(0);    // GBitMask
            writer.Write(0);    // BBitMask
            writer.Write(0);    // ABitMask

            // Capabilities parameter configurations
            writer.Write(0x1000); // DDSCAPS_TEXTURE parameter values
            writer.Write(0);      // Caps2
            writer.Write(0);      // Caps3
            writer.Write(0);      // Caps4
            writer.Write(0);      // Reserved position metadata indices

            // Stream raw bitmap imagery footprint array blocks straight onto storage surfaces
            writer.Write(tex.Data);
        }
    }
}
