using System.Windows.Input;

namespace YoutubeDotMp3.Utils
{
    public interface ISimpleCommand : ICommand
    {
        void UpdateCanExecute();
    }
}