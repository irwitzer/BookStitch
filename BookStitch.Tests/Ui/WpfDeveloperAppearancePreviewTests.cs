using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Presentation;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Live_cover_override_preserves_real_cover_file_and_project_metadata()
    {
        RunOnStaThread(() =>
        {
            using var folder = new TemporaryFolder();
            var coverPath = System.IO.Path.Combine(folder.Path, "actual-cover.jpg");
            var bitmap = BitmapSource.Create(16, 16, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, new byte[16 * 16 * 3], 16 * 3);
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = System.IO.File.Create(coverPath)) encoder.Save(stream);
            var originalBytes = System.IO.File.ReadAllBytes(coverPath);
            var access = DeveloperToolsCommands.AccessState;
            access.Initialize(new AppSettings { DeveloperToolsEnabled = true }, BookStitchEnvironment.Production, () => true);
            var preview = DeveloperToolsCommands.AppearancePreview;
            preview.Clear();
            var state = new ProjectMetadataState();
            state.ApplyCoverMetadata(coverPath, coverPath, MetadataOrigin.User);
            state.CoverPreviewSource = coverPath;
            var regular = new AppSettings { BackgroundPreset = "Black" };
            var dialog = CreateMetadataDialog(state, new MetadataPreferences(), appSettings: regular);
            try
            {
                preview.SetImage(AppearanceAssetRole.CoverPlaceholder, "CoverPlaceholder.Light");
                preview.SetBackground("Ivory");
                Assert.Equal(coverPath, dialog.CoverPreviewSource);
                Assert.Equal(coverPath, state.ProcessedCoverPath);
                Assert.Equal(coverPath, state.CoverPreviewSource);
                Assert.Equal(originalBytes, System.IO.File.ReadAllBytes(coverPath));
                access.VisibilityChoice = DeveloperToolsVisibilityChoice.Hide;
                Assert.Equal(coverPath, dialog.CoverPreviewSource);
                access.VisibilityChoice = DeveloperToolsVisibilityChoice.Conceal;
                Assert.Equal(coverPath, dialog.CoverPreviewSource);
                Assert.Equal(originalBytes, System.IO.File.ReadAllBytes(coverPath));
            }
            finally
            {
                CloseWithoutDraftWarning(dialog);
                access.Initialize(new AppSettings(), BookStitchEnvironment.Production, () => false);
            }
        });
    }

    [Fact]
    public void Developer_image_roles_render_overrides_without_changing_faq_answers_and_restore_live_defaults()
    {
        RunOnStaThread(() =>
        {
            var access = DeveloperToolsCommands.AccessState;
            access.Initialize(new AppSettings { DeveloperToolsEnabled = true, DeveloperToolsVisible = true }, BookStitchEnvironment.Production, () => true);
            var preview = DeveloperToolsCommands.AppearancePreview;
            preview.Clear();
            var regular = new AppSettings { BackgroundPreset = "Black" };
            var resources = Application.Current.Resources;
            EventHandler apply = (_, _) => AppearanceSettingsService.ApplyEffectiveToResources(resources, regular);
            preview.Changed += apply;
            var info = new SettingsInfoControl();
            var faq = new SettingsFaqControl();
            var panel = new StackPanel();
            panel.Children.Add(info);
            panel.Children.Add(faq);
            var host = new Window { Content = panel, Width = 1050, Height = 700, ShowInTaskbar = false };
            try
            {
                apply(null, EventArgs.Empty);
                host.Show();
                DrainDispatcher();
                foreach (var variant in AppearanceAssetCatalog.Variants.Where(variant => !variant.IsText))
                    Assert.IsAssignableFrom<BitmapSource>(AppearanceSettingsService.LoadArtwork(variant.Role, variant.RelativePath));
                var rows = AppearanceAssetCatalog.PreviewRoles.Select(role => new DeveloperImagePreviewRow(role, preview)).ToArray();
                Assert.Equal(5, rows.Length);
                var header = rows.Single(row => row.Role == AppearanceAssetRole.HeaderBrand);
                header.Refresh(AppearanceMode.Dark);
                foreach (var variant in AppearanceAssetCatalog.Variants.Where(variant => variant.Role == AppearanceAssetRole.HeaderBrand && !variant.IsText))
                {
                    header.SelectedId = variant.Id;
                    DrainDispatcher();
                    Assert.Same(AppearanceSettingsService.LoadArtwork(variant.Role, variant.RelativePath),
                        resources[AppearanceResourceKeys.HeaderBrandImageSource]);
                    Assert.Equal(Visibility.Visible, resources[AppearanceResourceKeys.HeaderBrandImageVisibility]);
                }
                var mismatch = header.Choices.Single(choice => choice.Id == "HeaderBrand.Light");
                Assert.True(mismatch.Opacity < 1);
                header.SelectedId = mismatch.Id;
                DrainDispatcher();
                Assert.Same(AppearanceSettingsService.LoadArtwork(AppearanceAssetRole.HeaderBrand,
                    AppearanceAssetCatalog.FindVariant(AppearanceAssetRole.HeaderBrand, mismatch.Id)!.RelativePath), resources[AppearanceResourceKeys.HeaderBrandImageSource]);
                preview.SetImage(AppearanceAssetRole.HeaderBrand, "HeaderBrand.Text");
                DrainDispatcher();
                Assert.Equal(Visibility.Visible, resources[AppearanceResourceKeys.HeaderBrandTextVisibility]);
                Assert.Equal(Visibility.Collapsed, resources[AppearanceResourceKeys.HeaderBrandImageVisibility]);
                header.SelectedId = mismatch.Id;
                DrainDispatcher();
                Assert.Equal(Visibility.Collapsed, resources[AppearanceResourceKeys.HeaderBrandTextVisibility]);
                Assert.Equal(Visibility.Visible, resources[AppearanceResourceKeys.HeaderBrandImageVisibility]);
                var questions = (ComboBox)faq.FindName("QuestionsComboBox");
                var originalQuestion = questions.SelectedItem;
                preview.SetImage(AppearanceAssetRole.FaqSquare, "FaqSquare.Cake");
                preview.SetImage(AppearanceAssetRole.InfoSquare, "InfoSquare.Brand");
                DrainDispatcher();
                Assert.Same(originalQuestion, questions.SelectedItem);
                Assert.Same(AppearanceSettingsService.LoadArtwork(AppearanceAssetRole.FaqSquare,
                    AppearanceAssetCatalog.FindVariant(AppearanceAssetRole.FaqSquare, "FaqSquare.Cake")!.RelativePath), ((Image)faq.FindName("FaqSidebarImage")).Source);
                Assert.Same(resources["InfoSquareImageSource"], ((Image)info.FindName("InfoSidebarImage")).Source);
                preview.SetBackground("Ivory");
                DrainDispatcher();
                Assert.Equal("Black", regular.BackgroundPreset);
                Assert.Equal("HeaderBrand.Light", preview.GetImageId(AppearanceAssetRole.HeaderBrand));
                access.VisibilityChoice = DeveloperToolsVisibilityChoice.Hide;
                Assert.Equal("Ivory", preview.Resolve(regular).BackgroundPreset);
                access.VisibilityChoice = DeveloperToolsVisibilityChoice.Conceal;
                DrainDispatcher();
                Assert.Equal("Black", preview.Resolve(regular).BackgroundPreset);
                Assert.Null(preview.GetImageId(AppearanceAssetRole.HeaderBrand));
                Assert.Null(preview.GetImageId(AppearanceAssetRole.FaqSquare));
                Assert.Equal("Black", regular.BackgroundPreset);
                Assert.Null(AppearanceSettingsService.LoadArtwork(AppearanceAssetRole.HeaderBrand, "/BookStitch;component/Assets/missing.png"));
            }
            finally
            {
                host.Close();
                preview.Changed -= apply;
                access.Initialize(new AppSettings(), BookStitchEnvironment.Production, () => false);
                AppearanceSettingsService.ApplyToResources(resources, new AppSettings());
            }
        });
    }

    [Fact]
    public void Global_settings_gear_inherits_quick_menu_opt_in_without_changing_access()
    {
        RunOnStaThread(() =>
        {
            var enabled = DeveloperToolsCommands.AccessState.IsEnabled;
            var gear = new Button { Style = (Style)Application.Current.FindResource("GlobalSettingsButtonStyle") };
            Assert.True(DeveloperToolsCommands.GetQuickMenuEnabled(gear));
            Assert.Equal(enabled, DeveloperToolsCommands.AccessState.IsEnabled);
        });
    }

    [Fact]
    public void All_settings_gears_use_the_same_attached_router_and_existing_left_click()
    {
        var source = RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
        var gear = source.Descendants().Single(element => (string?)element.Attribute("Click") == "Settings_Click");
        Assert.Equal("True", gear.Attributes().Single(attribute => attribute.Name.LocalName == "DeveloperToolsCommands.QuickMenuEnabled").Value);
        var theme = RepositorySource.LoadXaml(["BookStitch", "Themes", "AppearanceTheme.xaml"]);
        var settingsStyle = theme.Descendants().Single(element => element.Name.LocalName == "Style" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == "GlobalSettingsButtonStyle"));
        Assert.Contains(settingsStyle.Elements(), element => (string?)element.Attribute("Property") == "services:DeveloperToolsCommands.QuickMenuEnabled" &&
            (string?)element.Attribute("Value") == "True");
        Assert.Contains(theme.Descendants(), element => (string?)element.Attribute("Property") == "services:DeveloperToolsCommands.QuickMenuEnabled" &&
            (string?)element.Attribute("Value") == "True");
        var commands = RepositorySource.Read(["BookStitch", "Services", "DeveloperToolsCommands.cs"]);
        Assert.Contains("mainWindow.OpenDeveloperQuickMenu(button)", commands);
        Assert.Contains("AccessState.IsEnabled", commands);
    }
}
