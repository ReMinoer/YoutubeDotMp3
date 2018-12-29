using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using YoutubeDotMp3.ViewModels.Base;
using YoutubeDotMp3.ViewModels.Utils;

namespace YoutubeDotMp3.ViewModels
{
    public class MainViewModel : NotifyPropertyChangedBase, IDisposable
    {
        public const string ApplicationName = "Youtube.Mp3";
        
        private ConcurrentDictionary<Task, byte> Tasks { get; } = new ConcurrentDictionary<Task, byte>();
        private readonly SemaphoreSlimQueued _downloadSemaphore = new SemaphoreSlimQueued(10);
        private CancellationTokenSource _cancellation;
        private string _lastClipboardText;

        public ObservableCollection<OperationViewModel> Operations { get; } = new ObservableCollection<OperationViewModel>();
        public bool HasRunningOperations => Tasks.Count > 0;

        private bool _isClipboardWatcherEnabled;
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

        private long _downloadSpeed;
        public long DownloadSpeed
        {
            get => _downloadSpeed;
            private set => Set(ref _downloadSpeed, value);
        }

        public MainViewModel()
        {
            _cancellation = new CancellationTokenSource();

            if (Clipboard.ContainsText())
                _lastClipboardText = Clipboard.GetText();
            
            Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ => DownloadSpeed = Operations.Sum(x => x.RefreshDownloadSpeed()));
            RunClipboardWatcher();
        }

        public async Task AddOperation(string youtubeVideoUrl)
        {
            await AddOperation(youtubeVideoUrl, _cancellation.Token);
        }

        private void RunClipboardWatcher()
        {
            if (!IsClipboardWatcherEnabled)
                return;

            ClipboardWatchdog(TimeSpan.FromMilliseconds(500), _cancellation.Token)
                .ContinueWith(t => RunClipboardWatcher(), _cancellation.Token, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.FromCurrentSynchronizationContext())
                .ConfigureAwait(false);
        }

        private async Task ClipboardWatchdog(TimeSpan refreshTime, CancellationToken cancellationToken)
        {
            while (IsClipboardWatcherEnabled && !cancellationToken.IsCancellationRequested)
            {
                if (Clipboard.ContainsText())
                {
                    string clipboardText = null;
                    Application.Current.Dispatcher.Invoke(() => clipboardText = Clipboard.GetText());
                    if (clipboardText != _lastClipboardText)
                        await AddOperation(Clipboard.GetText(), cancellationToken);

                    _lastClipboardText = clipboardText;
                }

                await Task.Delay(refreshTime, cancellationToken);
            }
        }
        
        private async Task AddOperation(string youtubeVideoUrl, CancellationToken cancellationToken)
        {
            Task downloadSemaphoreWaitTask = _downloadSemaphore.WaitAsync(cancellationToken);
            OperationViewModel operation = OperationViewModel.FromYoutubeUri(youtubeVideoUrl);
            if (operation == null)
                return;

            Operations.Insert(0, operation);

            if (!await operation.InitializeAsync(cancellationToken))
            {
                await downloadSemaphoreWaitTask;
                _downloadSemaphore.Release();
                return;
            }

            Task runTask = operation.RunAsync(cancellationToken, _downloadSemaphore, downloadSemaphoreWaitTask);
            Tasks.GetOrAdd(runTask, default(byte));

            runTask.ContinueWith(t =>
            {
                Tasks.TryRemove(t, out _);
            }, CancellationToken.None).ConfigureAwait(false);
        }

        public async Task CancelAllOperations()
        {
            if (_cancellation != null)
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
                _cancellation = null;
            }

            foreach (OperationViewModel operation in Operations)
                operation.Cancel();

            await Task.WhenAll(Tasks.Keys.ToArray());
        }

        public void Dispose()
        {
            CancelAllOperations().Wait();
            _downloadSemaphore.Dispose();
        }
    }
}