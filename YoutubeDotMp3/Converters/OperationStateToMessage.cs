using YoutubeDotMp3.Converters.Base;
using YoutubeDotMp3.ViewModels;

namespace YoutubeDotMp3.Converters
{
    public class OperationStateToMessage : SimpleValueConverter<OperationViewModel.State, string>
    {
        protected override string Convert(OperationViewModel.State value)
        {
            switch (value)
            {
                case OperationViewModel.State.Initializing: return "Initializing...";
                case OperationViewModel.State.InQueue: return "Waiting in queue for others operations to end...";
                case OperationViewModel.State.DownloadingVideo: return "Downloading video...";
                case OperationViewModel.State.ConvertingToAudio: return "Converting video to audio file...";
                case OperationViewModel.State.Completed: return "Completed with success.";
                case OperationViewModel.State.Failed: return "Failed.";
                case OperationViewModel.State.Cancelling: return "Cancelling operation...";
                case OperationViewModel.State.Canceled: return "Cancelled by user.";
                default: return null;
            }
        }
    }
}