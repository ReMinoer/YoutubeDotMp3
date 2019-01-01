using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using YoutubeDotMp3.ValidationRules;
using YoutubeDotMp3.ViewModels.Base;
using YoutubeDotMp3.ViewModels.Utils;

namespace YoutubeDotMp3.ViewModels
{
    public class MainViewModel : NotifyPropertyChangedBase, IDisposable
    {
        public const string ApplicationName = "Youtube.Mp3";
        public const string FriendlyApplicationName = "YoutubeDotMp3";

        static public readonly Regex YoutubeVideoAddressRegex = new Regex(
            @"^(?:(?:https?:\/\/)?(?:(?:www\.)?youtube\.com\/watch\?.*v=([\w\-]*)?(?:\&.*)?.*|youtu\.be\/([\w\-]*)?:\?.*?))?$",
            RegexOptions.Compiled);
        
        static public readonly Regex YoutubePlaylistAddressRegex = new Regex(
            @"^(?:(?:https?:\/\/)?(?:(?:www\.)?youtube\.com\/playlist\?.*list=([\w\-]*)?(?:\&.*)?.*))?$",
            RegexOptions.Compiled);

        static public readonly ValidationRule InputUrlValidationRule = new OrValidationRule
        {
            ErrorMessage = "Input must be a Youtube video URL",
            Rules =
            {
                new RegexValidationRule { Regex = YoutubeVideoAddressRegex },
                new RegexValidationRule { Regex = YoutubePlaylistAddressRegex }
            }
        };
        
        private ConcurrentDictionary<Task, byte> Tasks { get; } = new ConcurrentDictionary<Task, byte>();
        private readonly SemaphoreSlimQueued _downloadSemaphore = new SemaphoreSlimQueued(10);

        public ObservableCollection<OperationViewModel> Operations { get; } = new ObservableCollection<OperationViewModel>();
        public bool HasRunningOperations => Tasks.Count > 0;
        
        private readonly CancellationTokenSource _applicationCancellation = new CancellationTokenSource();
        private CancellationTokenSource _operationCancellation = new CancellationTokenSource();

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
        
        private OperationViewModel _selectedOperation;
        public OperationViewModel SelectedOperation
        {
            get => _selectedOperation;
            set
            {
                if (_selectedOperation != null)
                    _selectedOperation.CurrentStateChanged -= SelectedOperationCurrentStateChanged;

                if (Set(ref _selectedOperation, value))
                    foreach (ISimpleCommand command in ContextualCommands)
                        command.UpdateCanExecute();
                
                if (_selectedOperation != null)
                    _selectedOperation.CurrentStateChanged += SelectedOperationCurrentStateChanged;
            }
        }

        private void SelectedOperationCurrentStateChanged(object sender, OperationViewModel.State e)
        {
            foreach (ISimpleCommand command in ContextualCommands)
                command.UpdateCanExecute();
        }

        private long _downloadSpeed;
        public long DownloadSpeed
        {
            get => _downloadSpeed;
            private set => Set(ref _downloadSpeed, value);
        }
        
        public ISimpleCommand AddOperationCommand { get; }

        public ISimpleCommand[] ContextualCommands { get; }
        public ISimpleCommand PlayCommand { get; }
        public ISimpleCommand ShowInExplorerCommand { get; }
        public ISimpleCommand ShowOnYoutubeCommand { get; }
        public ISimpleCommand CancelCommand { get; }
        public ISimpleCommand CancelAllCommand { get; }
        public ISimpleCommand ShowErrorMessageCommand { get; }

        private readonly IDisposable _downloadSpeedRefresh;
        private readonly IDisposable _contextualCommandsRefresh;

        public MainViewModel()
        {
            AddOperationCommand = new SimpleCommand<bool>(AddOperation, CanAddOperation);
            ContextualCommands = new[]
            {
                PlayCommand = new SimpleCommand(Play, CanPlay),
                ShowInExplorerCommand = new SimpleCommand(ShowInExplorer, CanShowInExplorer),
                ShowOnYoutubeCommand = new SimpleCommand(ShowOnYoutube, CanShowOnYoutube),
                CancelCommand = new SimpleCommand(Cancel, CanCancel),
                CancelAllCommand = new SimpleCommand(CancelAll, CanCancelAll),
                ShowErrorMessageCommand = new SimpleCommand(ShowErrorMessage, CanShowErrorMessage)
            };

            if (Clipboard.ContainsText())
                _lastClipboardText = Clipboard.GetText();

            _downloadSpeedRefresh = Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ => DownloadSpeed = Operations.ToArray().Sum(x => x.RefreshDownloadSpeed()));
            _contextualCommandsRefresh = Observable.Interval(TimeSpan.FromSeconds(0.5)).Subscribe(_ => Application.Current.Dispatcher.Invoke(() => CancelAllCommand.UpdateCanExecute()));

