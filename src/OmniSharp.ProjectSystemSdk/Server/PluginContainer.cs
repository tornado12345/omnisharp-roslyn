using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OmniSharp.ProjectSystemSdk.Models;

namespace OmniSharp.ProjectSystemSdk.Server
{
    public class PluginContainer : IDisposable, IPluginEventEmitter
    {
        private readonly ILogger _logger;
        private readonly string _description;
        private readonly string _executable;
        private readonly IDictionary<string, string> _settings;
        private readonly ProcessStartInfo _startInfo;
        private readonly ConcurrentDictionary<Guid, Action<Envelope>> _waitFor;
        private Process _process;

        public PluginContainer(IConfigurationSection config,
                               ILoggerFactory loggerFactory,
                               ICompilationWorkspace workspace)
        {
            Name = config.Key;
            _description = config["description"];
            _executable = config["executable"];
            _settings = config.GetSection("settings")
                              .GetChildren()
                              .ToDictionary(section => section.Key, section => section.Value);

            _startInfo = new ProcessStartInfo()
            {
                FileName = _executable,
                Arguments = $"--host-pid {Process.GetCurrentProcess().Id}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _logger = loggerFactory.CreateLogger($"O#PluginContainer.{Name}");
            _logger.LogInformation($"Create plugin {config.Key} - {config["description"]}");

            _waitFor = new ConcurrentDictionary<Guid, Action<Envelope>>();
        }

        public string Name { get; }

        public void Start(string rootPath)
        {
            _process = Process.Start(_startInfo);
            _process.OutputDataReceived += OnOutputData;
            _process.ErrorDataReceived += OnErrorData;
            _process.Exited += OnProcessExit;

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Emit(EventTypes.ProjectSystemInitialize, new
            {
                root = rootPath,
                settings = _settings
            });
        }

        public Guid Emit(string kind, object args)
        {
            return Emit(kind, args, Guid.NewGuid());
        }

        public Guid Emit(string kind, object args, Guid sessionId)
        {
            var content = Envelope.Serialize(sessionId, kind, args);
            _process.StandardInput.WriteLine(content);

            return sessionId;
        }

        public Task<KeyValuePair<string, object>> GetWorkspaceInformation(object request)
        {
            var session = Emit(EventTypes.WorkspaceInformation, request);
            var tcs = new TaskCompletionSource<KeyValuePair<string, object>>();

            Action<Envelope> action = envelope =>
            {
                Action<Envelope> placeholder;
                _waitFor.TryRemove(session, out placeholder);
                
                tcs.SetResult(new KeyValuePair<string, object>(Name, envelope.Data));
            };

            _waitFor.TryAdd(session, action);

            return tcs.Task;
        }

        public event Action<Envelope, IPluginEventEmitter> OnPluginResponse;

        public void Dispose()
        {
            // Thought: further testing the process leak
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
            }
        }

        private void OnErrorData(object sender, DataReceivedEventArgs e)
        {
            _logger.LogInformation($" error: {e.Data}");
        }

        private void OnOutputData(object sender, DataReceivedEventArgs e)
        {
            var envelop = Envelope.Deserialize(e.Data);
            if (envelop == null)
            {
                _logger.LogInformation($"plugin: unenveloped data: {e.Data}");
            }
            else
            {
                Action<Envelope> action;
                if (_waitFor.TryGetValue(envelop.Session, out action))
                {
                    action(envelop);
                }

                OnPluginResponse(envelop, this);
            }
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            _logger.LogInformation($"plugin exiting");
        }
    }
}