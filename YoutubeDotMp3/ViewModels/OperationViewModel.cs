using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
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
            Starting,
            DownloadingVideo,
            ConvertingToAudio,
            Completed,
            Failed,
            Canceled
        }

        public const string OutputDirectory = nameof(YoutubeDotMp3);
        static public string OutputDirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), OutputDirectory);

        private const string YoutubeVideoAddressRegexPattern = @"^(?:https?:\/\/)?(?:(?:www\.)?youtube\.com\/watch\?v=[\w\-]*(?:\&.*)?|youtu\.be\/([\w\-]*)?:\?.*?)$";
        static private readonly Regex YoutubeVideoAddressRegex = new Regex(YoutubeVideoAddressRegexPattern, RegexOptions.Compiled);
        
        public YouTubeVideo YoutubeVideo { get; }
        public ICommand CancelCommand { get; }

        private string _title;
        public string Title
        {
            get => _title;
            set => Set(ref _title, value);
        }

        private State _currentState = State.Starting;
        public State CurrentState
        {
            get => _currentState;
            set
            {
                if (!Set(ref _currentState, value))
                    return;
                
                NotifyPropertyChanged(nameof(CurrentStateText));
                NotifyPropertyChanged(nameof(Message));
            }
        }
        
        public string CurrentStateText => GetStateDisplayText(CurrentState);
        public string Message => GetStateMessage(CurrentState);

        private readonly CancellationTokenSource _cancellation;

        private OperationViewModel(YouTubeVideo youtubeVideo)
        {
            YoutubeVideo = youtubeVideo;
            Title = youtubeVideo.Title;
            Title = Title.Substring(0, Title.Length - " - Youtube".Length);

            CancelCommand = new SimpleCommand(Cancel);

            _cancellation = new CancellationTokenSource();
        }

        static public OperationViewModel FromYoutubeUri(string youtubeUri)
        {
            if (!YoutubeVideoAddressRegex.IsMatch(youtubeUri))
                return null;

            try
            {
                YouTubeVideo youtubeVideo = YouTube.Default.GetVideo(youtubeUri);
                return youtubeVideo != null ? new OperationViewModel(youtubeVideo) : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            YouTubeVideo youtubeVideo = YoutubeVideo;
            string outputDirectoryPath = OutputDirectoryPath;
            string videoTempFilePath = Path.GetTempFileName();
            string outputFilePath = GetValidFileName(outputDirectoryPath, Title, ".mp3");

            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellation.Token).Token;

            try
            {
                CurrentState = State.DownloadingVideo;
                await DownloadAsync(youtubeVideo, videoTempFilePath, cancellationToken);

                CurrentState = State.ConvertingToAudio;
                await ConvertAsync(videoTempFilePath, outputFilePath, cancellationToken);

                CurrentState = State.Completed;
            }
            catch (OperationCanceledException)
            {
                CurrentState = State.Canceled;
            }
            catch (Exception)
            {
                CurrentState = State.Failed;
            }
            finally
            {
                if (File.Exists(videoTempFilePath))
                    File.Delete(videoTempFilePath);

                if (CurrentState != State.Completed && File.Exists(outputFilePath))
                    File.Delete(outputFilePath);
            }
        }

        public void Cancel()
        {
            CurrentState = State.Canceled;
            _cancellation.Cancel();
        }

        static private async Task DownloadAsync(YouTubeVideo youtubeVideo, string videoOutputFilePath, CancellationToken cancellationToken)
        {
            byte[] buffer = await youtubeVideo.GetBytesAsync();

            cancellationToken.ThrowIfCancellationRequested();

            using (FileStream tempVideoFileStream = File.OpenWrite(videoOutputFilePath))
                await tempVideoFileStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
        }

        static private async Task ConvertAsync(string inputFilePath, string outputFilePath, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
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
            }, cancellationToken);
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

        private string GetStateMessage(State currentState)
        {
            switch (currentState)
            {
                case State.Starting: return "Starting...";
                case State.DownloadingVideo: return "Downloading video...";
                case State.ConvertingToAudio: return "Converting video to audio file...";
                case State.Completed: return "Completed with success.";
                case State.Failed: return "Failed.";
                case State.Canceled: return "Cancelled by user.";
                default: return null;
            }
        }

        private string GetStateDisplayText(State currentState)
        {
            switch (currentState)
            {
                case State.Starting: return "Starting...";
                case State.DownloadingVideo: return "Downloading video...";
                case State.ConvertingToAudio: return "Converting to audio...";
                case State.Completed: return "Completed";
                case State.Failed: return "Failed";
                case State.Canceled: return "Cancelled";
                default: return currentState.ToString();
            }
        }
    }
}