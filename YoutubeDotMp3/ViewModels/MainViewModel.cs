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

        public ObservableCollection<OperationViewModel> Operations { get; } = new ObservableCollection<OperationViewModel>();
        public bool HasRunningOperations => Tasks.Count > 0;
        
        private readonly CancellationTokenSource _applicationCancellation = new CancellationTokenSource();
        private CancellationTokenSource _operationCancellation = new CancellationTokenSource();

        static public readonly ValidationRule InputUrlValidationRule = new RegexValidationRule { Regex = YoutubeVideoAddressRegex, ErrorMessage = "Input must be a Youtube video URL" };
        private string _lastClipboardText;
        
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

        private readonly IDisposable _downloadSpeedRefresh;
        private readonly IDisposable _contextualCommandsRefresh;

        public MainViewModel()
        {
            AddOperationCommand = new SimpleCommand<bool>(AddOperation, CanAddOperation);
            ContextualCommands = new[]
            {
                CancelAllCommand = new SimpleCommand(CancelAll, CanCancelAll)
            };

            if (Clipboard.ContainsText())
                _lastClipboardText = Clipboard.GetText();

            _downloadSpeedRefresh = Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ => DownloadSpeed = Operations.ToArray().Sum(x => x.RefreshDownloadSpeed()));
            _contextualCommandsRefresh = Observable.Interval(TimeSpan.FromSeconds(0.5)).Subscribe(_ => Application.Current.Dispatcher.Invoke(() => 
            {
                foreach (ISimpleCommand command in ContextualCommands)
                    command.UpdateCanExecute();
            }));

            RunClipboardWatcher();
        }

        private bool CanAddOperation(bool hasError) => !hasError && !string.IsNullOrEmpty(InputUrl);
        private async void AddOperation()
        {
            await AddOperationAsync(InputUrl);
        }
        
        private async Task AddOperationAsync(string youtubeVideoUrl)
        {
            var operation = new OperationViewModel(youtubeVideoUrl);
            Operations.Insert(0, operation);

            Task runTask = operation.RunAsync(_downloadSemaphore);
            Tasks.GetOrAdd(runTask, default(byte));
            await runTask.ContinueWith(t => Tasks.TryRemove(runTask, out _), CancellationToken.None);
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

            ClipboardWatchdog(TimeSpan.FromMilliseconds(500), _applicationCancellation.Token)
                .ContinueWith(t => RunClipboardWatcher(), _applicationCancellation.Token, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.FromCurrentSynchronizationContext())
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
                            await AddOperationAsync(Clipboard.GetText());
                    }

                    _lastClipboardText = clipboardText;
                }

                await Task.Delay(refreshTime, cancellationToken);
            }
        }

            {

            {
        }

        public void CancelAllOperations()
        {
            if (_operationCancellation != null)
            {
                _operationCancellation.Cancel();
                _operationCancellation.Dispose();
                _operationCancellation = new CancellationTokenSource();
            }

            foreach (OperationViewModel operation in Operations.Where(x => x.CanCancel()))
                operation.Cancel();
        }

        public async Task PreDisposeAsync()
        {
            if (_applicationCancellation != null)
            {
                _applicationCancellation.Cancel();
                _applicationCancellation.Dispose();
            }

            _downloadSpeedRefresh?.Dispose();
            _contextualCommandsRefresh?.Dispose();

            CancelAllOperations();

            await Task.WhenAll(Tasks.Keys.ToArray());
        }

        public void Dispose()
        {
            _downloadSemaphore.Dispose();
        }
    }
}