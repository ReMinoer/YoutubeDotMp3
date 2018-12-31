using System.Windows.Input;

namespace YoutubeDotMp3.ViewModels.Utils
{
    public interface ISimpleCommand : ICommand
    {
        void UpdateCanExecute();
    }
}