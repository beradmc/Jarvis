using Serilog;

namespace JarvisCSharp.Core
{
    public static class Logger
    {
        static Logger()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/jarvis-.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }

        public static void Information(string message, params object[] args)
        {
            Log.Information(message, args);
        }

        public static void Debug(string message, params object[] args)
        {
            Log.Debug(message, args);
        }

        public static void Warning(string message, params object[] args)
        {
            Log.Warning(message, args);
        }

        public static void Error(string message, params object[] args)
        {
            Log.Error(message, args);
        }

        public static void Error(Exception ex, string message, params object[] args)
        {
            Log.Error(ex, message, args);
        }

        public static void CloseAndFlush()
        {
            Log.CloseAndFlush();
        }
    }
}
