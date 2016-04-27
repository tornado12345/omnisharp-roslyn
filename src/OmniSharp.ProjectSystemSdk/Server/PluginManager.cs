using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OmniSharp.Interfaces;

namespace OmniSharp.ProjectSystemSdk.Server
{
    [Export, Shared]
    public class PluginManager : IDisposable
    {
        private readonly string ConfigSectionName = "projectSystems";
        private readonly List<PluginContainer> _containers = new List<PluginContainer>();
        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ICompilationWorkspace _workspace;
        private readonly PluginResponseProcessor _pluginResponseProcessor;

        [ImportingConstructor]
        public PluginManager(ILoggerFactory loggerFactory,
                             ICompilationWorkspace workspace,
                             IEventEmitter emitter)
        {
            _workspace = workspace;

            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<PluginManager>();

            _pluginResponseProcessor = new PluginResponseProcessor(emitter, loggerFactory, workspace, this);
        }

        public event Action<Envelope, IPluginEventEmitter> OnPluginsResponse;

        public void Start(IConfiguration rootConfiguration, string rootPath)
        {
            foreach (var config in rootConfiguration.GetSection(ConfigSectionName)?.GetChildren() ??
                                   Enumerable.Empty<IConfigurationSection>())
            {
                var plugin = new PluginContainer(config, _loggerFactory, _workspace);
                plugin.OnPluginResponse += (envelope, emitter) => OnPluginsResponse(envelope, emitter);

                _containers.Add(plugin);
            }

            foreach (var plugin in _containers)
            {
                plugin.Start(rootPath);
            }
        }

        public Task<Dictionary<string, object>> GetInformationModels(object request)
        {
            if (_containers.Any())
            {
                return Task<Dictionary<string, object>>
                    .Factory
                    .ContinueWhenAll<KeyValuePair<string, object>>(
                        _containers.Select(p => p.GetWorkspaceInformation(request)).ToArray(),
                        t => t.ToDictionary(each => each.Result.Key, each => each.Result.Value));
            }
            else 
            {
                return Task.FromResult<Dictionary<string, object>>(new Dictionary<string, object>());
            }
        }

        public void Dispose()
        {
            _logger.LogInformation("Disposing ...");

            foreach (var container in _containers)
            {
                container.Dispose();
            }
        }
    }
}