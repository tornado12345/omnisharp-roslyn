using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.DotNet.ProjectModel;
using Newtonsoft.Json.Linq;
using OmniSharp.DotNet.Cache;
using OmniSharp.DotNet.Extensions;
using OmniSharp.DotNet.Models;
using OmniSharp.DotNet.Projects;
using OmniSharp.DotNet.Tools;
using OmniSharp.Interfaces;
using OmniSharp.ProjectSystemSdk;
using OmniSharp.ProjectSystemSdk.Components;
using OmniSharp.ProjectSystemSdk.Models;

namespace OmniSharp.DotNet
{
    public class DotNetProjectSystem
    {
        private readonly string _compilationConfiguration = "Debug";
        private readonly PackagesRestoreTool _packageRestore;
        private readonly ICompilationWorkspace _workspace;
        private readonly ProjectStatesCache _projectStates;
        private readonly IPluginEventEmitter _emitter;
        private readonly IFileSystemWatcher _watcher;
        private WorkspaceContext _workspaceContext;
        private bool _enableRestorePackages = false;

        private readonly string _root;

        public DotNetProjectSystem(string root,
                                   ICompilationWorkspace workspace,
                                   IPluginEventEmitter emitter,
                                   ProcessQueue listener)
        {
            _root = root;

            _workspace = workspace;
            _emitter = emitter;

            _packageRestore = new PackagesRestoreTool(_emitter);
            _projectStates = new ProjectStatesCache(_emitter, _workspace);

            _watcher = new FileSystemWatcherWrapper(_root);

            listener.OnWorkspaceInformation += OnWorkspaceInformation;
        }

        public IEnumerable<string> Extensions { get; } = new string[] { ".cs" };

        public string Key => "DotNet";

        public string Language => GeneralLanguageNames.CSharp;

        public void OnWorkspaceInformation(Envelope envelop, IPluginEventEmitter emitter)
        {
            var excludeSourceFiles = envelop.Data.Value<bool>("ExcludeSourceFiles");

            var workspaceInfo = new DotNetWorkspaceInformation(
                entries: _projectStates.GetStates,
                includeSourceFiles: !excludeSourceFiles);

            emitter.Emit(EventTypes.WorkspaceInformation, workspaceInfo, envelop.Session);
        }

        // public Task<object> GetProjectModel(string path)
        // {
        //     // _logger.LogTrace($"GetProjectModel {path}");
        //     var projectPath = _workspace.GetProjectPathFromDocumentPath(path);
        //     if (projectPath == null)
        //     {
        //         return Task.FromResult<object>(null);
        //     }

        //     // _logger.LogTrace($"GetProjectModel {path}=>{projectPath}");
        //     var projectEntry = _projectStates.GetOrAddEntry(projectPath);
        //     var projectInformation = new DotNetProjectInformation(projectEntry);
        //     return Task.FromResult<object>(projectInformation);
        // }

        public void Initalize(JObject configuration)
        {
            _enableRestorePackages = configuration?.Value<bool>("enablePackageRestore") ?? false;
            _emitter.Emit(EventTypes.Trace, new { message = $"plugin is initializing ... enable restore packages {_enableRestorePackages}" });

            _workspaceContext = WorkspaceContext.Create();
            var projects = ProjectSearcher.Search(_root);
            foreach (var path in projects)
            {
                _workspaceContext.AddProject(path);
            }

            Update(allowRestore: true);
        }

        public void Update(bool allowRestore)
        {
            // _logger.LogTrace("Update workspace context");
            _workspaceContext.Refresh();

            var projectPaths = _workspaceContext.GetAllProjects();

            _projectStates.RemoveExcept(projectPaths, entry =>
            {
                foreach (var state in entry.ProjectStates)
                {
                    _workspace.RemoveProject(state.Id);
                    // _logger.LogTrace($"Removing project {state.Id}.");
                }
            });

            foreach (var projectPath in projectPaths)
            {
                UpdateProject(projectPath);
            }

            // _logger.LogTrace("Resolving projects references");
            foreach (var state in _projectStates.GetValues())
            {

                // _logger.LogTrace($"  Processing {state}");

                var lens = new ProjectContextLens(state.ProjectContext, _compilationConfiguration);
                UpdateFileReferences(state, lens.FileReferences);
                UpdateProjectReferences(state, lens.ProjectReferences);
                UpdateUnresolvedDependencies(state, allowRestore);
                UpdateCompilationOption(state);
                UpdateSourceFiles(state, lens.SourceFiles);
            }
        }

