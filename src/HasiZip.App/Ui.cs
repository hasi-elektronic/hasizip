using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace HasiZip.App;

/// <summary>Farben, Schriften und Bausteine im Hasi-Elektronic-Look.</summary>
public static class Ui
{
    public static readonly Color Navy = Color.FromRgb(0x0C, 0x19, 0x29);
    public static readonly Color Blue = Color.FromRgb(0x4D, 0xBC, 0xE8);
    public static readonly Color BlueDark = Color.FromRgb(0x2F, 0x9F, 0xCC);
    public static readonly Color Background = Color.FromRgb(0xF4, 0xF7, 0xFA);
    public static readonly Color Text = Color.FromRgb(0x1E, 0x2A, 0x38);
    public static readonly Color Gray = Color.FromRgb(0x6B, 0x77, 0x85);
    public static readonly Color Line = Color.FromRgb(0xDD, 0xE4, 0xEC);
    public static readonly Color Green = Color.FromRgb(0x22, 0xA0, 0x6B);
    public static readonly Color Red = Color.FromRgb(0xD6, 0x45, 0x45);

    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public const string GlyphPackage = "";
    public const string GlyphCheck = "";
    public const string GlyphError = "";
    public const string GlyphLock = "";

    public static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static Style? _primary, _secondary;

    /// <summary>Flache, abgerundete Buttons; Stil einmalig aus XAML erzeugt.</summary>
    public static Button Button(string text, bool primary = false)
    {
        _primary ??= MakeStyle("#4DBCE8", "#2F9FCC", "#FFFFFF", "#4DBCE8");
        _secondary ??= MakeStyle("#FFFFFF", "#EAF6FC", "#1E2A38", "#C9D6E2");
        return new Button { Content = text, Style = primary ? _primary : _secondary, Margin = new Thickness(5, 0, 5, 0) };
    }

    private static Style MakeStyle(string bg, string hover, string fg, string border)
    {
        var xaml = $$"""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Setter Property="Foreground" Value="{{fg}}"/>
              <Setter Property="FontSize" Value="13"/>
              <Setter Property="FontWeight" Value="SemiBold"/>
              <Setter Property="Cursor" Value="Hand"/>
              <Setter Property="Padding" Value="16,9"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="Button">
                    <Border x:Name="b"
                            Background="{{bg}}" BorderBrush="{{border}}" BorderThickness="1" CornerRadius="8"
                            Padding="{TemplateBinding Padding}">
                      <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                      <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="b" Property="Background" Value="{{hover}}"/>
                      </Trigger>
                      <Trigger Property="IsEnabled" Value="False">
                        <Setter TargetName="b" Property="Opacity" Value="0.5"/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """;
        return (Style)XamlReader.Parse(xaml);
    }

    public static TextBlock Label(string text, double size = 13, Color? color = null, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Brush(color ?? Text),
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    public static TextBlock Glyph(string glyph, Color color, double size = 44) => new()
    {
        Text = glyph,
        FontFamily = Icons,
        FontSize = size,
        Foreground = Brush(color),
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 0, 0, 10),
    };

    public static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : v.ToString(v >= 100 ? "0" : "0.0", De) + " " + units[u];
    }

    public static string Duration(TimeSpan t)
    {
        if (t.TotalSeconds < 60) return t.TotalSeconds.ToString(t.TotalSeconds < 10 ? "0.0" : "0", De) + " s";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} min {t.Seconds} s";
        return $"{(int)t.TotalHours} h {t.Minutes} min";
    }

    public static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : n.ToString("N0", De) + " " + many;
}
