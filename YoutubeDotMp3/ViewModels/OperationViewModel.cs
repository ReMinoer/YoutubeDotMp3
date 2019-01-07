using System;
using System.IO;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using MediaToolkit.Model;
using VideoLibrary;
using YoutubeDotMp3.Utils;

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

        public const string OutputDirectory = MainViewModel.FriendlyApplicationName;
        static public string OutputDirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), OutputDirectory);
        
        private Subject<long> _downloadedBytesSubject;
        private CancellationTokenSource _cancellation = new CancellationTokenSource();
        
        public string YoutubeVideoUrl { get; }

        private YouTubeVideo _youtubeVideo;
        public YouTubeVideo YoutubeVideo
        {
            get => _youtubeVideo;
            private set => Set(ref _youtubeVideo, value);
        }

        private const string UnknownTitle = "<...>";
        private const string FailTitle = "<Invalid URL>";

        private string _title;
        public string Title
        {
            get => _title ?? UnknownTitle;
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
                
                CurrentStateChanged?.Invoke(this, _currentState);
            }
        }

        public EventHandler<State> CurrentStateChanged;
        public bool IsRunning => CurrentState != State.Completed && CurrentState != State.Failed && CurrentState != State.Cancelling && CurrentState != State.Canceled;
        
        private string _outputFilePath;
        public string OutputFilePath
        {
            get => _outputFilePath;
            private set => Set(ref _outputFilePath, value);
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

        private Exception _exception;
        public Exception Exception
        {
            get => _exception;
            private set => Set(ref _exception, value);
        }

        public OperationViewModel(string youtubeVideoUrl)
        {
            YoutubeVideoUrl = youtubeVideoUrl;
        }

        public async Task RunAsync(SemaphoreSlimQueued downloadSemaphore, SemaphoreSlimQueued conversionSemaphore)
        {
            CurrentState = State.Initializing;
            DownloadedBytes = 0;
            VideoSize = long.MaxValue;
            DownloadSpeed = 0;
            Exception = null;

            string videoTempFilePath = Path.GetTempFileName();
            try
            {
                CancellationToken cancellationToken = _cancellation.Token;
                Task downloadSemaphoreWaitTask = downloadSemaphore.WaitAsync(cancellationToken);
                await InitializeAsync(cancellationToken).ConfigureAwait(false);

                CurrentState = State.InQueue;
                await downloadSemaphoreWaitTask.ConfigureAwait(false);

                try
                {
                    CurrentState = State.DownloadingVideo;
                    await CreateValidFileAsync(cancellationToken).ConfigureAwait(false);
                    await DownloadAsync(YoutubeVideo, videoTempFilePath, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    downloadSemaphore.Release();
                }

                CurrentState = State.ConvertingToAudio;
                await conversionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    await ConvertAsync(videoTempFilePath, OutputFilePath, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    conversionSemaphore.Release();
                }

                CurrentState = State.Completed;
            }
            catch (OperationCanceledException)
            {
                CurrentState = State.Canceled;
            }
            catch (Exception ex)
            {
                Exception = ex;
                CurrentState = State.Failed;
            }
            finally
            {
                DownloadSpeed = 0;
                
                if (File.Exists(videoTempFilePath))
                    File.Delete(videoTempFilePath);

                if (CurrentState != State.Completed && File.Exists(OutputFilePath))
                    File.Delete(OutputFilePath);
            }
        }

        private async Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Title == UnknownTitle || Title == FailTitle)
                Title = null;

            if (YoutubeVideo == null)
            {
                try
                {
                    YoutubeVideo = await YouTube.Default.GetVideoAsync(YoutubeVideoUrl).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    Title = FailTitle;
                    throw;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (_title == null)
                Title = YoutubeVideo.Title.Substring(0, YoutubeVideo.Title.Length - " - Youtube".Length);
        }

        static private readonly SemaphoreSlimQueued ValidNameSemaphore = new SemaphoreSlimQueued(1);
        private async Task CreateValidFileAsync(CancellationToken cancellationToken)
        {
            string fileNameBase = Title;

            foreach (char invalidFileNameChar in Path.GetInvalidFileNameChars())
                fileNameBase = fileNameBase.Replace(invalidFileNameChar, '_');
            fileNameBase = fileNameBase.Replace('.', '_');

            const string fileExtension = ".mp3";
            string filePath = Path.Combine(OutputDirectoryPath, fileNameBase + fileExtension);

            await ValidNameSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                int i = 1;
                while (File.Exists(filePath))
                {
                    i++;
                    filePath = Path.Combine(OutputDirectoryPath, $"{fileNameBase} ({i})" + fileExtension);
                }

                if (!Directory.Exists(OutputDirectoryPath))
                    Directory.CreateDirectory(OutputDirectoryPath);

                File.Create(filePath);
            }
            finally
            {
                ValidNameSemaphore.Release();
            }

            OutputFilePath = filePath;
        }

        private async Task DownloadAsync(YouTubeVideo youtubeVideo, string videoOutputFilePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (_downloadedBytesSubject = new Subject<long>())
            using (_downloadedBytesSubject.Scan((size: 0L, speed: 0L), (previous, currentSize) => (currentSize, currentSize - previous.size))
                                          .Subscribe(current => DownloadSpeed = current.speed))
            {
                using (var httpClient = new HttpClient())
                {
                    string requestUri = await youtubeVideo.GetUriAsync().ConfigureAwait(false);
                    using (HttpResponseMessage response = await httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();

                        VideoSize = response.Content.Headers.ContentLength ?? throw new EndOfStreamException();

                        cancellationToken.ThrowIfCancellationRequested();
                        using (Stream videoInputStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            using (FileStream videoOutputFileStream = File.OpenWrite(videoOutputFilePath))
                            {
                                int readBytes;
                                var buffer = new byte[4096];
                                do
                                {
                                    readBytes = await videoInputStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                                    await videoOutputFileStream.WriteAsync(buffer, 0, readBytes, cancellationToken).ConfigureAwait(false);

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

        static private async Task ConvertAsync(string inputFilePath, string outputFilePath, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var videoFile = new MediaFile { Filename = inputFilePath };
                    var outputFile = new MediaFile { Filename = outputFilePath };

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

        public long RefreshDownloadSpeed()
        {
            if (_downloadedBytesSubject != null && !_downloadedBytesSubject.IsDisposed)
                _downloadedBytesSubject.OnNext(DownloadedBytes);
            return DownloadSpeed;
        }

        public void Cancel()
        {
            if (!IsRunning)
                return;

            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = new CancellationTokenSource();

            CurrentState = State.Cancelling;
        }
    }
}