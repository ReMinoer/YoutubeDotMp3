﻿using YoutubeDotMp3.Converters.Base;
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
                case OperationViewModel.State.QueuedForVideoDownload: return "Download queue";
                case OperationViewModel.State.DownloadingVideo: return "Downloading video...";
                case OperationViewModel.State.QueuedForAudioExtraction: return "Extraction queue";
                case OperationViewModel.State.ExtractingAudio: return "Extracting audio...";
                case OperationViewModel.State.Completed: return "Completed";
                case OperationViewModel.State.Failed: return "Failed";
                case OperationViewModel.State.Cancelling: return "Cancelling...";
                case OperationViewModel.State.Canceled: return "Cancelled";
                default: return value.ToString();
            }
        }
    }
}