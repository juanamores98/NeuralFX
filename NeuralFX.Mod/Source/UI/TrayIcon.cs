using UnityEngine;

namespace NeuralFX.UI
{
    internal static class TrayIcon
    {
        internal static Texture2D Make()
        {
            const int size = 48;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                name = "NeuralFX.tray"
            };

            var center = new Vector2((size - 1) / 2f, (size - 1) / 2f);
            Color32 discColor = new Color32(24, 28, 36, 255);
            Color32 borderEmerald = new Color32(78, 201, 176, 255);
            Color32 accentCyan = new Color32(30, 150, 230, 255);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    Color c = new Color(0f, 0f, 0f, 0f);

                    if (distance <= 22f)
                    {
                        c = discColor;
                    }

                    if (distance >= 20.5f && distance <= 22f)
                    {
                        c = borderEmerald;
                    }

                    // Draw stylized "N" for NeuralFX
                    // Left vertical column: x in [14, 18], y in [12, 36]
                    // Right vertical column: x in [30, 34], y in [12, 36]
                    // Diagonal line: connecting (14, 36) to (34, 12)
                    if (distance < 20f)
                    {
                        bool leftCol = (x >= 14 && x <= 18 && y >= 12 && y <= 36);
                        bool rightCol = (x >= 30 && x <= 34 && y >= 12 && y <= 36);

                        // Diagonal: y = 36 - (x - 14) * 1.2
                        float expectedY = 36f - (x - 14f) * 1.2f;
                        bool diagonal = (x >= 14 && x <= 34 && Mathf.Abs(y - expectedY) <= 2.2f);

                        if (leftCol || rightCol || diagonal)
                        {
                            // Gradient from Emerald to Cyan
                            float t = (float)(x - 14) / 20f;
                            c = Color.Lerp(borderEmerald, accentCyan, t);
                        }
                    }

                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply(false, true);
            return tex;
        }
    }
}
