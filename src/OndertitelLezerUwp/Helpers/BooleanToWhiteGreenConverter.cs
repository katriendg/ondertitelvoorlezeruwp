using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;

namespace OndertitelLezerUwp.Helpers
{
    public class BooleanToWhiteGreenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool)
            {
                if ((bool)value == true)
                {
                    return new SolidColorBrush(Windows.UI.Colors.Green);
                }
            }
            return new SolidColorBrush(Windows.UI.Colors.White);
        }

        // No need to implement converting back on a one-way binding 
        public object ConvertBack(object value, Type targetType,
            object parameter, string language)
        {
            throw new NotImplementedException();
        }

    }
}
