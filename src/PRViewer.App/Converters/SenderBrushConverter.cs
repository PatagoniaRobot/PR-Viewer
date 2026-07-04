using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PRViewer.App.Converters;

/// <summary>
/// Asigna un color estable a cada remitente por su índice de aparición.
/// Arranca con el verde y el cian de la paleta PRImager.
/// </summary>
public sealed class SenderBrushConverter : IValueConverter
{
    private static readonly Brush[] Palette =
    {
        CreateFrozen("#10B981"), // verde PRImager
        CreateFrozen("#00D4AA"), // cian PRImager
        CreateFrozen("#818CF8"),
        CreateFrozen("#F59E0B"),
        CreateFrozen("#F472B6"),
        CreateFrozen("#38BDF8"),
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int index ? Palette[index % Palette.Length] : Palette[0];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush CreateFrozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
