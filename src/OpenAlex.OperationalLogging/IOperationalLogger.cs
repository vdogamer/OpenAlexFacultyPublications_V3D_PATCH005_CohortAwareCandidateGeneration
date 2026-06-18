namespace OpenAlex.OperationalLogging;

public interface IOperationalLogger
{
    Task LogInfoAsync(string category, string eventName, string message, Guid? correlationId = null, CancellationToken cancellationToken = default);
    Task LogWarningAsync(string category, string eventName, string message, Guid? correlationId = null, CancellationToken cancellationToken = default);
    Task LogErrorAsync(string category, string eventName, string message, Exception? exception = null, Guid? correlationId = null, CancellationToken cancellationToken = default);
}
