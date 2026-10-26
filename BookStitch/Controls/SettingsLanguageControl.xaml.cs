using BookStitch.Models;
using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;

namespace BookStitch.Controls;

public partial class SettingsLanguageControl : UserControl
{
    private GlobalSettingsManager? _settingsManager;
    private Action? _requestApplicationRestart;
    private string _activeUiLanguageId = UiLanguageService.System;
    private bool _isLoading = true;

    public SettingsLanguageControl() => InitializeComponent();

    public void Initialize(GlobalSettingsManager settingsManager, Action requestApplicationRestart)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _requestApplicationRestart = requestApplicationRestart ?? throw new ArgumentNullException(nameof(requestApplicationRestart));
        LanguageHeadingTextBlock.Text = LocalizationText.Get("LanguageHeading");
        _activeUiLanguageId = Application.Current is App app
            ? app.AppliedUiLanguageId
            : UiLanguageService.Normalize(Settings.UiLanguage);
        System.Windows.Automation.AutomationProperties.SetName(UiLanguageComboBox, LanguageHeadingTextBlock.Text);
        UiLanguageComboBox.ItemsSource = UiLanguageService.Choices
            .Select(option => new UiLanguageChoice(option.Id, option.DisplayName))
            .ToList();
        UiLanguageComboBox.DisplayMemberPath = nameof(UiLanguageChoice.Label);
        UiLanguageComboBox.SelectedItem = UiLanguageComboBox.Items.OfType<UiLanguageChoice>()
            .First(choice => choice.Id == UiLanguageService.Normalize(Settings.UiLanguage));
        LanguageRestartTextBlock.Text = LocalizationText.Get("LanguageRestart");
        UiLanguageRestartButton.Content = LocalizationText.Get("LanguageRestartButton");
        UpdateLanguageRestartPrompt();
        _isLoading = false;
    }

    private AppSettings Settings =>
        _settingsManager?.Current ?? throw new InvalidOperationException("The language settings control is not initialized.");

    private sealed record UiLanguageChoice(string Id, string Label)
    {
        // The combo box surface renders the selection box through a custom control template that
        // does not pick up DisplayMemberPath, so the record would fall back to its generated
        // ToString(). Returning the label keeps the closed box readable in every language.
        public override string ToString() => Label;
    }

    private void UiLanguageComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || UiLanguageComboBox.SelectedItem is not UiLanguageChoice choice ||
            choice.Id == Settings.UiLanguage)
            return;

        var previousId = Settings.UiLanguage;
        Settings.UiLanguage = choice.Id;
        if (_settingsManager!.SaveImmediate())
        {
            UpdateLanguageRestartPrompt();
            return;
        }

        Settings.UiLanguage = previousId;
        _isLoading = true;
        UiLanguageComboBox.SelectedItem = UiLanguageComboBox.Items.OfType<UiLanguageChoice>()
            .First(item => item.Id == UiLanguageService.Normalize(previousId));
        _isLoading = false;
    }

    private void UpdateLanguageRestartPrompt()
    {
        var restartNeeded = UiLanguageService.Normalize(Settings.UiLanguage) != _activeUiLanguageId;
        var targetCulture = UiLanguageService.Resolve(Settings.UiLanguage, System.Globalization.CultureInfo.InstalledUICulture);
        LanguageRestartTextBlock.Text = LocalizationText.Get("LanguageRestart", targetCulture);
        UiLanguageRestartButton.Content = LocalizationText.Get("LanguageRestartButton", targetCulture);
        TranslationNoticeTextBlock.Text = LocalizationText.Get("Settings.Language.TranslationNotice", targetCulture);
        ChapterNoticeTextBlock.Text = LocalizationText.Get("Settings.Language.ChapterNotice", targetCulture);
        LanguageRestartTextBlock.Visibility = restartNeeded ? Visibility.Visible : Visibility.Hidden;
        UiLanguageRestartButton.Visibility = restartNeeded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UiLanguageRestartButton_Click(object sender, RoutedEventArgs e) =>
        _requestApplicationRestart?.Invoke();

}
