using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Xunit;
namespace BookStitch.Tests.Ui;
public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData("Black", "WarmSand")]
    [InlineData("Ivory", "Ink")]
    public void Default_vector_arrows_follow_palette_and_use_petrol_for_keyboard_focus(string background, string text)
    {
        RunOnStaThread(() =>
        {
            var panel = new StackPanel();
            AppearanceSettingsService.ApplyToResources(panel.Resources, new AppSettings { BackgroundPreset = background, TextColorPreset = text });
            var decoy = new TextBox(); panel.Children.Add(decoy);
            var button = new Button { Style = (Style)Application.Current.FindResource("TrackOrderButtonStyle"), Width = 35, Height = 35 };
            var icon = new TrackOrderIcon { Width = 32, Height = 32 };
            icon.SetBinding(Control.ForegroundProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
            button.Content = icon; panel.Children.Add(button);
            var window = new Window { Content = panel, Width = 200, Height = 160, Left = -2000, Top = -2000, ShowInTaskbar = false };
            try
            {
                window.Show(); decoy.Focus(); Keyboard.Focus(decoy); DrainDispatcher();
                Assert.True(icon.VectorMode);
                Assert.Same(button.FindResource("MainTextBrush"), button.Foreground);
                Assert.Same(button.Foreground, icon.Foreground);
                button.Focus(); Keyboard.Focus(button); DrainDispatcher();
                Assert.True(button.IsKeyboardFocusWithin);
                Assert.Same(button.FindResource("AccentBrush"), button.Foreground);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(32, 32, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(icon);
                var pixels = new byte[32 * 32 * 4]; bitmap.CopyPixels(pixels, 128, 0);
                var expected = Assert.IsType<System.Windows.Media.SolidColorBrush>(button.Foreground).Color;
                var opaque = Enumerable.Range(0, 32 * 32).Where(i => pixels[i * 4 + 3] == 255).ToArray();
                Assert.NotEmpty(opaque);
                Assert.All(opaque, i => { Assert.Equal(expected.B, pixels[i * 4]); Assert.Equal(expected.G, pixels[i * 4 + 1]); Assert.Equal(expected.R, pixels[i * 4 + 2]); });

                Assert.Same(button.Foreground, icon.Foreground);
                decoy.Focus(); Keyboard.Focus(decoy); DrainDispatcher();
                Assert.Same(button.FindResource("MainTextBrush"), icon.Foreground);
                var trigger = button.Style.Triggers.OfType<Trigger>().Single(t => t.Property == UIElement.IsMouseOverProperty);
                Assert.Equal(true, trigger.Value);
                var setter = Assert.IsType<Setter>(Assert.Single(trigger.Setters.Cast<SetterBase>()));
                Assert.Equal(Control.ForegroundProperty, setter.Property);
                Assert.Equal("AccentBrush", Assert.IsType<DynamicResourceExtension>(setter.Value).ResourceKey);
            }
            finally { window.Close(); }
        });
    }
}
