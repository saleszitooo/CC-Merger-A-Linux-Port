using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CCMerger;

/// <summary>Avalonia has no MessageBox, so this is a minimal "text + OK" window.</summary>
internal sealed class MessageDialog : Window
{
    public static Task ShowAsync(Window owner, string title, string message)
    {
        var dialog = new MessageDialog(title, message);
        return dialog.ShowDialog(owner);
    }

    private MessageDialog(string title, string message)
    {
        Title = title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 560;
        CanResize = false;
        Background = App.WindowBackground;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        };

        var scroll = new ScrollViewer
        {
            MaxHeight = 420,
            Content = text
        };

        var ok = new Button
        {
            Content = "OK",
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ok.Click += (_, _) => Close();

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16
        };
        panel.Children.Add(scroll);
        panel.Children.Add(ok);

        Content = panel;
    }
}