        private void UpdateProject(string projectDirectory)
        {
            // _logger.LogTrace($"Update project {projectDirectory}");
            var contexts = _workspaceContext.GetProjectContexts(projectDirectory);

            if (!contexts.Any())
            {
                // _logger.LogWarning($"Cannot create any {nameof(ProjectContext)} from project {projectDirectory}");
                return;
            }

            _projectStates.Update(projectDirectory, contexts, AddProject, RemoveProject);

            var projectFilePath = contexts.First().ProjectFile.ProjectFilePath;
            _watcher.Watch(projectFilePath, file =>
            {
                // _logger.LogTrace($"Watcher: {file} updated.");
                Update(true);
            });

            _watcher.Watch(Path.ChangeExtension(projectFilePath, "lock.json"), file =>
            {
                // _logger.LogTrace($"Watcher: {file} updated.");
                Update(false);
            });
        }

        private void AddProject(Guid id, ProjectContext context)
        {
            _workspace.AddProject(
                id: id,
                name: $"{context.ProjectFile.Name}+{context.TargetFramework.GetShortFolderName()}",
                assemblyName: context.ProjectFile.Name,
                language: Language,
                filePath: context.ProjectFile.ProjectFilePath);

            // _logger.LogTrace($"Add project {context.ProjectFile.ProjectFilePath} => {id}");
        }

        private void RemoveProject(Guid projectId)
        {
            _workspace.RemoveProject(projectId);
        }

        private void UpdateFileReferences(ProjectState state, IEnumerable<string> fileReferences)
        {
            var metadataReferences = new List<string>();
            var fileReferencesToRemove = state.FileMetadataReferences.ToHashSet();

            foreach (var fileReference in fileReferences)
            {
                if (!File.Exists(fileReference))
                {
                    continue;
                }

                if (fileReferencesToRemove.Remove(fileReference))
                {
                    continue;
                }

                metadataReferences.Add(fileReference);
                state.FileMetadataReferences.Add(fileReference);
                // _logger.LogTrace($"Add file reference {fileReference} | project: {state.Id}");
            }

            foreach (var reference in metadataReferences)
            {
                _workspace.AddFileReference(state.Id, reference);
            }

            foreach (var reference in fileReferencesToRemove)
            {
                state.FileMetadataReferences.Remove(reference);
                _workspace.RemoveFileReference(state.Id, reference);
                // _logger.LogTrace($"Remove file reference {reference} | project: {state.Id}");
            }

            if (metadataReferences.Count != 0 || fileReferencesToRemove.Count != 0)
            {
                // _logger.LogInformation($"Project {state.Id}: Added {metadataReferences.Count} and removed {fileReferencesToRemove.Count} file references.");
            }
        }

        private void UpdateProjectReferences(ProjectState state, IEnumerable<ProjectDescription> projectReferencesLatest)
        {
            var projectReferences = new List<Guid>();
            var projectReferencesToRemove = state.ProjectReferences.ToHashSet();

            foreach (var description in projectReferencesLatest)
            {
                var key = Tuple.Create(Path.GetDirectoryName(description.Path), description.Framework);
                if (projectReferencesToRemove.Remove(key))
                {
                    continue;
                }

                var referencedProjectState = _projectStates.Find(key.Item1, description.Framework);
                projectReferences.Add(referencedProjectState.Id);
                state.ProjectReferences.Add(key);

                // _logger.LogTrace($"Add project reference {description.Path}");
            }

            foreach (var reference in projectReferences)
            {
                _workspace.AddProjectReference(state.Id, reference);
            }

            foreach (var reference in projectReferencesToRemove)
            {
                var toRemove = _projectStates.Find(reference.Item1, reference.Item2);
                state.ProjectReferences.Remove(reference);
                _workspace.RemoveProjectReference(state.Id, toRemove.Id);

                // _logger.LogTrace($"Remove project reference {reference}");
            }

            if (projectReferences.Count != 0 || projectReferencesToRemove.Count != 0)
            {
                // _logger.LogInformation($"Project {state.Id}: Added {projectReferences.Count} and removed {projectReferencesToRemove.Count} project references");
            }
        }

