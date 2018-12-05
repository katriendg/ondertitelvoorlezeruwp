using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace OndertitelLezerUwp.Helpers
{
    public class BooleanToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool)
            {
                if ((bool)value == true)
                {
                    return 1.0;
                }
            }
            return 0.5;
        }

        // No need to implement converting back on a one-way binding 
        public object ConvertBack(object value, Type targetType,
            object parameter, string language)
        {
            throw new NotImplementedException();
        }

    }
}
