using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace YoutubeDotMp3.Utils
{
    static public class TaskExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // ReSharper disable once UnusedParameter.Global
        static public void Forget(this Task task)
        {
        }
    }
}