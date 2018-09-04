using System;
using System.Windows.Input;

namespace YoutubeDotMp3.ViewModels.Utils
{
    public class SimpleCommand : ICommand
    {
        private readonly Action _action;
        public event EventHandler CanExecuteChanged;

        public SimpleCommand(Action action)
        {
            _action = action;
        }

        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _action();
    }
}