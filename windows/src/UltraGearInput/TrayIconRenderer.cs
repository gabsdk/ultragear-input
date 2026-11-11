using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using Microsoft.Win32;
using UltraGearInput.Native;

namespace UltraGearInput;

[SupportedOSPlatform("windows")]
internal sealed class TrayIconRenderer : IDisposable
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private nint handle;

    public Icon Render()
    {
        Release();
        var size = SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            Draw(graphics, size, UsesLightTaskbar() ? Color.Black : Color.White);
        }
        handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    public void Dispose() => Release();

    private static void Draw(Graphics graphics, float size, Color color)
    {
        var stroke = Math.Max(1f, size / 12f);
        using var pen = new Pen(color, stroke) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        var screen = new RectangleF(size * 0.08f, size * 0.14f, size * 0.84f, size * 0.56f);
        graphics.DrawRectangle(pen, screen.X, screen.Y, screen.Width, screen.Height);
        var center = size / 2f;
        graphics.DrawLine(pen, center, screen.Bottom, center, size * 0.84f);
        graphics.DrawLine(pen, size * 0.3f, size * 0.86f, size * 0.7f, size * 0.86f);
    }

    private static bool UsesLightTaskbar()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    private void Release()
    {
        if (handle != 0)
        {
            Win32Icons.Destroy(handle);
            handle = 0;
        }
    }
}
