using System;
using System.Windows.Input;

namespace YoutubeDotMp3.ViewModels.Utils
{
    public class SimpleCommand : ICommand
    {
        private readonly Action<object> _executeAction;
        private readonly Func<object, bool> _canExecuteAction;

        public event EventHandler CanExecuteChanged;

        public SimpleCommand(Action executeAction, Func<bool> canExecuteAction = null)
        {
            _executeAction = _ => executeAction?.Invoke();
            if (canExecuteAction != null)
                _canExecuteAction = _ => canExecuteAction();
        }

        public SimpleCommand(Action<object> executeAction, Func<object, bool> canExecuteAction = null)
        {
            _executeAction = executeAction;
            _canExecuteAction = canExecuteAction;
        }
        
        public bool CanExecute(object parameter) => _canExecuteAction?.Invoke(parameter) ?? true;
        public void Execute(object parameter)
        {
            if (!CanExecute(parameter))
                return;

            _executeAction?.Invoke(parameter);
        }

        public void UpdateCanExecute() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}