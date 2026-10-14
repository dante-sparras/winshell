using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Winshell.Core.Diagnostics;

namespace Winshell.Kit;

/// <summary>
/// Every bar and panel animation that is not a theme transition runs here, on the compositor,
/// so it keeps pace with the display and follows the Windows animation setting.
/// </summary>
public static class Motion
{
    // One object for the life of the process. Asked several times per panel open, a new one each time was a WinRT activation each time.
    private static Windows.UI.ViewManagement.UISettings? Settings;

    // The elements a slide has already been turned on for, each with where it was last sent. Weak, so an element that leaves the screen is not kept.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, System.Runtime.CompilerServices.StrongBox<Vector3?>> Slides = new();

    // The loop a scrolling line is running, so the next line can stop it and let the compositor object go.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, CompositionAnimation> Scrolls = new();

    private const double DefaultCaretWidth = 1;
    private const double DefaultCaretBeatMs = 530;

    /// <summary>True when Windows animation effects are on.</summary>
    public static bool Enabled()
    {
        try
        {
            return (Settings ??= new Windows.UI.ViewManagement.UISettings()).AnimationsEnabled;
        }
        catch (Exception ex)
        {
            Log.Warning("Bar", "animation setting failed", ex);
            return true;
        }
    }

    /// <summary>How long a caret is shown, and then hidden, as Windows has it set.</summary>
    /// <returns>The time of one half of a blink. Null when Windows says a caret does not blink.</returns>
    public static TimeSpan? CaretBeat()
    {
        try
        {
            // Windows answers with its largest number for a caret that stays lit.
            var rate = (Settings ??= new Windows.UI.ViewManagement.UISettings()).CaretBlinkRate;
            return rate is 0 or uint.MaxValue ? null : TimeSpan.FromMilliseconds(rate);
        }
        catch (Exception ex)
        {
            Log.Warning("Bar", "caret blink setting failed", ex);
            return TimeSpan.FromMilliseconds(DefaultCaretBeatMs);
        }
    }

    /// <summary>How wide a caret is, as Windows has it set: one pixel, unless a person made it thicker.</summary>
    /// <returns>The width.</returns>
    public static double CaretWidth()
    {
        try
        {
            return Math.Max(DefaultCaretWidth, (Settings ??= new Windows.UI.ViewManagement.UISettings()).CaretWidth);
        }
        catch (Exception ex)
        {
            Log.Warning("Bar", "caret width setting failed", ex);
            return DefaultCaretWidth;
        }
    }

