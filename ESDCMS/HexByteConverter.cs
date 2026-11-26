using System;
using System.Globalization;
using System.Windows.Data;

namespace ESDCMS;

/// <summary>Displays a byte as 2-digit hex (e.g. "03"); accepts "03", "0x03" or "3" as input.</summary>
public class HexByteConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return value is byte b ? b.ToString("X2") : string.Empty;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		string text = (value as string)?.Trim();
		if (string.IsNullOrEmpty(text))
		{
			return Binding.DoNothing;
		}
		if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			text = text.Substring(2);
		}
		return byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte result)
			? result
			: System.Windows.DependencyProperty.UnsetValue;
	}
}
