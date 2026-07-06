namespace Relay.Logging;

public interface IRelayLogger
{
    void Log(LogVerbosity level, string message);
}
