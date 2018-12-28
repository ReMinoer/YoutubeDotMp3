using System;
using System.Globalization;
using System.Windows.Data;

namespace YoutubeDotMp3.Converters.Base
{
    public abstract class SimpleValueConverter<TFrom, TTo> : IValueConverter
    {
        protected abstract TTo Convert(TFrom value);
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Convert((TFrom)value);
        public virtual object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}