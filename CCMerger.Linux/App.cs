using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace CCMerger;

public sealed class App : Application
{
    public static readonly IBrush WindowBackground = Solid("#F0F0F0");

    private static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));

    public override void Initialize()
    {
        // Always light, like the Windows version (otherwise it follows the system's dark mode).
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        AddClassicLook();
    }

    /// <summary>
    /// Makes the Fluent controls look like the WinForms ones: flat grey buttons, square
    /// corners, thin grey borders and the green progress bar.
    /// </summary>
    private void AddClassicLook()
    {
        var button = new Style(x => x.OfType<Button>());
        button.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Solid("#E1E1E1")));
        button.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, Solid("#ADADAD")));
        button.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)));
        button.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0)));
        Styles.Add(button);

        var textBox = new Style(x => x.OfType<TextBox>());
        textBox.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Solid("#FFFFFF")));
        textBox.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, Solid("#7A7A7A")));
        textBox.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)));
        textBox.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0)));
        Styles.Add(textBox);

        var numeric = new Style(x => x.OfType<NumericUpDown>());
        numeric.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Solid("#FFFFFF")));
        numeric.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, Solid("#7A7A7A")));
        numeric.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)));
        numeric.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0)));
        Styles.Add(numeric);

        var progress = new Style(x => x.OfType<ProgressBar>());
        progress.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Solid("#E6E6E6")));
        progress.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Solid("#06B025")));
        progress.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, Solid("#BCBCBC")));
        progress.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)));
        progress.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0)));
        Styles.Add(progress);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
