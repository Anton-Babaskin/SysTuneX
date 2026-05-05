namespace SysTuneX.Core.Services;

public interface IGameDetectionService
{
    event EventHandler<string> GameStarted;
    event EventHandler<string> GameStopped;
    void Start();
    void Stop();
}
