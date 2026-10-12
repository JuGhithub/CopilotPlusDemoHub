namespace CopilotPlusDemoHub.Core;

public sealed class ConsoleSpinner : IDisposable
{
    private readonly string[] _frames =
    {
        "⠋",
        "⠙",
        "⠹",
        "⠸",
        "⠼",
        "⠴",
        "⠦",
        "⠧",
        "⠇",
        "⠏"
    };

    private readonly string _message;

    private CancellationTokenSource? _cts;
    private Task? _animationTask;

    public ConsoleSpinner(
        string message)
    {
        _message = message;
    }

    public void Start()
    {
        _cts =
            new CancellationTokenSource();

        _animationTask =
            Task.Run(
                async () =>
                {
                    int frameIndex = 0;

                    while (!_cts.IsCancellationRequested)
                    {
                        Console.Write(
                            "\r" +
                            _frames[frameIndex] +
                            " " +
                            _message +
                            "   ");

                        frameIndex++;

                        if (frameIndex >= _frames.Length)
                        {
                            frameIndex = 0;
                        }

                        try
                        {
                            await Task.Delay(
                                100,
                                _cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                });
    }

    public void Stop(
        string completionMessage =
            "✔ Terminé")
    {
        if (_cts == null)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            _animationTask?.Wait();
        }
        catch
        {
        }

        Console.Write("\r");

        if (!string.IsNullOrWhiteSpace(completionMessage))
        {
            Console.WriteLine(
                completionMessage +
                new string(' ', 20));
        }
        else
        {
            Console.Write(new string(' ', _message.Length + 20) + "\r");
        }
    }

    public void Clear()
    {
        Stop(string.Empty);
    }

    public void Dispose()
    {
        Stop();
    }
}