    /// <summary>The compositor's version of <paramref name="curve"/>.</summary>
    public static CubicBezierEasingFunction Easing(Compositor compositor, MotionCurve curve)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        return compositor.CreateCubicBezierEasingFunction(
            new Vector2((float)curve.X1, (float)curve.Y1),
            new Vector2((float)curve.X2, (float)curve.Y2));
    }

    /// <summary>
    /// Fades <paramref name="element"/> in from clear over <paramref name="duration"/>, settling like a Windows flyout.
    /// Nothing happens while animation effects are off.
    /// </summary>
    public static void FadeIn(UIElement element, TimeSpan duration) => FadeIn(element, duration, MotionCurve.Decelerate);

    /// <summary>
    /// Fades <paramref name="element"/> in from clear over <paramref name="duration"/> along <paramref name="curve"/>.
    /// The glyph is already drawn, and the layer fades, so the end does not draw it again. The resting opacity is the
    /// value the fade arrives at, which is what remains when it ends. Nothing happens while animation effects are off.
    /// </summary>
    /// <param name="element">What fades.</param>
    /// <param name="duration">How long the fade takes.</param>
    /// <param name="curve">How it settles.</param>
    public static void FadeIn(UIElement element, TimeSpan duration, MotionCurve curve)
    {
        ArgumentNullException.ThrowIfNull(element);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        // The base value is what remains when the fade ends, so the last frame and the frame after it are the same.
        visual.Opacity = 1;
        if (!Enabled() || duration <= TimeSpan.Zero)
        {
            return;
        }

        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, Easing(compositor, curve));
        fade.Duration = duration;
        visual.StartAnimation("Opacity", fade);
    }

    /// <summary>
    /// Fades <paramref name="element"/> out to clear over <paramref name="duration"/> along <paramref name="curve"/>.
    /// The glyph stays as it was drawn, and the layer fades, so the end does not draw it again. The resting opacity is clear,
    /// which is what remains when the fade ends. <paramref name="finished"/> runs then, on the UI thread. Nothing is animated
    /// while animation effects are off, and <paramref name="finished"/> runs at once.
    /// </summary>
    /// <param name="element">What fades.</param>
    /// <param name="duration">How long the fade takes.</param>
    /// <param name="curve">How it settles.</param>
    /// <param name="finished">Runs when the fade has ended. Null asks for nothing.</param>
    public static void FadeOut(UIElement element, TimeSpan duration, MotionCurve curve, Action? finished)
    {
        ArgumentNullException.ThrowIfNull(element);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        // Clear is what remains when the fade ends, so the last frame and the frame after it are the same.
        visual.Opacity = 0;
        if (!Enabled() || duration <= TimeSpan.Zero)
        {
            finished?.Invoke();
            return;
        }

        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 1);
        fade.InsertKeyFrame(1, 0, Easing(compositor, curve));
        fade.Duration = duration;
        // The value set above is what stays. Leaving the default puts the layer back to where the fade began, which pops the box on at the end.
        fade.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        if (finished is null)
        {
            visual.StartAnimation("Opacity", fade);
            return;
        }

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Opacity", fade);
        batch.Completed += (_, _) => finished();
        batch.End();
    }

    /// <summary>
    /// Turns <paramref name="element"/> about its center to <paramref name="degrees"/>.
    /// A turn that is already running continues from where it is.
    /// </summary>
    public static void Turn(FrameworkElement element, double degrees, bool animate)
    {
        ArgumentNullException.ThrowIfNull(element);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)(element.Width / 2), (float)(element.Height / 2), 0);
        if (!animate)
        {
            visual.StopAnimation("RotationAngleInDegrees");
            visual.RotationAngleInDegrees = (float)degrees;
            return;
        }

        var compositor = visual.Compositor;
        var turn = compositor.CreateScalarKeyFrameAnimation();
        turn.InsertKeyFrame(1, (float)degrees, Easing(compositor, MotionCurve.EaseOut));
        turn.Duration = UiMotion.Fast;
        // No first key frame: the turn starts from the current angle, so a reversal mid-turn does not jump.
        visual.StartAnimation("RotationAngleInDegrees", turn);
    }

    /// <summary>
    /// Moves <paramref name="element"/> to <paramref name="x"/>, and down to <paramref name="y"/> along a vertical bar, on the flyout curve.
    /// A move that is already running continues from where it is. Animation effects off places it at once.
    /// </summary>
    public static void Slide(UIElement element, double x, bool animate, double y = 0)
    {
        ArgumentNullException.ThrowIfNull(element);

        // Once per element. The bar's chip line and drop line slide many times, and each ask was a call into the compositor.
        if (!Slides.TryGetValue(element, out var sent))
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            sent = new System.Runtime.CompilerServices.StrongBox<Vector3?>();
            Slides.Add(element, sent);
        }

        var to = new Vector3((float)x, (float)y, 0);

        // A drag sends the drop line to the same gap on every pointer move. It is there or on its way,
        // and a new animation each time started the curve again and made two compositor objects per move.
        if (animate && sent.Value == to)
        {
            return;
        }

        sent.Value = to;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!animate || !Enabled())
        {
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", to);
            return;
        }

        var compositor = visual.Compositor;
        var move = compositor.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(1, to, Easing(compositor, MotionCurve.Decelerate));
        move.Duration = UiMotion.Normal;
        visual.StartAnimation("Translation", move);
    }

    /// <summary>
    /// Scrolls <paramref name="element"/> left by <paramref name="distance"/> and repeats from the start.
    /// The pass is linear, and <paramref name="duration"/> is the whole pass, so a longer line is not faster than a short one.
    /// A distance of zero, a duration of zero, or animation effects off puts the element back at the start and leaves it there.
    /// </summary>
    /// <param name="element">What moves. Its parent clips it, so the pass stays inside the box.</param>
    /// <param name="distance">How far one pass travels, in device-independent pixels.</param>
    /// <param name="duration">How long one pass takes.</param>
    public static void Scroll(UIElement element, double distance, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(element);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        if (Scrolls.TryGetValue(element, out var running))
        {
            Scrolls.Remove(element);
            running.Dispose();
        }

        if (!Enabled() || distance <= 0 || duration <= TimeSpan.Zero || !double.IsFinite(distance))
        {
            return;
        }

        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0, Vector3.Zero);
        // No curve: the distance is covered at one speed for the whole pass.
        animation.InsertKeyFrame(1, new Vector3(-(float)distance, 0, 0), visual.Compositor.CreateLinearEasingFunction());
        animation.Duration = duration;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        Scrolls.Add(element, animation);
        visual.StartAnimation("Translation", animation);
    }
}
