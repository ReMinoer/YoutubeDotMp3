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

        private readonly CancellationTokenSource _cancellation;
        private string _lastClipboardText;

        public MainViewModel()
        {
            _cancellation = new CancellationTokenSource();

            if (Clipboard.ContainsText())
                _lastClipboardText = Clipboard.GetText();
            
            RunClipboardWatchdog();
        }

        private void RunClipboardWatchdog()
        {
            ClipboardWatchdog(TimeSpan.FromMilliseconds(500), _cancellation.Token)
                .ContinueWith(t => RunClipboardWatchdog(), _cancellation.Token, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.FromCurrentSynchronizationContext())
                .ConfigureAwait(false);
        }

        private async Task ClipboardWatchdog(TimeSpan resfreshTime, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Clipboard.ContainsText())
                {
                    string clipboardText = null;
                    Application.Current.Dispatcher.Invoke(() => clipboardText = Clipboard.GetText());
                    if (clipboardText != _lastClipboardText)
                    {
                        OperationViewModel operation = OperationViewModel.FromYoutubeUri(Clipboard.GetText());
                        if (operation != null)
                        {
                            Operations.Insert(0, operation);
                            operation.RunAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }

                    _lastClipboardText = clipboardText;
                }

                await Task.Delay(resfreshTime, cancellationToken);
            }
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