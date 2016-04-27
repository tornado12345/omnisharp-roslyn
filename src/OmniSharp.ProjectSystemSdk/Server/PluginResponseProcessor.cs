using System;
using Microsoft.Extensions.Logging;
using OmniSharp.Interfaces;
using OmniSharp.ProjectSystemSdk.Models;

namespace OmniSharp.ProjectSystemSdk.Server
{
    public class PluginResponseProcessor
    {
        private readonly IEventEmitter _consumer;
        private readonly ILogger _logger;
        private readonly PluginCompilationWorkspaceProxy _workspace;

        public PluginResponseProcessor(IEventEmitter consumer,
                                       ILoggerFactory loggerFactory,
                                       ICompilationWorkspace workspace,
                                       PluginManager pluginManager)
        {
            if (consumer == null)
            {
                throw new ArgumentNullException(nameof(consumer));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            _consumer = consumer;
            _logger = loggerFactory.CreateLogger("O#PluginResponseProcessor");
            _workspace = new PluginCompilationWorkspaceProxy(workspace, loggerFactory);

            pluginManager.OnPluginsResponse += Process;
        }

        private void Process(Envelope envelope, IPluginEventEmitter emitter)
        {
            // _logger.LogInformation($"resp: {envelope.Kind} from {envelope.Session} \n {envelope.Data.ToString(Formatting.Indented)}");
            switch (envelope.Kind)
            {
                case EventTypes.Trace:
                    _logger.LogInformation($"      trace: {envelope.Data.Value<string>("message")}");
                    break;
                case EventTypes.CompilationWorkspace:
                    _workspace.Invoke(envelope, emitter);
                    break;
                case EventTypes.WorkspaceInformation:
                    break;
                default:
                    _logger.LogInformation($"    default: {envelope.Kind}");
                    _consumer.Emit(envelope.Kind, envelope.Data.ToObject<object>());
                    break;
            }
        }
    }
}