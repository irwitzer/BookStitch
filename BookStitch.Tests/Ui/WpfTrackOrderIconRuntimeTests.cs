using BookStitch.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData(TrackOrderDirection.Up)]
    [InlineData(TrackOrderDirection.Down)]
    [InlineData(TrackOrderDirection.Top)]
    [InlineData(TrackOrderDirection.Bottom)]
    public void TrackOrderVariants_RenderInBothThemeColorsWithTransparentCorners(TrackOrderDirection direction)
    {
        RunOnStaThread(() =>
        {
            var icon = new TrackOrderIcon { Width = 32, Height = 32, Direction = direction };
            foreach (var theme in new[] { "Dark", "Light" })
            foreach (var vector in new[] { false, true })
            {
                icon.Theme = theme;
                icon.VectorMode = vector;
                var color = (Color)ColorConverter.ConvertFromString(theme == "Dark" ? "#126F73" : "#326B68");
                icon.Foreground = new SolidColorBrush(color);
                icon.Measure(new Size(32, 32)); icon.Arrange(new Rect(0, 0, 32, 32)); icon.UpdateLayout();
                var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(icon);
                var pixels = new byte[32 * 32 * 4]; bitmap.CopyPixels(pixels, 128, 0);
                Assert.Equal(0, pixels[3]);
                var solid = 0; var antialiased = 0;
                for (var index = 0; index < pixels.Length; index += 4)
                {
                    if (pixels[index + 3] == 255)
                    {
                        solid++;
                        Assert.Equal(color.B, pixels[index]); Assert.Equal(color.G, pixels[index + 1]); Assert.Equal(color.R, pixels[index + 2]);
                    }
                    else if (pixels[index + 3] > 0) antialiased++;
                }
                Assert.True(solid > 20, $"{theme} vector={vector}: missing opaque geometry");
                Assert.True(antialiased > 20, $"{theme} vector={vector}: missing edge smoothing");
            }
        });
    }
}
