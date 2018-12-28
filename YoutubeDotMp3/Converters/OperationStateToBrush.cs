using System.Windows.Media;
using YoutubeDotMp3.Converters.Base;
using YoutubeDotMp3.ViewModels;

namespace YoutubeDotMp3.Converters
{
    public class OperationStateToBrush : SimpleValueConverter<OperationViewModel.State, Brush>
    {
        protected override Brush Convert(OperationViewModel.State value)
        {
            switch (value)
            {
                case OperationViewModel.State.Initializing: return Brushes.RoyalBlue;
                case OperationViewModel.State.InQueue: return Brushes.LightSkyBlue;
                case OperationViewModel.State.DownloadingVideo: return Brushes.Gold;
                case OperationViewModel.State.ConvertingToAudio: return Brushes.Orange;
                case OperationViewModel.State.Completed: return Brushes.Green;
                case OperationViewModel.State.Failed: return Brushes.Red;
                case OperationViewModel.State.Cancelling: return Brushes.DarkGray;
                case OperationViewModel.State.Canceled: return Brushes.Gray;
                default: return Brushes.White;
            }
        }
    }
}