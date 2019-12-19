using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using VideoLibrary;
using Xabe.FFmpeg;
using YoutubeDotMp3.Utils;

namespace YoutubeDotMp3.ViewModels
{
    public class OperationViewModel : NotifyPropertyChangedBase
    {
        public enum State
        {
            Initializing,
            QueuedForVideoDownload,
            DownloadingVideo,
            QueuedForAudioExtraction,
            ExtractingAudio,
            Completed,
            Failed,
            Cancelling,
            Canceled
        }

        public enum OutputFormat
        {
            Aac,
            Mp3,
            Wma,
            Wav,
            Flac,
            Ogg
        }

        static private readonly SemaphoreSlimQueued ValidNameSemaphore = new SemaphoreSlimQueued(1);

        public const string OutputDirectory = MainViewModel.FriendlyApplicationName;
        static public string OutputDirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), OutputDirectory);
        
        private Subject<long> _downloadedBytesSubject;
        private CancellationTokenSource _cancellation = new CancellationTokenSource();
        
        public string YoutubeVideoUrl { get; }
        public OutputFormat OutputFileFormat { get; }

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

        public event EventHandler<State> CurrentStateChanged;
        public bool IsRunning => CurrentState != State.Completed && CurrentState != State.Failed && CurrentState != State.Cancelling && CurrentState != State.Canceled;
        
        private string _outputFileName;
        private string _outputFilePathTemp;
        private string _outputFilePath;
        public string OutputFilePath
        {
            get => _outputFilePath;
            private set => Set(ref _outputFilePath, value);
        }

        private long _progress;
        public long Progress
        {
            get => _progress;
            private set => Set(ref _progress, value);
        }

        private long _progressMax;
        public long ProgressMax
        {
            get => _progressMax;
            private set => Set(ref _progressMax, value);
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

        public OperationViewModel(string youtubeVideoUrl, OutputFormat outputFileFormat)
        {
            YoutubeVideoUrl = youtubeVideoUrl;
            OutputFileFormat = outputFileFormat;
        }

        public async Task RunAsync(SemaphoreSlimQueued downloadSemaphore, SemaphoreSlimQueued conversionSemaphore)
        {
            CurrentState = State.Initializing;
            Progress = 0;
            ProgressMax = long.MaxValue;
            DownloadSpeed = 0;
            Exception = null;

            string videoTempFilePath = Path.GetTempFileName();

            CancellationToken cancellationToken = _cancellation.Token;
            var downloadLock = new SemaphoreLock(downloadSemaphore, cancellationToken);
            SemaphoreLock convertLock = null;

            try
            {
                await InitializeAsync(cancellationToken).ConfigureAwait(false);

                CurrentState = State.QueuedForVideoDownload;
                using (await downloadLock)
                {
                    CurrentState = State.DownloadingVideo;
                    {
                        await CreateValidFileAsync(cancellationToken).ConfigureAwait(false);
                        await DownloadAsync(YoutubeVideo, videoTempFilePath, cancellationToken).ConfigureAwait(false);
                    }
                }

                Progress = 0;
                ProgressMax = long.MaxValue;
                convertLock = new SemaphoreLock(conversionSemaphore, cancellationToken);

                CurrentState = State.QueuedForAudioExtraction;
                using (await convertLock)
                {
                    CurrentState = State.ExtractingAudio;
                    {
                        await ConvertAsync(videoTempFilePath, _outputFileName, cancellationToken).ConfigureAwait(false);
                    }
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

                downloadLock.Dispose();
                convertLock?.Dispose();

                if (File.Exists(_outputFilePathTemp))
                    File.Delete(_outputFilePathTemp);
                
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

        private async Task CreateValidFileAsync(CancellationToken cancellationToken)
        {
            string fileNameBase = Title;

            foreach (char invalidFileNameChar in Path.GetInvalidFileNameChars())
                fileNameBase = fileNameBase.Replace(invalidFileNameChar, '_');
            fileNameBase = fileNameBase.Replace('.', '_');

            string fileName = fileNameBase;

            using (await new SemaphoreLock(ValidNameSemaphore, cancellationToken))
            {
                if (!Directory.Exists(OutputDirectoryPath))
                    Directory.CreateDirectory(OutputDirectoryPath);
                else
                {
                    int i = 1;
                    while (Directory.GetFiles(OutputDirectoryPath, $"{fileName}.*").Any())
                    {
                        i++;
                        fileName = $"{fileNameBase} ({i})";
                    }
                }

                _outputFilePathTemp = Path.Combine(OutputDirectoryPath, fileName + ".tmp");
                File.Create(_outputFilePathTemp).Close();
            }

            _outputFileName = fileName;
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

                        ProgressMax = response.Content.Headers.ContentLength ?? throw new EndOfStreamException();

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

                                    Progress += readBytes;
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

        private async Task ConvertAsync(string inputFilePath, string outputFileName, CancellationToken cancellationToken)
        {
            OutputFilePath = Path.Combine(OutputDirectoryPath, outputFileName + "." + OutputFileFormat.ToString().ToLowerInvariant());

            IConversion extractAudio = Conversion.ExtractAudio(inputFilePath, OutputFilePath).SetOverwriteOutput(true);
            extractAudio.OnProgress += (sender, e) =>
            {
                Progress = e.Duration.Ticks;
                ProgressMax = e.TotalLength.Ticks;
            };

            await extractAudio.Start(cancellationToken);
        }

        public long RefreshDownloadSpeed()
        {
            if (_downloadedBytesSubject != null && !_downloadedBytesSubject.IsDisposed)
                _downloadedBytesSubject.OnNext(Progress);
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