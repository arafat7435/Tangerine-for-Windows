using System.Runtime.CompilerServices;
using System.Windows.Media.Animation;
namespace Tangerine;

// Short, event-driven WPF transitions. No render loop or timer runs while idle.
public static class Motion
{
    static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, AnimationTimeline>> active = new();
    public static bool Enabled => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    public static readonly DependencyProperty ButtonFeedbackProperty = DependencyProperty.RegisterAttached("ButtonFeedback", typeof(bool), typeof(Motion), new PropertyMetadata(false, FeedbackChanged));
    static readonly DependencyProperty FeedbackStateProperty = DependencyProperty.RegisterAttached("FeedbackState", typeof(ButtonMotion), typeof(Motion));
    public static bool GetButtonFeedback(DependencyObject target) => (bool)target.GetValue(ButtonFeedbackProperty);
    public static void SetButtonFeedback(DependencyObject target, bool value) => target.SetValue(ButtonFeedbackProperty, value);

    internal static void Number(DependencyObject target, DependencyProperty property, double value, int milliseconds = 140, bool animate = true)
    {
        var from = (double)target.GetValue(property);
        Begin(target, property, value, new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }, animate && Math.Abs(from - value) > .0001);
    }
    internal static void Tint(DependencyObject target, DependencyProperty property, Color value, int milliseconds = 120, bool animate = true)
    {
        var from = (Color)target.GetValue(property);
        Begin(target, property, value, new ColorAnimation(from, value, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } }, animate && from != value);
    }
    static void Begin(DependencyObject target, DependencyProperty property, object value, AnimationTimeline animation, bool animate)
    {
        var owner = (IAnimatable)target; var tracks = active.GetOrCreateValue(target);
        // These properties are owned by this helper (no bindings). Set a durable base
        // value: SetCurrentValue's coercion can be cleared when a clock is removed.
        tracks.Remove(property); owner.BeginAnimation(property, null); target.SetValue(property, value);
        if (!Enabled || !animate || target.Dispatcher.HasShutdownStarted) return;
        animation.FillBehavior = FillBehavior.Stop; Timeline.SetDesiredFrameRate(animation, 60); tracks[property] = animation;
        animation.Completed += (_, _) => { if (tracks.TryGetValue(property, out var current) && ReferenceEquals(current, animation)) { tracks.Remove(property); owner.BeginAnimation(property, null); } };
        owner.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
    internal static void Reveal(FrameworkElement element, double offset = 0, int milliseconds = 180, double opacity = .65)
    {
        if (!Enabled) return;
        element.Opacity = opacity; Number(element, UIElement.OpacityProperty, 1, milliseconds);
        if (offset != 0) { var move = new TranslateTransform(0, offset); element.RenderTransform = move; Number(move, TranslateTransform.YProperty, 0, milliseconds); }
    }
    static void FeedbackChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Button button) return;
        if (button.GetValue(FeedbackStateProperty) is ButtonMotion previous) { previous.Detach(); button.ClearValue(FeedbackStateProperty); }
        if ((bool)e.NewValue) button.SetValue(FeedbackStateProperty, new ButtonMotion(button));
    }
    sealed class ButtonMotion
    {
        readonly Button button; FrameworkElement? surface; ScaleTransform? scale; bool pressed;
        internal ButtonMotion(Button button)
        {
            this.button = button; button.Loaded += Loaded; button.Unloaded += Unloaded; button.MouseEnter += MouseChanged; button.MouseLeave += MouseChanged;
            button.PreviewMouseLeftButtonDown += Down; button.PreviewMouseLeftButtonUp += Up; button.LostMouseCapture += LostCapture;
            button.PreviewKeyDown += KeyDown; button.PreviewKeyUp += KeyUp; button.IsEnabledChanged += EnabledChanged;
            if (button.IsLoaded) Prepare();
        }
        void Loaded(object sender, RoutedEventArgs e) => Prepare();
        void Prepare()
        {
            button.ApplyTemplate(); surface = button.Template?.FindName("Surface", button) as FrameworkElement ?? button.Template?.FindName("b", button) as FrameworkElement;
            if (surface == null && VisualTreeHelper.GetChildrenCount(button) > 0) surface = VisualTreeHelper.GetChild(button, 0) as FrameworkElement;
            if (surface == null) return; scale = new ScaleTransform(1, 1); surface.RenderTransformOrigin = new Point(.5, .5); surface.RenderTransform = scale; Update(false);
        }
        void Update(bool animate = true)
        {
            if (surface == null || scale == null) return;
            var down = button.IsEnabled && pressed; var hover = button.IsEnabled && button.IsMouseOver;
            double size = !Enabled ? 1 : down ? .97 : hover ? 1.018 : 1;
            Number(scale, ScaleTransform.ScaleXProperty, size, down ? 75 : 140, animate); Number(scale, ScaleTransform.ScaleYProperty, size, down ? 75 : 140, animate);
            Number(surface, UIElement.OpacityProperty, !button.IsEnabled ? .4 : down ? .72 : hover ? .88 : 1, 120, animate);
        }
        void MouseChanged(object sender, MouseEventArgs e) => Update();
        void Down(object sender, MouseButtonEventArgs e) { pressed = true; Update(); }
        void Up(object sender, MouseButtonEventArgs e) { pressed = false; Update(); }
        void LostCapture(object sender, MouseEventArgs e) { pressed = false; Update(); }
        void KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Space) { pressed = true; Update(); } }
        void KeyUp(object sender, KeyEventArgs e) { if (e.Key == Key.Space) { pressed = false; Update(); } }
        void EnabledChanged(object sender, DependencyPropertyChangedEventArgs e) { pressed = false; Update(); }
        void Unloaded(object sender, RoutedEventArgs e)
        {
            pressed = false; if (scale != null) { Number(scale, ScaleTransform.ScaleXProperty, 1, animate: false); Number(scale, ScaleTransform.ScaleYProperty, 1, animate: false); }
            if (surface != null) Number(surface, UIElement.OpacityProperty, 1, animate: false);
        }
        internal void Detach()
        {
            Unloaded(button, new()); button.Loaded -= Loaded; button.Unloaded -= Unloaded; button.MouseEnter -= MouseChanged; button.MouseLeave -= MouseChanged;
            button.PreviewMouseLeftButtonDown -= Down; button.PreviewMouseLeftButtonUp -= Up; button.LostMouseCapture -= LostCapture; button.PreviewKeyDown -= KeyDown; button.PreviewKeyUp -= KeyUp; button.IsEnabledChanged -= EnabledChanged;
        }
    }
}
