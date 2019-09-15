using System;
using Common.Logging;
using Serilog;
using Serilog.Events;

namespace Atomex.Common
{
    public class RpcLog : ILog
    {
        public bool IsTraceEnabled => Log.IsEnabled(LogEventLevel.Debug);
        public bool IsDebugEnabled => Log.IsEnabled(LogEventLevel.Debug);
        public bool IsErrorEnabled => Log.IsEnabled(LogEventLevel.Error);
        public bool IsFatalEnabled => Log.IsEnabled(LogEventLevel.Fatal);
        public bool IsInfoEnabled => Log.IsEnabled(LogEventLevel.Information);
        public bool IsWarnEnabled => Log.IsEnabled(LogEventLevel.Warning);

        public IVariablesContext GlobalVariablesContext => throw new NotImplementedException();

        public IVariablesContext ThreadVariablesContext => throw new NotImplementedException();

        public INestedVariablesContext NestedThreadVariablesContext => throw new NotImplementedException();

        public void Debug(object message)
        {
            Log.Debug(message.ToString());
        }

        public void Debug(object message, Exception exception)
        {
            Log.Debug(exception, message.ToString());
        }

        public void Debug(Action<FormatMessageHandler> formatMessageCallback)
        {
            throw new NotImplementedException();
        }

        public void Debug(Action<FormatMessageHandler> formatMessageCallback, Exception exception)
        {
            throw new NotImplementedException();
        }

        public void Debug(IFormatProvider formatProvider, Action<FormatMessageHandler> formatMessageCallback)
        {
            throw new NotImplementedException();
        }

        public void Debug(IFormatProvider formatProvider, Action<FormatMessageHandler> formatMessageCallback, Exception exception)
        {
            throw new NotImplementedException();
        }

        public void DebugFormat(string format, params object[] args)
        {
            Log.Debug(format, args);
        }

        public void DebugFormat(string format, Exception exception, params object[] args)
        {
            Log.Debug(exception, format, args);
        }

        public void DebugFormat(IFormatProvider formatProvider, string format, params object[] args)
        {
            throw new NotImplementedException();
        }

        public void DebugFormat(IFormatProvider formatProvider, string format, Exception exception, params object[] args)
        {
            throw new NotImplementedException();
        }

        public void Error(object message)
        {
            Log.Error(message.ToString());
        }

        public void Error(object message, Exception exception)
        {
            Log.Debug(exception, message.ToString());
        }

        public void Error(Action<FormatMessageHandler> formatMessageCallback)
        {
            throw new NotImplementedException();
        }

        public void Error(Action<FormatMessageHandler> formatMessageCallback, Exception exception)
        {
            throw new NotImplementedException();
        }

        public void Error(IFormatProvider formatProvider, Action<FormatMessageHandler> formatMessageCallback)
        {
            throw new NotImplementedException();
        }

        public void Error(IFormatProvider formatProvider, Action<FormatMessageHandler> formatMessageCallback, Exception exception)
        {
            throw new NotImplementedException();
        }

        public void ErrorFormat(string format, params object[] args)
        {
            Log.Error(format, args);
        }

        public void ErrorFormat(string format, Exception exception, params object[] args)
        {
            Log.Error(exception, format, args);
        }

        public void ErrorFormat(IFormatProvider formatProvider, string format, params object[] args)
        {
            throw new NotImplementedException();
        }

        public void ErrorFormat(IFormatProvider formatProvider, string format, Exception exception, params object[] args)
        {
            throw new NotImplementedException();
        }

        public void Fatal(object message)
        {
            Log.Fatal(message.ToString());
        }

        public void Fatal(object message, Exception exception)
        {
            Log.Fatal(exception, message.ToString());
        }

        public void Fatal(Action<FormatMessageHandler> formatMessageCallback)
        {
            throw new NotImplementedException();
        }

        public void Fatal(Action<FormatMessageHandler> formatMessageCallback, Exception exception)
        {
            throw new NotImplementedException();
        }

        public void Fatal(IFormatProvider formatProvider, Action<FormatMessageHandler> formatMessageCallback)
        {
            throw new NotImplementedException();
        }

        public void Fatal(IFormatProvider formatProvider, Action<FormatMessageHandler> formatMessageCallback, Exception exception)
        {
            throw new NotImplementedException();
        }