            RunClipboardWatcher();
        }

        private bool CanAddOperation(bool hasError) => !hasError && !string.IsNullOrEmpty(InputUrl);
        private async void AddOperation()
        {
            Match playlistRegexMatch = YoutubePlaylistAddressRegex.Match(InputUrl);
            if (playlistRegexMatch.Success)
            {
                await AddOperationsFromPlaylist(playlistRegexMatch.Groups[1].Value, _operationCancellation.Token);
                return;
            }

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

        private async Task AddOperationsFromPlaylist(string youtubePlaylistId, CancellationToken cancellationToken)
        {
            try
            {
                UserCredential credential;
                using (var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(Properties.Resources.GoogleClientSecretsJson)))
                {
                    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.Load(jsonStream).Secrets,
                        new[] { YouTubeService.Scope.Youtube },
                        "user",
                        cancellationToken,
                        new FileDataStore(FriendlyApplicationName)
                    );
                }

                using (var youTubeService = new YouTubeService(new BaseClientService.Initializer { HttpClientInitializer = credential }))
                {
                    string pageToken = null;
                    do
                    {
                        PlaylistItemsResource.ListRequest listRequest = youTubeService.PlaylistItems.List("contentDetails");
                        listRequest.PlaylistId = youtubePlaylistId;
                        listRequest.MaxResults = 50;
                        listRequest.PageToken = pageToken;

                        PlaylistItemListResponse listResponse = await listRequest.ExecuteAsync(cancellationToken);
                        foreach (PlaylistItem playlistItem in listResponse.Items)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            AddOperationAsync($"https://www.youtube.com/watch?v={playlistItem.ContentDetails.VideoId}").ConfigureAwait(false);
                        }

                        pageToken = listResponse.NextPageToken;
                    }
                    while (pageToken != null);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
        
        private bool CanPlay() => SelectedOperation != null && SelectedOperation.CurrentState == OperationViewModel.State.Completed && File.Exists(SelectedOperation.OutputFilePath);
        private void Play()
        {
            Process.Start(SelectedOperation.OutputFilePath);
        }

        private bool CanShowInExplorer() => SelectedOperation != null && SelectedOperation.CurrentState == OperationViewModel.State.Completed && File.Exists(SelectedOperation.OutputFilePath);
        private void ShowInExplorer()
        {
            Process.Start("explorer.exe", $"/select,\"{SelectedOperation.OutputFilePath}\"");
        }
        
        private bool CanShowOnYoutube() => SelectedOperation != null;
        private void ShowOnYoutube()
        {
            if (SelectedOperation != null)
                Process.Start(SelectedOperation.YoutubeVideoUrl);
        }
        
        private bool CanCancel() => SelectedOperation != null && SelectedOperation.IsRunning;
        private void Cancel()
        {
            SelectedOperation.Cancel();
        }

        private bool CanCancelAll() => Operations.Any(x => x.IsRunning);
        private void CancelAll()
        {
            if (_operationCancellation != null)
            {
                _operationCancellation.Cancel();
                _operationCancellation.Dispose();
                _operationCancellation = new CancellationTokenSource();
            }

            foreach (OperationViewModel operation in Operations)
                operation.Cancel();
        }

        private bool CanShowErrorMessage() => SelectedOperation != null && SelectedOperation.CurrentState == OperationViewModel.State.Failed;
        private void ShowErrorMessage()
        {
            var exceptionMessageBuilder = new StringBuilder();
            exceptionMessageBuilder.AppendLine(SelectedOperation.Exception.Message);
            exceptionMessageBuilder.AppendLine();
            exceptionMessageBuilder.AppendLine(SelectedOperation.Exception.StackTrace);

            MessageBox.Show(exceptionMessageBuilder.ToString(), "Error Message", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public async Task PreDisposeAsync()
        {
            if (_applicationCancellation != null)
            {
                _applicationCancellation.Cancel();
                _applicationCancellation.Dispose();
            }

            _downloadSpeedRefresh.Dispose();
            _contextualCommandsRefresh.Dispose();

            CancelAll();

            await Task.WhenAll(Tasks.Keys.ToArray());
        }

        public void Dispose()
        {
            _downloadSemaphore.Dispose();
        }
    }
}