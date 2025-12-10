using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;
using AasSharpClient.Models.Messages;
using MAS_BT.Core;

namespace MAS_BT.Services;

/// <summary>
/// Logger Provider der automatisch Logs via MQTT sendet
/// </summary>
public class MqttLoggerProvider : ILoggerProvider
{
    private readonly MessagingClient? _messagingClient;
    private readonly Func<string> _agentIdProvider;
    private readonly Func<string> _agentRoleProvider;
    private readonly bool _publishLogs;
    private readonly ConcurrentDictionary<string, MqttLogger> _loggers = new();
    
    public MqttLoggerProvider(
        MessagingClient? messagingClient,
        Func<string> agentIdProvider,
        Func<string> agentRoleProvider,
        bool publishLogs = true)
    {
        _messagingClient = messagingClient;
        _agentIdProvider = agentIdProvider ?? throw new ArgumentNullException(nameof(agentIdProvider));
        _agentRoleProvider = agentRoleProvider ?? throw new ArgumentNullException(nameof(agentRoleProvider));
        _publishLogs = publishLogs;
    }
    
    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => 
            new MqttLogger(name, _messagingClient, _agentIdProvider, _agentRoleProvider, _publishLogs));
    }
    
    public void Dispose()
    {
        _loggers.Clear();
    }
}

/// <summary>
/// Logger der automatisch Logs via MQTT publiziert
/// </summary>
public class MqttLogger : ILogger
{
    private readonly string _categoryName;
    private readonly MessagingClient? _messagingClient;
    private readonly Func<string> _agentIdProvider;
    private readonly Func<string> _agentRoleProvider;
    private readonly bool _publishLogs;
    
    public MqttLogger(
        string categoryName,
        MessagingClient? messagingClient,
        Func<string> agentIdProvider,
        Func<string> agentRoleProvider,
        bool publishLogs)
    {
        _categoryName = categoryName;
        _messagingClient = messagingClient;
        _agentIdProvider = agentIdProvider;
        _agentRoleProvider = agentRoleProvider;
        _publishLogs = publishLogs;
    }
    
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;
    
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;
        
        var message = formatter(state, exception);
        
        // Konsolen-Output (wie bisher)
        var logLevelString = GetLogLevelString(logLevel);
        Console.WriteLine($"{logLevelString}: {_categoryName}[{eventId.Id}]");
        Console.WriteLine($"      {message}");
        
        if (exception != null)
        {
            Console.WriteLine($"      Exception: {exception}");
        }
        
        // MQTT-Output (Debug and above)
        if (_publishLogs && logLevel >= LogLevel.Debug && _messagingClient != null && _messagingClient.IsConnected)
        {
            _ = SendLogMessageAsync(logLevel, message);
        }
    }
    
    private async Task SendLogMessageAsync(LogLevel logLevel, string message)
    {
        try
        {
            var agentId = _agentIdProvider();
            var agentRole = _agentRoleProvider();

            var i40Message = CreateI40LogMessage(logLevel, message, agentId, agentRole);
            var topic = $"{agentId}/logs";
            await _messagingClient!.PublishAsync(i40Message, topic);
        }
        catch (Exception ex)
        {
            // Fehler beim MQTT-Senden nicht eskalieren
            Console.WriteLine($"warn: Failed to send log via MQTT: {ex.Message}");
        }
    }
    
    private I40Message CreateI40LogMessage(LogLevel logLevel, string message, string agentId, string agentRole)
    {
        var logCollection = new LogMessage(
            GetLogLevelName(logLevel),
            message,
            agentRole,
            agentId);

        var builder = new I40MessageBuilder()
            .From(agentId, agentRole)
            .To("broadcast", string.Empty)
            .WithType(I40MessageTypes.INFORM)
            .WithConversationId(Guid.NewGuid().ToString())
            .AddElement(logCollection);

        return builder.Build();
    }
    
    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "none"
        };
    }
    
    private static string GetLogLevelName(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARNING",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRITICAL",
            _ => "NONE"
        };
    }
}
