using YoutubeDotMp3.Converters.Base;
using YoutubeDotMp3.ViewModels;

namespace YoutubeDotMp3.Converters
{
    public class OperationStateToDisplayText : SimpleValueConverter<OperationViewModel.State, string>
    {
        protected override string Convert(OperationViewModel.State value)
        {
            switch (value)
            {
                case OperationViewModel.State.Initializing: return "Initializing...";
                case OperationViewModel.State.InQueue: return "In queue";
                case OperationViewModel.State.DownloadingVideo: return "Downloading video...";
                case OperationViewModel.State.ConvertingToAudio: return "Converting to audio...";
                case OperationViewModel.State.Completed: return "Completed";
                case OperationViewModel.State.Failed: return "Failed";
                case OperationViewModel.State.Cancelling: return "Cancelling...";
                case OperationViewModel.State.Canceled: return "Cancelled";
                default: return value.ToString();
            }
        }
    }
}