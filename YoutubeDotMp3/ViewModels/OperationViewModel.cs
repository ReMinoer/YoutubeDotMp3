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

        private const string YoutubeVideoAdressRegexPattern = @"^(?:https?:\/\/)?(?:(?:www\.)?youtube\.com\/watch\?v=[\w\-]*(?:\&.*)?|youtu\.be\/([\w\-]*)?:\?.*?)$";
        static private readonly Regex YoutubeVideoAdressRegex = new Regex(YoutubeVideoAdressRegexPattern, RegexOptions.Compiled);
        
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
            set => Set(ref _currentState, value);
        }

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
            if (!YoutubeVideoAdressRegex.IsMatch(youtubeUri))
                return null;

            YouTubeVideo youtubeVideo = YouTube.Default.GetVideo(youtubeUri);
            return youtubeVideo != null ? new OperationViewModel(youtubeVideo) : null;
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
                await DownloadSync(youtubeVideo, videoTempFilePath, cancellationToken);

                CurrentState = State.ConvertingToAudio;
                await ConvertSync(videoTempFilePath, outputFilePath, cancellationToken);

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

        static private async Task DownloadSync(YouTubeVideo youtubeVideo, string videoOutputFilePath, CancellationToken cancellationToken)
        {
            byte[] buffer = await youtubeVideo.GetBytesAsync();

            Throw.IfTaskCancelled(cancellationToken);

            using (FileStream tempVideoFileStream = File.OpenWrite(videoOutputFilePath))
                await tempVideoFileStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
        }

        static private async Task ConvertSync(string inputFilePath, string outputFilePath, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                Throw.IfTaskCancelled(cancellationToken);

                var videoFile = new MediaFile { Filename = inputFilePath };
                var outputFile = new MediaFile { Filename = outputFilePath };

                if (!Directory.Exists(OutputDirectoryPath))
                    Directory.CreateDirectory(OutputDirectoryPath);

                Throw.IfTaskCancelled(cancellationToken);

                using (var engine = new MediaToolkit.Engine())
                {
                    Throw.IfTaskCancelled(cancellationToken);

                    engine.GetMetadata(videoFile);

                    Throw.IfTaskCancelled(cancellationToken);

                    engine.Convert(videoFile, outputFile);

                    Throw.IfTaskCancelled(cancellationToken);
                }
            }, cancellationToken);
        }

        static private string GetValidFileName(string directory, string title, string extension)
        {
            foreach (char invalidFileNameChar in Path.GetInvalidPathChars())
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