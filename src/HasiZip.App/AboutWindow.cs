using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HasiZip.App;

public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "Über HasiZip";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version == null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}";

        var stack = new StackPanel { Margin = new Thickness(28, 22, 28, 22), HorizontalAlignment = HorizontalAlignment.Center };
        try
        {
            stack.Children.Add(new Image { Width = 96, Height = 96, Margin = new Thickness(0, 0, 0, 8), Source = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/hasizip.ico")) });
        }
        catch { /* Icon optional */ }
        stack.Children.Add(new TextBlock { Text = "HasiZip", FontSize = 22, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = $"Version {versionText}", Foreground = SystemColors.GrayTextBrush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 10) });
        stack.Children.Add(new TextBlock { Text = "Entwickelt von Hamdi Güncavdı", HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = "Hasi Elektronic · Vaihingen/Enz", HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = "hasi-elektronic.de · 07042/16391", Foreground = new SolidColorBrush(Ui.Blue), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 8) });
        stack.Children.Add(new TextBlock
        {
            Text = $"© {DateTime.Now.Year} Hasi Elektronic. Packt ZIP auf allen Prozessorkernen,\nentpackt ZIP, 7z, RAR, TAR, GZ, BZ2 und XZ. Nutzt SharpCompress (MIT-Lizenz).",
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            Foreground = SystemColors.GrayTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var ok = new Button { Content = "OK", Width = 90, Margin = new Thickness(0, 14, 0, 0), IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => Close();
        stack.Children.Add(ok);
        Content = stack;
    }
}
