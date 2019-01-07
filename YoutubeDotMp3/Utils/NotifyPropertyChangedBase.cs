using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YoutubeDotMp3.Utils
{
    public abstract class NotifyPropertyChangedBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool Set<T>(ref T backingField, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(value, backingField))
                return false;

            backingField = value;
            NotifyPropertyChanged(propertyName);
            return true;
        }
    }
}