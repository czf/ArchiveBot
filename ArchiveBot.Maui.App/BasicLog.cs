using Microsoft.Extensions.Logging;
using Czf.Domain.NewsBankWrapper.Interfaces;

namespace ArchiveBot.Maui.App;
public class BasicLog : ILogger , ICanLog
{
    public class EmptyDisposable : IDisposable
    {
        public void Dispose()
        {}
    }
    public IDisposable BeginScope<TState>(TState state)
    {
        return new EmptyDisposable();
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        switch (logLevel)
        {
            case LogLevel.Trace:
            case LogLevel.None:
                return false;
            case LogLevel.Debug:
            case LogLevel.Information:
            case LogLevel.Warning:
            case LogLevel.Error:
            case LogLevel.Critical:
                return true;
                    
            default:
                break;
        }
        return false;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        if (IsEnabled(logLevel) && state is string)
        {

            Console.WriteLine(state.ToString());
        }
    }

    public void Error(string message)
    {
        Console.WriteLine(message);
    }

    public void Info(string message)
    {
        Console.WriteLine(message);
    }
}
    