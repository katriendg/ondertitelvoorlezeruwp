using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace OndertitelLezerUwp.Helpers
{
    public class OneThirdConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            try
            {
                if (value == null)
                {
                    return 0.0;
                }

                double measure = (double)value;
                return (measure / 3);
            }
            catch (Exception)
            {
                return 0.0;
            }

        }

        // No need to implement converting back on a one-way binding 
        public object ConvertBack(object value, Type targetType,
            object parameter, string language)
        {
            throw new NotImplementedException();
        }

    }
}
