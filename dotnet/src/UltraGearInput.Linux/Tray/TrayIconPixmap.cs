namespace UltraGearInput.Linux.Tray;

internal static class TrayIconPixmap
{
    public static readonly int[] Sizes = [22, 44];

    private const uint Color = 0xFFBEBEBE;

    public static IReadOnlyList<object> All() =>
        [.. Sizes.Select(size => (object)new object[] { size, size, Render(size) })];

    public static byte[] Render(int size)
    {
        var pixels = new uint[size * size];
        var stroke = Math.Max(1, size / 11);
        var left = size / 11;
        var right = size - 1 - left;
        var top = size * 3 / 22;
        var bottom = size * 15 / 22;
        var center = size / 2;
        var standWidth = Math.Max(1, stroke);
        var baseTop = size * 18 / 22;

        void Fill(int x0, int y0, int x1, int y1)
        {
            for (var y = Math.Max(0, y0); y <= Math.Min(size - 1, y1); y++)
            {
                for (var x = Math.Max(0, x0); x <= Math.Min(size - 1, x1); x++)
                {
                    pixels[(y * size) + x] = Color;
                }
            }
        }

        Fill(left, top, right, top + stroke - 1);
        Fill(left, bottom - stroke + 1, right, bottom);
        Fill(left, top, left + stroke - 1, bottom);
        Fill(right - stroke + 1, top, right, bottom);
        Fill(center - (standWidth / 2), bottom, center - (standWidth / 2) + standWidth - 1, baseTop);
        Fill(size * 6 / 22, baseTop, size - 1 - (size * 6 / 22), baseTop + stroke - 1);

        var bytes = new byte[pixels.Length * 4];
        for (var index = 0; index < pixels.Length; index++)
        {
            var pixel = pixels[index];
            bytes[index * 4] = (byte)(pixel >> 24);
            bytes[(index * 4) + 1] = (byte)(pixel >> 16);
            bytes[(index * 4) + 2] = (byte)(pixel >> 8);
            bytes[(index * 4) + 3] = (byte)pixel;
        }
        return bytes;
    }
}
