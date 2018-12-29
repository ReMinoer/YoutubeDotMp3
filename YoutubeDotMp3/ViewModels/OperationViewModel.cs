using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MediaToolkit.Model;
using VideoLibrary;
using YoutubeDotMp3.ViewModels.Base;
using YoutubeDotMp3.ViewModels.Utils;

namespace YoutubeDotMp3.ViewModels
{
    public class OperationViewModel : NotifyPropertyChangedBase
    {
        public enum State
        {
            Initializing,
            InQueue,
            DownloadingVideo,
            ConvertingToAudio,
            Completed,
            Failed,
            Cancelling,
            Canceled
        }

        public const string OutputDirectory = nameof(YoutubeDotMp3);
        static public string OutputDirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), OutputDirectory);

        private const string YoutubeVideoAddressRegexPattern = @"^(?:https?:\/\/)?(?:(?:www\.)?youtube\.com\/watch\?.*v=([\w\-]*)?(?:\&.*)?.*|youtu\.be\/([\w\-]*)?:\?.*?)$";
        static private readonly Regex YoutubeVideoAddressRegex = new Regex(YoutubeVideoAddressRegexPattern, RegexOptions.Compiled);

        private string _outputFilePath;
        private Subject<long> _downloadedBytesSubject;
        private Exception _exception;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        public SimpleCommand[] Commands { get; }
        public SimpleCommand PlayCommand { get; }
        public SimpleCommand ShowInExplorerCommand { get; }
        public SimpleCommand ShowOnYoutubeCommand { get; }
        public SimpleCommand CancelCommand { get; }
        public SimpleCommand ShowErrorMessageCommand { get; }
        
        public string YoutubeVideoUrl { get; }

        private YouTubeVideo _youtubeVideo;
        public YouTubeVideo YoutubeVideo
        {
            get => _youtubeVideo;
            private set => Set(ref _youtubeVideo, value);
        }

        private string _title;
        public string Title
        {
            get => _title ?? "<...>";
            private set => Set(ref _title, value);
        }

        private State _currentState = State.Initializing;
        public State CurrentState
        {
            get => _currentState;
            private set
            {
                if (!Set(ref _currentState, value))
                    return;
                
                foreach (SimpleCommand command in Commands)
                    command.UpdateCanExecute();
            }
        }
        
        private long _downloadedBytes;
        public long DownloadedBytes
        {
            get => _downloadedBytes;
            private set => Set(ref _downloadedBytes, value);
        }

        private long _videoSize;
        public long VideoSize
        {
            get => _videoSize;
            private set => Set(ref _videoSize, value);
        }

        private long _downloadSpeed;
        public long DownloadSpeed
        {
            get => _downloadSpeed;
            private set => Set(ref _downloadSpeed, value);
        }

        private OperationViewModel(string youtubeVideoUrl)
        {
            YoutubeVideoUrl = youtubeVideoUrl;

            Commands = new []
            {
                PlayCommand = new SimpleCommand(Play, CanPlay),
                ShowInExplorerCommand = new SimpleCommand(ShowInExplorer, CanShowInExplorer),
                ShowOnYoutubeCommand = new SimpleCommand(ShowOnYoutube),
                CancelCommand = new SimpleCommand(Cancel, CanCancel),
                ShowErrorMessageCommand = new SimpleCommand(ShowErrorMessage, CanShowErrorMessage)
            };
        }

        static public OperationViewModel FromYoutubeUri(string youtubeVideoUrl)
        {
            return YoutubeVideoAddressRegex.IsMatch(youtubeVideoUrl) ? new OperationViewModel(youtubeVideoUrl) : null;
        }

        public async Task<bool> InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellation.Token).Token;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                try
                {
                    YoutubeVideo = await YouTube.Default.GetVideoAsync(YoutubeVideoUrl);
                }
                catch (InvalidOperationException ex)
                {
                    Title = "<Invalid URL>";
                    _exception = ex;
                    CurrentState = State.Failed;
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();

                Title = YoutubeVideo.Title.Substring(0, YoutubeVideo.Title.Length - " - Youtube".Length);

                _outputFilePath = GetValidFileName(OutputDirectoryPath, Title, ".mp3");
                File.Create(_outputFilePath);

                CurrentState = State.InQueue;
            }
            catch (OperationCanceledException)
            {
                CurrentState = State.Canceled;
                return false;
            }
            catch (Exception ex)
            {
                _exception = ex;
                CurrentState = State.Failed;
                return false;
            }

            return true;
        }

        public async Task RunAsync(CancellationToken cancellationToken, SemaphoreSlimQueued downloadSemaphore, Task downloadSemaphoreWaitTask)
        {
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellation.Token).Token;
            cancellationToken.ThrowIfCancellationRequested();
            
            string videoTempFilePath = Path.GetTempFileName();
            try
            {
                await downloadSemaphoreWaitTask;
                try
                {
                    CurrentState = State.DownloadingVideo;
                    await DownloadAsync(YoutubeVideo, videoTempFilePath, cancellationToken);
                }
                finally
                {
                    downloadSemaphore.Release();
                    downloadSemaphore = null;
                }

                CurrentState = State.ConvertingToAudio;
                await ConvertAsync(videoTempFilePath, _outputFilePath, cancellationToken);

                CurrentState = State.Completed;
            }
            catch (OperationCanceledException)
            {
                CurrentState = State.Canceled;
            }
            catch (Exception ex)
            {
                _exception = ex;
                CurrentState = State.Failed;
            }
            finally
            {
                if (downloadSemaphore != null)
                {
                    if (!downloadSemaphoreWaitTask.IsCompleted)
                        await downloadSemaphoreWaitTask;
                    downloadSemaphore.Release();
                }

                if (File.Exists(videoTempFilePath))
                    File.Delete(videoTempFilePath);

                if (CurrentState != State.Completed && File.Exists(_outputFilePath))
                    File.Delete(_outputFilePath);
            }
        }

        private async Task DownloadAsync(YouTubeVideo youtubeVideo, string videoOutputFilePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DownloadedBytes = 0;
            VideoSize = long.MaxValue;

            using (_downloadedBytesSubject = new Subject<long>())
            using (_downloadedBytesSubject.Scan((size: 0L, speed: 0L), (previous, currentSize) => (currentSize, currentSize - previous.size))
                                          .Subscribe(current => DownloadSpeed = current.speed))
            {
                using (var httpClient = new HttpClient())
                {
                    string requestUri = await youtubeVideo.GetUriAsync();
                    using (HttpResponseMessage response = await httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();

                        VideoSize = response.Content.Headers.ContentLength ?? throw new EndOfStreamException();

                        cancellationToken.ThrowIfCancellationRequested();
                        using (Stream videoInputStream = await response.Content.ReadAsStreamAsync())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            using (FileStream videoOutputFileStream = File.OpenWrite(videoOutputFilePath))
                            {
                                int readBytes;
                                var buffer = new byte[4096];
                                do
                                {
                                    readBytes = await videoInputStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                                    await videoOutputFileStream.WriteAsync(buffer, 0, readBytes, cancellationToken);

                                    DownloadedBytes += readBytes;
                                }
                                while (readBytes != 0);
                            }
                        }
                    }
                }
            }

            _downloadedBytesSubject = null;
            DownloadSpeed = 0;
        }

        public long RefreshDownloadSpeed()
        {
            if (_downloadedBytesSubject != null && !_downloadedBytesSubject.IsDisposed)
                _downloadedBytesSubject.OnNext(DownloadedBytes);
            return DownloadSpeed;
        }

        static private async Task ConvertAsync(string inputFilePath, string outputFilePath, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var videoFile = new MediaFile { Filename = inputFilePath };
                    var outputFile = new MediaFile { Filename = outputFilePath };

                    if (!Directory.Exists(OutputDirectoryPath))
                        Directory.CreateDirectory(OutputDirectoryPath);

                    cancellationToken.ThrowIfCancellationRequested();

                    using (var engine = new MediaToolkit.Engine())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        engine.GetMetadata(videoFile);

                        cancellationToken.ThrowIfCancellationRequested();

                        engine.Convert(videoFile, outputFile);

                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
        }
        
        private bool CanCancel() => CurrentState != State.Completed && CurrentState != State.Failed && CurrentState != State.Cancelling && CurrentState != State.Canceled;
        public void Cancel()
        {
            _cancellation.Cancel();

            switch (CurrentState)
            {
                case State.Initializing:
                case State.ConvertingToAudio:
                case State.DownloadingVideo:
                case State.Cancelling:
                    CurrentState = State.Cancelling;
                    break;
                case State.InQueue:
                case State.Canceled:
                    CurrentState = State.Canceled;
                    break;
            }
        }
        
        public bool CanPlay() => CurrentState == State.Completed && File.Exists(_outputFilePath);
        public void Play()
        {
            Process.Start(_outputFilePath);
        }

        private bool CanShowInExplorer() => CurrentState == State.Completed && File.Exists(_outputFilePath);
        private void ShowInExplorer()
        {
            Process.Start("explorer.exe", $"/select,\"{_outputFilePath}\"");
        }
        
        private void ShowOnYoutube()
        {
            Process.Start(YoutubeVideoUrl);
        }

        private bool CanShowErrorMessage() => CurrentState == State.Failed;
        private void ShowErrorMessage()
        {
            var exceptionMessageBuilder = new StringBuilder();
            exceptionMessageBuilder.AppendLine(_exception.Message);
            exceptionMessageBuilder.AppendLine();
            exceptionMessageBuilder.AppendLine(_exception.StackTrace);

            MessageBox.Show(exceptionMessageBuilder.ToString(), "Error Message", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        static private string GetValidFileName(string directory, string title, string extension)
        {
            foreach (char invalidFileNameChar in Path.GetInvalidFileNameChars())
                title = title.Replace(invalidFileNameChar, '_');

            string validFileName = title;
            string result = Path.Combine(directory, title + extension);

            if (!File.Exists(result))
                return result;

            int i = 1;
            do
            {
                i++;
                title = $"{validFileName} ({i})";
                result = Path.Combine(directory, title + extension);
            }
            while (File.Exists(result));

            return result;
        }
    }
}