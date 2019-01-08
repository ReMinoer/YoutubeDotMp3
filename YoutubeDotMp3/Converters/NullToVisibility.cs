using System.Windows;
using YoutubeDotMp3.Converters.Base;

namespace YoutubeDotMp3.Converters
{
    public class NullToVisibility : SimpleValueConverter<object, Visibility>
    {
        protected override Visibility Convert(object value)
        {
            return value != null ? Visibility.Visible : Visibility.Hidden;
        }
    }
}