        public void FatalFormat(string format, params object[] args)
        {
            Log.Fatal(format, args);
        }

        public void FatalFormat(string format, Exception exception, params object[] args)
        {
            Log.Fatal(exception, format, args);
        }

        public void FatalFormat(IFormatProvider formatProvider, string format, params object[] args)
        {
            throw new NotImplementedException();
        }

        public void FatalFormat(IFormatProvider formatProvider, string format, Exception exception, params object[] args)
        {
            throw new NotImplementedException();
        }

        public void Info(object message)
        {
            Log.Information(message.ToString());
        }

        public void Info(object message, Exception exception)
        {
            Log.Information(exception, message.ToString());
        }

        public void Info(Action<FormatMessageHandler> formatMessageCallback)
        {
            throw new NotImplementedException();
        }

        public void Info(Action<FormatMessageHandler> formatMessageCallback, Exception exception)
        {
            throw new NotImplementedException();
        }

        public void Info(IFormatProvider formatProvider, Action<FormatMessageHandler> formatMessageCallback)
        {
            throw new NotImplementedException();
        }

        public void Info(IFormatProvider formatProvider, Action<FormatMessageHandler> formatMessageCallback, Exception exception)
        {
            throw new NotImplementedException();
        }

        public void InfoFormat(string format, params object[] args)
        {
            Log.Information(format, args);
        }

        public void InfoFormat(string format, Exception exception, params object[] args)
        {
            Log.Information(exception, format, args);
        }

        public void InfoFormat(IFormatProvider formatProvider, string format, params object[] args)
        {
            throw new NotImplementedException();
        }

        public void InfoFormat(IFormatProvider formatProvider, string format, Exception exception, params object[] args)
        {
            throw new NotImplementedException();
        }

        public void Trace(object message)
        {
            Log.Debug(message.ToString());
        }

        public void Trace(object message, Exception exception)
        {
            Log.Debug(exception, message.ToString());
        }

        public void Trace(Action<FormatMessageHandler> formatMessageCallback)
        {
            throw new NotImplementedException();
        }

        public void Trace(Action<FormatMessageHandler> formatMessageCallback, Exception exception)
        {
            throw new NotImplementedException();
        }

        public void Trace(IFormatProvider formatProvider, Action<FormatMessageHandler> formatMessageCallback)
        {
            throw new NotImplementedException();
        }

        public void Trace(IFormatProvider formatProvider, Action<FormatMessageHandler> formatMessageCallback, Exception exception)
        {
            throw new NotImplementedException();
        }

        public void TraceFormat(string format, params object[] args)
        {
            Log.Debug(format, args);
        }

        public void TraceFormat(string format, Exception exception, params object[] args)
        {
            Log.Debug(exception, format, args);
        }

        public void TraceFormat(IFormatProvider formatProvider, string format, params object[] args)
        {
            throw new NotImplementedException();
        }

        public void TraceFormat(IFormatProvider formatProvider, string format, Exception exception, params object[] args)
        {
            throw new NotImplementedException();
        }

        public void Warn(object message)
        {
            Log.Warning(message.ToString());
        }

        public void Warn(object message, Exception exception)
        {
            Log.Warning(exception, message.ToString());
        }

        public void Warn(Action<FormatMessageHandler> formatMessageCallback)
        {
            throw new NotImplementedException();
        }

        public void Warn(Action<FormatMessageHandler> formatMessageCallback, Exception exception)
        {
            throw new NotImplementedException();
        }

        public void Warn(IFormatProvider formatProvider, Action<FormatMessageHandler> formatMessageCallback)
        {
            throw new NotImplementedException();
        }

        public void Warn(IFormatProvider formatProvider, Action<FormatMessageHandler> formatMessageCallback, Exception exception)
        {
            throw new NotImplementedException();
        }

        public void WarnFormat(string format, params object[] args)
        {
            Log.Warning(format, args);
        }

        public void WarnFormat(string format, Exception exception, params object[] args)
        {
            Log.Warning(exception, format, args);
        }

        public void WarnFormat(IFormatProvider formatProvider, string format, params object[] args)
        {
            throw new NotImplementedException();
        }

        public void WarnFormat(IFormatProvider formatProvider, string format, Exception exception, params object[] args)
        {
            throw new NotImplementedException();
        }
    }
}