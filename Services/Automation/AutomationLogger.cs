using Serilog;
using Serilog.Core;

namespace JarvisCSharp.Services.Automation
{
    /// <summary>
    /// Specialized logger for automation actions.
    /// Provides separate log files and formatting for automation-specific events.
    /// </summary>
    public static class AutomationLogger
    {
        private static Logger? _logger;
        private static readonly object _lock = new();

        /// <summary>
        /// Initialize the automation logger with dedicated log files.
        /// </summary>
        public static void Initialize()
        {
            lock (_lock)
            {
                if (_logger != null)
                    return;

                _logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] [AUTOMATION] {Message:lj}{NewLine}{Exception}")
                    .WriteTo.File(
                        "logs/automation-.txt",
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                        fileSizeLimitBytes: 10_485_760, // 10 MB
                        retainedFileCountLimit: 5
                    )
                    .CreateLogger();
            }
        }

        /// <summary>
        /// Log an automation action execution.
        /// </summary>
        public static void LogAction(string actionType, string targetApp, Dictionary<string, object> parameters, bool success, TimeSpan duration)
        {
            EnsureInitialized();
            
            var status = success ? "SUCCESS" : "FAILED";
            var paramStr = string.Join(", ", parameters.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            
            _logger!.Information(
                "[{Status}] {ActionType} on {TargetApp} | Params: {Parameters} | Duration: {Duration}ms",
                status, actionType, targetApp, paramStr, duration.TotalMilliseconds
            );
        }

        /// <summary>
        /// Log a workflow execution.
        /// </summary>
        public static void LogWorkflow(string workflowName, int stepCount, bool success, TimeSpan duration)
        {
            EnsureInitialized();
            
            var status = success ? "SUCCESS" : "FAILED";
            _logger!.Information(
                "[{Status}] Workflow: {WorkflowName} | Steps: {StepCount} | Duration: {Duration}ms",
                status, workflowName, stepCount, duration.TotalMilliseconds
            );
        }

        /// <summary>
        /// Log a vision analysis result.
        /// </summary>
        public static void LogVisionAnalysis(string targetSpec, int elementsFound, int confidenceScore, TimeSpan duration)
        {
            EnsureInitialized();
            
            _logger!.Information(
                "Vision Analysis: {TargetSpec} | Found {ElementsFound} elements | Confidence: {ConfidenceScore}% | Duration: {Duration}ms",
                targetSpec, elementsFound, confidenceScore, duration.TotalMilliseconds
            );
        }

        /// <summary>
        /// Log a safety confirmation request.
        /// </summary>
        public static void LogSafetyConfirmation(string actionDescription, RiskLevel risk, bool confirmed, bool timedOut)
        {
            EnsureInitialized();
            
            var result = timedOut ? "TIMEOUT" : (confirmed ? "CONFIRMED" : "DENIED");
            _logger!.Warning(
                "Safety Check [{Risk}]: {ActionDescription} | Result: {Result}",
                risk.ToString().ToUpper(), actionDescription, result
            );
        }

        /// <summary>
        /// Log a learning system correction.
        /// </summary>
        public static void LogCorrection(string applicationName, string elementDescription, string correctName)
        {
            EnsureInitialized();
            
            _logger!.Information(
                "Learning Correction: {ApplicationName} | '{ElementDescription}' -> '{CorrectName}'",
                applicationName, elementDescription, correctName
            );
        }

        /// <summary>
        /// Log an error with full details.
        /// </summary>
        public static void LogError(string context, Exception ex, Dictionary<string, object>? additionalInfo = null)
        {
            EnsureInitialized();
            
            var infoStr = additionalInfo != null 
                ? string.Join(", ", additionalInfo.Select(kvp => $"{kvp.Key}={kvp.Value}"))
                : "N/A";
            
            _logger!.Error(ex, "Error in {Context} | Additional Info: {Info}", context, infoStr);
        }

        /// <summary>
        /// Log a debug message.
        /// </summary>
        public static void Debug(string message, params object[] args)
        {
            EnsureInitialized();
            _logger!.Debug(message, args);
        }

        /// <summary>
        /// Log an informational message.
        /// </summary>
        public static void Information(string message, params object[] args)
        {
            EnsureInitialized();
            _logger!.Information(message, args);
        }

        /// <summary>
        /// Log a warning message.
        /// </summary>
        public static void Warning(string message, params object[] args)
        {
            EnsureInitialized();
            _logger!.Warning(message, args);
        }

        /// <summary>
        /// Close and flush the logger.
        /// </summary>
        public static void CloseAndFlush()
        {
            lock (_lock)
            {
                _logger?.Dispose();
                _logger = null;
            }
        }

        private static void EnsureInitialized()
        {
            if (_logger == null)
            {
                Initialize();
            }
        }
    }
}
