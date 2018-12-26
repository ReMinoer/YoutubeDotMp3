using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using YoutubeDotMp3.ViewModels.Base;

namespace YoutubeDotMp3.ViewModels
{
    public class MainViewModel : NotifyPropertyChangedBase, IDisposable
    {
        public ObservableCollection<OperationViewModel> Operations { get; } = new ObservableCollection<OperationViewModel>();

        private bool _isClipboardWatcherEnabled = true;
        public bool IsClipboardWatcherEnabled
        {
            get => _isClipboardWatcherEnabled;
            set
            {
                if (Set(ref _isClipboardWatcherEnabled, value))
                {
                    if (_isClipboardWatcherEnabled)
                    {
                        _lastClipboardText = Clipboard.GetText();
                        RunClipboardWatcher();
                    }
                }
            }
        }

        private readonly CancellationTokenSource _cancellation;
        private string _lastClipboardText;

        public MainViewModel()
        {
            _cancellation = new CancellationTokenSource();

            if (Clipboard.ContainsText())
                _lastClipboardText = Clipboard.GetText();
            
            RunClipboardWatcher();
        }

        public void AddOperation(string url)
        {
            AddOperation(url, _cancellation.Token);
        }

        private void RunClipboardWatcher()
        {
            if (!IsClipboardWatcherEnabled)
                return;

            ClipboardWatchdog(TimeSpan.FromMilliseconds(500), _cancellation.Token)
                .ContinueWith(t => RunClipboardWatcher(), _cancellation.Token, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.FromCurrentSynchronizationContext())
                .ConfigureAwait(false);
        }

        private async Task ClipboardWatchdog(TimeSpan resfreshTime, CancellationToken cancellationToken)
        {
            while (IsClipboardWatcherEnabled && !cancellationToken.IsCancellationRequested)
            {
                if (Clipboard.ContainsText())
                {
                    string clipboardText = null;
                    Application.Current.Dispatcher.Invoke(() => clipboardText = Clipboard.GetText());
                    if (clipboardText != _lastClipboardText)
                        AddOperation(Clipboard.GetText(), cancellationToken);

                    _lastClipboardText = clipboardText;
                }

                await Task.Delay(resfreshTime, cancellationToken);
            }
        }

        private void AddOperation(string url, CancellationToken cancellationToken)
        {
            OperationViewModel operation = OperationViewModel.FromYoutubeUri(url);
            if (operation == null)
                return;

            Operations.Insert(0, operation);
            operation.RunAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_cancellation == null)
                return;

            _cancellation.Cancel();
            _cancellation.Dispose();
        }
    }
}