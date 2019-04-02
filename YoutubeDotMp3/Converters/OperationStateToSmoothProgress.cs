using YoutubeDotMp3.Converters.Base;
using YoutubeDotMp3.ViewModels;

namespace YoutubeDotMp3.Converters
{
    public class OperationStateToSmoothProgress : SimpleValueConverter<OperationViewModel.State, bool>
    {
        protected override bool Convert(OperationViewModel.State value)
        {
            return value == OperationViewModel.State.ExtractingAudio;
        }
    }
}