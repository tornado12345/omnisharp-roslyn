using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.ProjectModel;
using NuGet.Frameworks;
using OmniSharp.DotNet.Models;
using OmniSharp.ProjectSystemSdk;
using OmniSharp.ProjectSystemSdk.Models;

namespace OmniSharp.DotNet.Cache
{
    public class ProjectStatesCache
    {
        private readonly Dictionary<string, ProjectEntry> _projects
                   = new Dictionary<string, ProjectEntry>(StringComparer.OrdinalIgnoreCase);

        // private readonly ILogger _logger;
        private readonly IPluginEventEmitter _emitter;
        private readonly ICompilationWorkspace _workspace;

        public ProjectStatesCache(IPluginEventEmitter emitter, ICompilationWorkspace workspace)
        {
            // _logger = loggerFactory?.CreateLogger<ProjectStatesCache>() ?? new DummyLogger<ProjectStatesCache>();
            _emitter = emitter;
            _workspace = workspace;
        }

        public IEnumerable<ProjectEntry> GetStates => _projects.Values;

        public IReadOnlyCollection<ProjectState> GetValues()
        {
            return _projects.Select(p => p.Value)
                            .SelectMany(entry => entry.ProjectStates)
                            .ToList();
        }

        public void Update(string projectDirectory,
                           IEnumerable<ProjectContext> contexts,
                           Action<Guid, ProjectContext> addAction,
                           Action<Guid> removeAction)
        {
            // _logger.LogTrace($"Updating project ${projectDirectory}");

            bool added;
            var entry = GetOrAddEntry(projectDirectory, out added);

            // remove frameworks which don't exist after update
            var remove = entry.Frameworks.Except(contexts.Select(c => c.TargetFramework));
            foreach (var each in remove)
            {
                var toRemove = entry.Get(each);
                removeAction(toRemove.Id);
                entry.Remove(each);
            }

            foreach (var context in contexts)
            {
                // _logger.LogTrace($"  For context {context.TargetFramework}");
                ProjectState currentState = entry.Get(context.TargetFramework);
                if (currentState != null)
                {
                    // _logger.LogTrace($"  Update exsiting {nameof(ProjectState)}.");
                    currentState.ProjectContext = context;
                }
                else
                {
                    // _logger.LogTrace($"  Add new {nameof(ProjectState)}.");
                    var projectId = _workspace.CreateNewProjectID();
                    entry.Set(new ProjectState(projectId, context));
                    addAction(projectId, context);
                }
            }

            var projectInformation = new DotNetProjectInformation(entry);
            if (added)
            {
                _emitter.Emit(EventTypes.ProjectChanged, projectInformation);
            }
            else
            {
                _emitter.Emit(EventTypes.ProjectAdded, projectInformation);
            }
        }

        /// <summary>
        /// Remove projects not in the give project set and execute the <paramref name="removeAction"/> on the removed project id.
        /// </summary>
        /// <param name="perservedProjects">Projects to perserve</param>
        /// <param name="removeAction"></param>
        public void RemoveExcept(IEnumerable<string> perservedProjects, Action<ProjectEntry> removeAction)
        {
            var removeList = new HashSet<string>(_projects.Keys, StringComparer.OrdinalIgnoreCase);
            removeList.ExceptWith(perservedProjects);

            foreach (var key in removeList)
            {
                var entry = _projects[key];
                var projectInformation = new DotNetProjectInformation(entry);

                _emitter.Emit(EventTypes.ProjectRemoved, projectInformation);
                removeAction(entry);

                _projects.Remove(key);
            }
        }

        public IEnumerable<ProjectState> Find(string projectDirectory)
        {
            ProjectEntry entry;
            if (_projects.TryGetValue(projectDirectory, out entry))
            {
                return entry.ProjectStates;
            }
            else
            {
                return Enumerable.Empty<ProjectState>();
            }
        }

        public ProjectState Find(string projectDirectory, NuGetFramework framework)
        {
            ProjectEntry entry;
            if (_projects.TryGetValue(projectDirectory, out entry))
            {
                return entry.Get(framework);
            }
            else
            {
                return null;
            }
        }

        internal ProjectEntry GetOrAddEntry(string projectDirectory)
        {
            ProjectEntry result;
            if (_projects.TryGetValue(projectDirectory, out result))
            {
                return result;
            }
            else
            {
                result = new ProjectEntry(projectDirectory);
                _projects[projectDirectory] = result;

                return result;
            }
        }

        private ProjectEntry GetOrAddEntry(string projectDirectory, out bool added)
        {
            added = false;
            ProjectEntry result;
            if (_projects.TryGetValue(projectDirectory, out result))
            {

                return result;
            }
            else
            {
                result = new ProjectEntry(projectDirectory);
                _projects[projectDirectory] = result;
                added = true;

                return result;
            }
        }
    }
}
