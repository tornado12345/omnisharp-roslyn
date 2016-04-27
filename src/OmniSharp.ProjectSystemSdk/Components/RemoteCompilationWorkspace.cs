using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using OmniSharp.ProjectSystemSdk.Models;

namespace OmniSharp.ProjectSystemSdk.Components
{
    public class RemoteCompilationWorkspace : ICompilationWorkspace
    {
        private readonly IPluginEventEmitter _emitter;
        private readonly ProcessQueue _listener;
        private ConcurrentDictionary<Guid, JToken> _responses;
        private AutoResetEvent _bell;

        public RemoteCompilationWorkspace(IPluginEventEmitter emitter, ProcessQueue queue)
        {
            _emitter = emitter;
            _listener = queue;
            _responses = new ConcurrentDictionary<Guid, JToken>();
            _bell = new AutoResetEvent(false);

            _listener.OnCompilationEvent += OnCompilationEvent;
        }

        public void AddAnalyzerReference(Guid projectId, string path)
        {
            InvokeRemote(nameof(AddAnalyzerReference), projectId, path);
        }

        public Guid AddDocument(Guid projectId, string filePath)
        {
            return InvokeRemoteWithResult<Guid>(nameof(AddDocument), projectId, filePath);
        }

        public void AddFileReference(Guid projectId, string filePath)
        {
            InvokeRemote(nameof(AddFileReference), projectId, filePath);
        }

        public void AddProject(Guid id, string name, string assemblyName, string language, string filePath)
        {
            InvokeRemote(nameof(AddProject), id, name, assemblyName, language, filePath);
        }

        public void AddProjectReference(Guid projectId, Guid referencedProjectId)
        {
            InvokeRemote(nameof(AddProjectReference), projectId, referencedProjectId);
        }

        public Guid CreateNewProjectID()
        {
            return InvokeRemoteWithResult<Guid>(nameof(CreateNewProjectID));
        }

        public IEnumerable<string> GetAnalyzersInPaths(Guid projectId)
        {
            return InvokeRemoteWithResult<IEnumerable<string>>(nameof(GetAnalyzersInPaths), projectId);
        }

        public IDictionary<string, Guid> GetDocuments(Guid projectId)
        {
            return InvokeRemoteWithResult<Dictionary<string, Guid>>(nameof(GetDocuments), projectId);
        }

        public string GetProjectPathFromDocumentPath(string path)
        {
            return InvokeRemoteWithResult<string>(nameof(GetProjectPathFromDocumentPath), path);
        }

        public IEnumerable<Guid> GetProjectReferences(Guid projectId)
        {
            return InvokeRemoteWithResult<IEnumerable<Guid>>(nameof(GetProjectReferences), projectId);
        }

        public void RemoveAnalyzerReference(Guid projectId, string path)
        {
            InvokeRemote(nameof(RemoveAnalyzerReference), projectId, path);
        }

        public void RemoveDocument(Guid projectId, Guid id)
        {
            InvokeRemote(nameof(RemoveDocument), projectId, id);
        }

        public void RemoveFileReference(Guid projectId, string filePath)
        {
            InvokeRemote(nameof(RemoveFileReference), projectId, filePath);
        }

        public void RemoveProject(Guid id)
        {
            InvokeRemote(nameof(RemoveProject), id);
        }

        public void RemoveProjectReference(Guid projectId, Guid referencedProjectId)
        {
            InvokeRemote(nameof(RemoveProjectReference), projectId, referencedProjectId);
        }

        public void SetCSharpCompilationOptions(Guid projectId, GeneralCompilationOptions options)
        {
            InvokeRemote(nameof(SetCSharpCompilationOptions), projectId, options);
        }

        public void SetCSharpCompilationOptions(Guid projectId, string projectPath, GeneralCompilationOptions options)
        {
            InvokeRemote(nameof(SetCSharpCompilationOptions), projectId, projectPath, options);
        }

        public void SetParsingOptions(Guid projectId, GeneralCompilationOptions option)
        {
            InvokeRemote(nameof(SetParsingOptions), projectId, option);
        }

        private void OnCompilationEvent(Envelope envelope, IPluginEventEmitter emitter)
        {
            // _emitter.Emit(EventTypes.Trace, new { message = $"ring a bell {envelope.Session}" });
            var k = envelope.Session;
            var v = envelope.Data["result"];

            _responses.AddOrUpdate(k, v, (key, old) => v);

            _bell.Set();
        }

        private Guid InvokeRemote(string name, params object[] arguments)
        {
            return _emitter.Emit(EventTypes.CompilationWorkspace, new { name = name, arguments = arguments });
        }

        private T InvokeRemoteWithResult<T>(string name, params object[] arguments)
        {
            var session = InvokeRemote(name, arguments);

            // potential issue: a response of another session comes in first
            JToken rval;
            if (_bell.WaitOne(5000) && _responses.TryRemove(session, out rval))
            {
                return rval.ToObject<T>();
            }

            throw new InvalidOperationException($"Remote execution of {name} failed because result is not sent back. {Thread.CurrentThread.ManagedThreadId}");
        }
    }
}
