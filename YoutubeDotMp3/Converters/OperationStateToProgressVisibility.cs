using System.Windows;
using YoutubeDotMp3.Converters.Base;
using YoutubeDotMp3.ViewModels;

namespace YoutubeDotMp3.Converters
{
    public class OperationStateToProgressVisibility : SimpleValueConverter<OperationViewModel.State, Visibility>
    {
        protected override Visibility Convert(OperationViewModel.State value)
        {
            return value == OperationViewModel.State.DownloadingVideo || value == OperationViewModel.State.ConvertingToAudio
                ? Visibility.Visible
                : Visibility.Hidden;
        }
    }
}