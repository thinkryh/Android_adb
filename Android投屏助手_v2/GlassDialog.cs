using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Android投屏助手;

internal static class GlassDialog
{
    public static bool Confirm(Window? owner, string title, string message, string confirmText = "确认", string cancelText = "取消")
        => Show(owner, title, message, confirmText, cancelText, true);

    public static void Message(Window? owner, string title, string message, bool warning = false)
        => Show(owner, title, message, "知道了", null, warning);

    public static bool Choice(Window? owner, string title, string message, string actionText, string cancelText = "稍后")
        => Show(owner, title, message, actionText, cancelText, false);

    private static bool Show(Window? owner, string title, string message, string actionText, string? cancelText, bool caution)
        => CreateMessageWindow(owner, title, message, actionText, cancelText, caution).ShowDialog() == true;

    internal static Window CreateMessageWindow(Window? owner, string title, string message, string actionText, string? cancelText, bool caution)
    {
        var window = NewWindow(owner, title, 480, message.Length > 180 ? 285 : 232);
        var root = CreateShell(title, out var contentHost, out var closeButton);
        window.Content = root;
        closeButton.Click += (_, _) => window.DialogResult = false;

        var layout = new Grid { Margin = new Thickness(24, 7, 24, 22) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new Grid { VerticalAlignment = VerticalAlignment.Center };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = new Border
        {
            Width = 42,
            Height = 42,
            VerticalAlignment = VerticalAlignment.Top,
            Background = (Brush)Application.Current.FindResource("DialogBadgeBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("DialogBadgeBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(21),
            Child = new TextBlock
            {
                Text = caution ? "?" : "i",
                FontFamily = new FontFamily("Segoe UI Variable"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 22,
                Foreground = (Brush)Application.Current.FindResource("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        body.Children.Add(badge);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 138 };
        scroll.Content = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 23,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.FindResource("TextBrush")
        };
        Grid.SetColumn(scroll, 1);
        body.Children.Add(scroll);
        layout.Children.Add(body);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, MinWidth = 96, Height = 40, IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
            cancel.Click += (_, _) => window.DialogResult = false;
            buttons.Children.Add(cancel);
        }
        var action = new Button
        {
            Content = new TextBlock { Text = actionText, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold },
            MinWidth = 108,
            Height = 40,
            IsDefault = true,
            Margin = new Thickness(0),
            Background = (Brush)Application.Current.FindResource("ActionBrush"),
            Foreground = Brushes.White,
            BorderBrush = Brushes.Transparent
        };
        action.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(action);
        Grid.SetRow(buttons, 1);
        layout.Children.Add(buttons);
        contentHost.Children.Add(layout);
        return window;
    }

    public static Window Form(Window owner, string title, double width, double height, UIElement content)
    {
        var window = NewWindow(owner, title, width, height);
        var root = CreateShell(title, out var host, out var closeButton);
        window.Content = root;
        closeButton.Click += (_, _) => window.DialogResult = false;
        host.Children.Add(content);
        return window;
    }

    private static Window NewWindow(Window? owner, string title, double width, double height)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = width,
            MinHeight = height,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            Background = (Brush)Application.Current.FindResource("PanelStrongBrush"),
            WindowStartupLocation = owner?.IsVisible == true ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            Topmost = owner?.Topmost == true,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        if (owner?.IsVisible == true) window.Owner = owner;
        GlassWindowEffects.EnableRoundedCorners(window);
        return window;
    }

    private static Border CreateShell(string title, out Grid contentHost, out Button closeButton)
    {
        var root = new Border
        {
            Margin = new Thickness(0),
            CornerRadius = new CornerRadius(24),
            BorderThickness = new Thickness(0),
            Background = (Brush)Application.Current.FindResource("PanelStrongBrush")
        };
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        root.RenderTransform = scale;
        root.Loaded += (_, _) =>
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.975, 1, TimeSpan.FromMilliseconds(210)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.975, 1, TimeSpan.FromMilliseconds(210)) { EasingFunction = ease });
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { Margin = new Thickness(23, 0, 15, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && Window.GetWindow(root) is { } window)
                window.DragMove();
        };
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        closeButton = new Button
        {
            Content = "×", Width = 31, Height = 31, Padding = new Thickness(0), Margin = new Thickness(0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 19,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(closeButton, 1);
        header.Children.Add(closeButton);
        grid.Children.Add(header);
        contentHost = new Grid();
        Grid.SetRow(contentHost, 1);
        grid.Children.Add(contentHost);
        root.Child = grid;
        return root;
    }
}
