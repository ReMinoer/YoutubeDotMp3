using System;
using System.Threading;

namespace YoutubeDotMp3.ViewModels.Utils
{
    static public class Throw
    {
        static public void IfTaskCancelled(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
        }
    }
}