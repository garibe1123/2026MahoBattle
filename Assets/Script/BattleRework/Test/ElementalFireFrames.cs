#if UNITY_EDITOR
using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

internal static class ElementalFireFrames
{
    private const int Size = 384;
    private const int PaletteSize = 256;

    public static Texture2D CreateTexture()
    {
        using var packed = new MemoryStream(Convert.FromBase64String(PixelData));
        using var stream = new DeflateStream(packed, CompressionMode.Decompress);
        using var reader = new BinaryReader(stream);
        Color32[] palette = new Color32[PaletteSize];
        for (int i = 0; i < palette.Length; i++)
            palette[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        Color32[] pixels = new Color32[Size * Size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = palette[reader.ReadByte()];
        Texture2D texture = new(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "Fire Smoke Reference Frames",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    // Packed palette and indexed pixels keep the authored sheet available without an imported image asset.
    private const string PixelData =
        "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";
}
#endif
