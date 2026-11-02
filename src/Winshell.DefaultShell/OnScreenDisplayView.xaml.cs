using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Winshell.DefaultShell;

/// <summary>The on-screen display panel. It draws the model and does not decide when the panel opens.</summary>
public sealed partial class OnScreenDisplayView : UserControl
{
    private readonly Storyboard _fade = new();
    private readonly DoubleAnimation _fadeTo = new();

    public OnScreenDisplayView(OnScreenDisplayModel model)
    {
        Model = model;
        InitializeComponent();
        Storyboard.SetTarget(_fadeTo, Card);
        Storyboard.SetTargetProperty(_fadeTo, "Opacity");
        _fadeTo.EnableDependentAnimation = true;
        _fade.Children.Add(_fadeTo);
        model.PropertyChanged += OnModel;
        ShowArt();
    }

    public OnScreenDisplayModel Model { get; }

    private void OnModel(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OnScreenDisplayModel.MediaArtUrl))
            ShowArt();
        else if (e.PropertyName == nameof(OnScreenDisplayModel.Hiding))
            FadeTo(Model.Hiding ? 0 : 1);
        else if (e.PropertyName == nameof(OnScreenDisplayModel.Open) && Model.Open && !Model.Hiding)
            FadeTo(1);
    }

    private void OnEntered(object sender, PointerRoutedEventArgs e)
    {
        var fadingOut = Model.Hiding;
        Model.PointerInside = true;
        if (!fadingOut)
            return;
        _fade.Stop();
        Card.Opacity = 1;
    }

    private void OnExited(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Card).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X < Card.ActualWidth && point.Y < Card.ActualHeight)
            return;
        Model.PointerInside = false;
    }

    private void ShowArt()
    {
        Art.Source = Model.MediaArtUrl.Length > 0 && Uri.TryCreate(Model.MediaArtUrl, UriKind.Absolute, out var uri)
            ? new BitmapImage(uri)
            : null;
    }

    private void FadeTo(double to)
    {
        var ms = Model.AnimationsEnabled ? OnScreenDisplayModel.FadeMs : 0;
        if (ms == 0)
        {
            _fade.Stop();
            Card.Opacity = to;
            return;
        }
        _fadeTo.Duration = TimeSpan.FromMilliseconds(ms);
        _fadeTo.From = Card.Opacity;
        _fadeTo.To = to;
        _fade.Begin();
    }

    private Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private Visibility Hidden(bool visible) => visible ? Visibility.Collapsed : Visibility.Visible;
}
