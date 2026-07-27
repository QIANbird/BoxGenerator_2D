using System;
using System.Collections;
using System.Threading;

public sealed class AITextureGenerationCancellationToken
{
    private int cancellationRequested;

    public bool IsCancellationRequested
    {
        get
        {
            return Volatile.Read(ref cancellationRequested) != 0;
        }
    }

    internal void Cancel()
    {
        Interlocked.Exchange(ref cancellationRequested, 1);
    }
}

public sealed class AITextureGenerationCancellationSource
{
    public AITextureGenerationCancellationToken Token { get; } =
        new AITextureGenerationCancellationToken();

    public void Cancel()
    {
        Token.Cancel();
    }
}

public interface IAITextureGenerationService
{
    string ServiceName { get; }

    IEnumerator Generate(
        AITextureGenerationRequest request,
        AITextureGenerationCancellationToken cancellationToken,
        Action<AITextureGenerationResult> onCompleted);

    void Cancel(string requestId);
}
