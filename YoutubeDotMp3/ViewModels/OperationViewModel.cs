using System;
using System.Diagnostics;
using System.IO;
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

        private const string YoutubeVideoAddressRegexPattern = @"^(?:https?:\/\/)?(?:(?:www\.)?youtube\.com\/watch\?v=[\w\-]*(?:\&.*)?|youtu\.be\/([\w\-]*)?:\?.*?)$";
        static private readonly Regex YoutubeVideoAddressRegex = new Regex(YoutubeVideoAddressRegexPattern, RegexOptions.Compiled);
        
        private string _outputFilePath;
        private string _exceptionMessage;
        private readonly CancellationTokenSource _cancellation;

        public SimpleCommand[] Commands { get; }
        public SimpleCommand PlayCommand { get; }
        public SimpleCommand ShowInExplorerCommand { get; }
        public SimpleCommand CancelCommand { get; }
        public SimpleCommand ShowErrorMessageCommand { get; }
        
        public string YoutubeVideoUri { get; }
        public YouTubeVideo YoutubeVideo { get; private set; }

        private string _title;
        public string Title
        {
            get => _title ?? "<...>";
            set => Set(ref _title, value);
        }

        private State _currentState = State.Initializing;
        public State CurrentState
        {
            get => _currentState;
            set
            {
                if (!Set(ref _currentState, value))
                    return;
                
                foreach (SimpleCommand command in Commands)
                    command.UpdateCanExecute();
            }
        }

        private OperationViewModel(string youtubeVideoUri)
        {
            YoutubeVideoUri = youtubeVideoUri;

            Commands = new []
            {
                PlayCommand = new SimpleCommand(Play, CanPlay),
                ShowInExplorerCommand = new SimpleCommand(ShowInExplorer, CanShowInExplorer),
                CancelCommand = new SimpleCommand(Cancel, CanCancel),
                ShowErrorMessageCommand = new SimpleCommand(ShowErrorMessage, CanShowErrorMessage)
            };

            _cancellation = new CancellationTokenSource();
        }

        static public OperationViewModel FromYoutubeUri(string youtubeVideoUri)
        {
            return YoutubeVideoAddressRegex.IsMatch(youtubeVideoUri) ? new OperationViewModel(youtubeVideoUri) : null;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellation.Token).Token;
            cancellationToken.ThrowIfCancellationRequested();
            
            YoutubeVideo = await YouTube.Default.GetVideoAsync(YoutubeVideoUri);

            cancellationToken.ThrowIfCancellationRequested();

            Title = YoutubeVideo.Title.Substring(0, YoutubeVideo.Title.Length - " - Youtube".Length);

            _outputFilePath = GetValidFileName(OutputDirectoryPath, Title, ".mp3");
            File.Create(_outputFilePath);

            CurrentState = State.InQueue;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellation.Token).Token;
            cancellationToken.ThrowIfCancellationRequested();

            string videoTempFilePath = Path.GetTempFileName();

            try
            {
                CurrentState = State.DownloadingVideo;
                await DownloadAsync(YoutubeVideo, videoTempFilePath, cancellationToken);

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
                var exceptionMessageBuilder = new StringBuilder();
                exceptionMessageBuilder.AppendLine(ex.Message);
                exceptionMessageBuilder.AppendLine();
                exceptionMessageBuilder.AppendLine(ex.StackTrace);

                _exceptionMessage = exceptionMessageBuilder.ToString();

                CurrentState = State.Failed;
            }
            finally
            {
                if (File.Exists(videoTempFilePath))
                    File.Delete(videoTempFilePath);

                if (CurrentState != State.Completed && File.Exists(_outputFilePath))
                    File.Delete(_outputFilePath);
            }
        }

        static private async Task DownloadAsync(YouTubeVideo youtubeVideo, string videoOutputFilePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
        
        private bool CanPlay() => CurrentState == State.Completed && File.Exists(_outputFilePath);
        private void Play()
        {
            Process.Start(_outputFilePath);
        }

        private bool CanShowInExplorer() => CurrentState == State.Completed && File.Exists(_outputFilePath);
        private void ShowInExplorer()
        {
            Process.Start("explorer.exe", $"/select,\"{_outputFilePath}\"");
        }

        private bool CanShowErrorMessage() => CurrentState == State.Failed;
        private void ShowErrorMessage()
        {
            MessageBox.Show(_exceptionMessage, "Error Message", MessageBoxButton.OK, MessageBoxImage.Error);
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