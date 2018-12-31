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
        
        public ISimpleCommand AddOperationCommand { get; }

        public ISimpleCommand[] ContextualCommands { get; }
        public ISimpleCommand CancelAllCommand { get; }

        public MainViewModel()
        {
            _cancellation = new CancellationTokenSource();

            AddOperationCommand = new SimpleCommand<bool>(AddOperation, CanAddOperation);
            ContextualCommands = new[]
            {
                CancelAllCommand = new SimpleCommand(CancelAll, CanCancelAll)
            };

            if (Clipboard.ContainsText())
                _lastClipboardText = Clipboard.GetText();
            
            Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ => DownloadSpeed = Operations.Sum(x => x.RefreshDownloadSpeed()));
            Observable.Interval(TimeSpan.FromSeconds(0.5)).Subscribe(_ => Application.Current.Dispatcher.Invoke(() => 
            {
                foreach (ISimpleCommand command in ContextualCommands)
                    command.UpdateCanExecute();
            }));

            RunClipboardWatcher();
        }

        private bool CanAddOperation(bool hasError) => !hasError && !string.IsNullOrEmpty(InputUrl);
        private void AddOperation()
        {
            AddOperation(InputUrl, _cancellation.Token).ConfigureAwait(false);
        }

        private bool CanCancelAll() => Operations.Any(x => x.IsRunning);
        private void CancelAll()
        {
            CancelAllOperations();
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

        private void CancelAllOperations()
        {
            if (_cancellation != null)
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
                _cancellation = new CancellationTokenSource();
            }

            foreach (OperationViewModel operation in Operations)
                operation.Cancel();
        }

        public async Task CancelAllBeforeQuit()
        {
            CancelAllOperations();
            await Task.WhenAll(Tasks.Keys.ToArray());
        }

        public void Dispose()
        {
            CancelAllBeforeQuit().Wait();
            _downloadSemaphore.Dispose();
        }
    }
}