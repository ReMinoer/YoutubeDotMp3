using System;
using System.Windows.Input;

namespace YoutubeDotMp3.ViewModels.Utils
{
    public class SimpleCommand : ISimpleCommand
    {
        private readonly Action _executeAction;
        private readonly Func<bool> _canExecuteAction;

        public event EventHandler CanExecuteChanged;

        public SimpleCommand(Action executeAction, Func<bool> canExecuteAction = null)
        {
            _executeAction = executeAction;
            _canExecuteAction = canExecuteAction;
        }
        
        public bool CanExecute() => _canExecuteAction?.Invoke() ?? true;
        public void Execute()
        {
            if (!CanExecute())
                return;

            _executeAction?.Invoke();
        }
        
        bool ICommand.CanExecute(object parameter) => CanExecute();
        void ICommand.Execute(object parameter) => Execute();

        public void UpdateCanExecute() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public class SimpleCommand<TParameter> : ISimpleCommand
    {
        private readonly Action<TParameter> _executeAction;
        private readonly Func<TParameter, bool> _canExecuteAction;

        public event EventHandler CanExecuteChanged;

        public SimpleCommand(Action executeAction, Func<TParameter, bool> canExecuteAction = null)
        {
            if (executeAction != null)
                _executeAction = _ => executeAction();
            else
                _executeAction = null;

            _canExecuteAction = canExecuteAction;
        }

        public SimpleCommand(Action<TParameter> executeAction, Func<TParameter, bool> canExecuteAction = null)
        {
            _executeAction = executeAction;
            _canExecuteAction = canExecuteAction;
        }
        
        public bool CanExecute(object parameter) => _canExecuteAction?.Invoke((TParameter)parameter) ?? true;
        public void Execute(object parameter)
        {
            var p = (TParameter)parameter;
            if (!CanExecute(p))
                return;

            _executeAction?.Invoke(p);
        }

        public void UpdateCanExecute() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}