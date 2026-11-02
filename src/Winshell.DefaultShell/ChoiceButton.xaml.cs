using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>One choice on the power mode or output page. The radio follows <see cref="QuickSettingsChoice.Chosen"/>.</summary>
public sealed partial class ChoiceButton : UserControl
{
    public static readonly DependencyProperty ChoiceProperty = DependencyProperty.Register(
        nameof(Choice), typeof(QuickSettingsChoice), typeof(ChoiceButton), new PropertyMetadata(null, OnChoiceChanged));

    private QuickSettingsChoice? _choice;

    public ChoiceButton()
    {
        InitializeComponent();
        Unloaded += (_, _) => Follow(null);
    }

    public QuickSettingsChoice? Choice
    {
        get => (QuickSettingsChoice?)GetValue(ChoiceProperty);
        set => SetValue(ChoiceProperty, value);
    }

    /// <summary>Keeps this radio out of the other page's group.</summary>
    public string Group
    {
        get => Radio.GroupName;
        set => Radio.GroupName = value;
    }

    private static void OnChoiceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((ChoiceButton)sender).Follow((QuickSettingsChoice?)e.NewValue);

    private void Follow(QuickSettingsChoice? choice)
    {
        if (_choice is not null)
            _choice.PropertyChanged -= OnChoice;
        _choice = choice;
        if (_choice is not null)
            _choice.PropertyChanged += OnChoice;
        Show();
    }

    private void OnChoice(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickSettingsChoice.Text))
            Radio.Content = _choice!.Text;
        else if (e.PropertyName == nameof(QuickSettingsChoice.Chosen))
            Radio.IsChecked = _choice!.Chosen;
    }

    private void Show()
    {
        Radio.Content = _choice?.Text ?? "";
        Radio.IsChecked = _choice?.Chosen == true;
    }

    private void OnClick(object sender, RoutedEventArgs e) => _choice?.Choose();
}
