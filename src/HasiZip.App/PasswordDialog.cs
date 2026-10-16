using System.Windows;
using System.Windows.Controls;

namespace HasiZip.App;

/// <summary>Passwortabfrage für verschlüsselte ZIP-, 7z- und RAR-Archive.</summary>
public sealed class PasswordDialog : Window
{
    private readonly PasswordBox _box = new() { FontSize = 14, Padding = new Thickness(6), Margin = new Thickness(0, 12, 0, 0) };

    private PasswordDialog(string archiveName, bool wrong)
    {
        Title = "Passwort – HasiZip";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Ui.Brush(Ui.Background);

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20), Width = 340 };
        var glyph = Ui.Glyph(Ui.GlyphLock, Ui.Blue, 30);
        stack.Children.Add(glyph);
        var label = Ui.Label($"„{archiveName}“ ist mit einem Passwort geschützt.", 13);
        stack.Children.Add(label);
        if (wrong)
        {
            var hint = Ui.Label("Das Passwort war falsch – bitte noch einmal.", 12, Ui.Red);
            hint.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(hint);
        }
        stack.Children.Add(_box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, -5, 0) };
        var ok = Ui.Button("Entpacken", primary: true);
        ok.IsDefault = true;
        ok.Click += (_, _) => { DialogResult = true; };
        var cancel = Ui.Button("Abbrechen");
        cancel.IsCancel = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        Content = stack;
        Loaded += (_, _) => _box.Focus();
    }

    /// <returns>Das Passwort oder <c>null</c> bei „Abbrechen“.</returns>
    public static string? Ask(Window owner, string archiveName, bool wrong)
    {
        var dialog = new PasswordDialog(archiveName, wrong) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog._box.Password : null;
    }
}
