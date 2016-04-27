using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace OmniSharp.ProjectSystemSdk.Server
{
    public class PluginCompilationWorkspaceProxy
    {
        private readonly ICompilationWorkspace _workspace;
        private readonly ILogger _logger;
        private readonly MethodInfo[] _methods;

        public PluginCompilationWorkspaceProxy(ICompilationWorkspace workspace, ILoggerFactory loggerFactory)
        {
            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _workspace = workspace;
            _logger = loggerFactory.CreateLogger("O#WorkspaceProxy");

            _methods = _workspace.GetType().GetMethods();
        }

        public void Invoke(Envelope envelope, IPluginEventEmitter emitter)
        {
            _logger.LogInformation($"compilation: {envelope.Data.Value<string>("name")} session: {envelope.Session}.");
            _logger.LogDebug($"compilation: {envelope.Data.ToString()}");

            var methodName = envelope.Data.Value<string>("name");
            var rawArguments = ((JArray)envelope.Data["arguments"]);

            var candiates = _methods.Where(m => m.Name == methodName);
            var methodInfo = candiates.First(m => m.Name == methodName &&
                                                  m.GetParameters().Count() == rawArguments.Count);

            var argumentsInfo = methodInfo.GetParameters();
            var arguments = new object[argumentsInfo.Length];
            if (argumentsInfo.Length != 0)
            {
                for (var i = 0; i < argumentsInfo.Length; ++i)
                {
                    arguments[i] = rawArguments[i].ToObject(argumentsInfo[i].ParameterType);
                }
            }

            var result = methodInfo.Invoke(_workspace, arguments);
            
            if (result != null)
            {
                _logger.LogInformation($"compilation: {envelope.Data.Value<string>("name")} session: {envelope.Session}. returns {result?.ToString()}");
                emitter.Emit(ProjectSystemSdk.Models.EventTypes.CompilationWorkspace, new { result = result }, envelope.Session);                
            }
        }
    }
}