        private void UpdateUnresolvedDependencies(ProjectState state, bool allowRestore)
        {
            var libraryManager = state.ProjectContext.LibraryManager;
            var allDiagnostics = libraryManager.GetAllDiagnostics();
            var unresolved = libraryManager.GetLibraries().Where(dep => !dep.Resolved);
            var needRestore = allDiagnostics.Any(diag => diag.ErrorCode == ErrorCodes.NU1006) || unresolved.Any();

            if (needRestore)
            {
                if (allowRestore && _enableRestorePackages)
                {
                    _packageRestore.Restore(state.ProjectContext.ProjectDirectory, onFailure: () =>
                    {
                        _emitter.Emit(EventTypes.UnresolvedDependencies, new UnresolvedDependenciesMessage()
                        {
                            FileName = state.ProjectContext.ProjectFile.ProjectFilePath,
                            UnresolvedDependencies = unresolved.Select(d => new PackageDependency { Name = d.Identity.Name, Version = d.Identity.Version?.ToString() })
                        });
                    });
                }
                else
                {
                    _emitter.Emit(EventTypes.UnresolvedDependencies, new UnresolvedDependenciesMessage()
                    {
                        FileName = state.ProjectContext.ProjectFile.ProjectFilePath,
                        UnresolvedDependencies = unresolved.Select(d => new PackageDependency { Name = d.Identity.Name, Version = d.Identity.Version?.ToString() })
                    });
                }
            }
        }

        private void UpdateCompilationOption(ProjectState state)
        {
            var context = state.ProjectContext;
            var project = context.ProjectFile;
            var commonOption = project.GetCompilerOptions(context.TargetFramework, _compilationConfiguration);
            var option = new GeneralCompilationOptions
            {
                OutputKind = commonOption.EmitEntryPoint.GetValueOrDefault() ? GeneralOutputKind.ConsoleApplication :
                                                                               GeneralOutputKind.DynamicallyLinkedLibrary,
                WarningsAsErrors = commonOption.WarningsAsErrors.GetValueOrDefault(),
                Optimize = commonOption.Optimize.GetValueOrDefault(),
                AllowUnsafe = commonOption.AllowUnsafe.GetValueOrDefault(),
                ConcurrentBuild = false,
                Platform = commonOption.Platform,
                KeyFile = commonOption.KeyFile,
                DiagnosticsOptions = new Dictionary<string, ReportDiagnosticOptions>{
                    { "CS1701", ReportDiagnosticOptions.Suppress },
                    { "CS1702", ReportDiagnosticOptions.Suppress },
                    { "CS1705", ReportDiagnosticOptions.Suppress },
                },
                LanguageVersion = commonOption.LanguageVersion,
                Defines = commonOption.Defines.ToArray()
            };

            _workspace.SetCSharpCompilationOptions(state.Id, project.ProjectDirectory, option);
            _workspace.SetParsingOptions(state.Id, option);
        }

        private void UpdateSourceFiles(ProjectState state, IEnumerable<string> sourceFiles)
        {
            sourceFiles = sourceFiles.Where(filename => Path.GetExtension(filename) == ".cs");

            var existingFiles = new HashSet<string>(state.DocumentReferences.Keys);

            var added = 0;
            var removed = 0;

            foreach (var file in sourceFiles)
            {
                if (existingFiles.Remove(file))
                {
                    continue;
                }

                var docId = _workspace.AddDocument(state.Id, file);
                state.DocumentReferences[file] = docId;

                // _logger.LogTrace($"    Added document {file}.");
                added++;
            }

            foreach (var file in existingFiles)
            {
                _workspace.RemoveDocument(state.Id, state.DocumentReferences[file]);
                state.DocumentReferences.Remove(file);
                // _logger.LogTrace($"    Removed document {file}.");
                removed++;
            }

            if (added != 0 || removed != 0)
            {
                // _logger.LogInformation($"Project {state.Id}: Added {added} and removed {removed} documents.");
            }
        }
    }
}
