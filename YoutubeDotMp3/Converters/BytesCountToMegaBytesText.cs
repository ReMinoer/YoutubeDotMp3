using YoutubeDotMp3.Converters.Base;

namespace YoutubeDotMp3.Converters
{
    public class BytesCountToMegaBytesText : SimpleValueConverter<long, string>
    {
        protected override string Convert(long value)
        {
            return ((float)value / 1000000).ToString("F1");
        }
    }
}