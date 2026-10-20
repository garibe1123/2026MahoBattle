#if UNITY_EDITOR
using UnityEngine;

internal static class ElementalFireFrames
{
    public static Texture2D CreateTexture(int cell)
    {
        Texture2D texture = new(cell * 4, cell * 4, TextureFormat.RGBA32, false)
        {
            name = "Fire Timing Sheet",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        Color32[] pixels = new Color32[texture.width * texture.height];
        for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                float dx = (x % cell + 0.5f) / cell * 2f - 1f;
                float dy = (y % cell + 0.5f) / cell * 2f - 1f;
                pixels[y * texture.width + x] = dx * dx + dy * dy < 0.5f
                    ? new Color32(255, 255, 255, 255) : default;
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}
#endif
