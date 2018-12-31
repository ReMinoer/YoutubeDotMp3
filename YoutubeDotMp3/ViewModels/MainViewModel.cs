using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YoutubeDotMp3.ValidationRules;
using YoutubeDotMp3.ViewModels.Base;
using YoutubeDotMp3.ViewModels.Utils;

namespace YoutubeDotMp3.ViewModels
{
    public class MainViewModel : NotifyPropertyChangedBase, IDisposable
    {
        public const string ApplicationName = "Youtube.Mp3";

        static public readonly Regex YoutubeVideoAddressRegex = new Regex(
            @"^(?:(?:https?:\/\/)?(?:(?:www\.)?youtube\.com\/watch\?.*v=([\w\-]*)?(?:\&.*)?.*|youtu\.be\/([\w\-]*)?:\?.*?))?$",
            RegexOptions.Compiled);
        
        private ConcurrentDictionary<Task, byte> Tasks { get; } = new ConcurrentDictionary<Task, byte>();
        private readonly SemaphoreSlimQueued _downloadSemaphore = new SemaphoreSlimQueued(10);
        private CancellationTokenSource _cancellation;
        private string _lastClipboardText;

        public ObservableCollection<OperationViewModel> Operations { get; } = new ObservableCollection<OperationViewModel>();
        public bool HasRunningOperations => Tasks.Count > 0;

        static public readonly ValidationRule InputUrlValidationRule = new RegexValidationRule { Regex = YoutubeVideoAddressRegex, ErrorMessage = "Input must be a Youtube video URL" };
        
        private string _inputUrl = string.Empty;
        public string InputUrl
        {
            get => _inputUrl;
            set => Set(ref _inputUrl, value);
        }

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

        public ICommand AddOperationCommand { get; }

        public MainViewModel()
        {
            _cancellation = new CancellationTokenSource();

            AddOperationCommand = new SimpleCommand<bool>(AddOperation, CanAddOperation);

            if (Clipboard.ContainsText())
                _lastClipboardText = Clipboard.GetText();
            
            Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ => DownloadSpeed = Operations.Sum(x => x.RefreshDownloadSpeed()));
            RunClipboardWatcher();
        }

        private bool CanAddOperation(bool hasError) => !hasError && !string.IsNullOrEmpty(InputUrl);
        private void AddOperation()
        {
            AddOperation(InputUrl, _cancellation.Token).ConfigureAwait(false);
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
                    {
                        if (InputUrlValidationRule.Validate(clipboardText, CultureInfo.CurrentCulture).IsValid)
                            await AddOperation(Clipboard.GetText(), cancellationToken);
                    }

                    _lastClipboardText = clipboardText;
                }

                await Task.Delay(refreshTime, cancellationToken);
            }
        }
        
        private async Task AddOperation(string youtubeVideoUrl, CancellationToken cancellationToken)
        {
            Task downloadSemaphoreWaitTask = _downloadSemaphore.WaitAsync(cancellationToken);

            var operation = new OperationViewModel(youtubeVideoUrl);
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