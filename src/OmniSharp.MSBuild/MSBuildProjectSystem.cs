using System;
using System.Collections.Generic;
using System.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OmniSharp.Interfaces;
using OmniSharp.Models;
using OmniSharp.Models.v1;
using OmniSharp.MSBuild.ProjectFile;
using OmniSharp.MSBuild.Solution;
using OmniSharp.Options;
using OmniSharp.ProjectSystemSdk;
using OmniSharp.Services;

namespace OmniSharp.MSBuild
{
    [Export(typeof(IProjectSystem))]
    public class MSBuildProjectSystem : IProjectSystem
    {
        private readonly ICompilationWorkspace _workspace;
        private readonly IMetadataFileReferenceCache _metadataReferenceCache;
        private readonly IOmnisharpEnvironment _env;
        private readonly ILogger _logger;
        private readonly IEventEmitter _emitter;
        private static readonly Guid[] _supportsProjectTypes = new[] {
            new Guid("fae04ec0-301f-11d3-bf4b-00c04f79efbc") // CSharp
        };

        private readonly MSBuildContext _context;
        private readonly IFileSystemWatcher _watcher;

        private MSBuildOptions _options;

        [ImportingConstructor]
        public MSBuildProjectSystem(ICompilationWorkspace workspace,
                                    IOmnisharpEnvironment env,
                                    ILoggerFactory loggerFactory,
                                    IEventEmitter emitter,
                                    IMetadataFileReferenceCache metadataReferenceCache,
                                    IFileSystemWatcher watcher,
                                    MSBuildContext context)
        {
            _workspace = workspace;
            _metadataReferenceCache = metadataReferenceCache;
            _watcher = watcher;
            _env = env;
            _logger = loggerFactory.CreateLogger<MSBuildProjectSystem>();
            _emitter = emitter;
            _context = context;
        }

        public string Key => "MsBuild";
        public string Language => GeneralLanguageNames.CSharp;
        public IEnumerable<string> Extensions { get; } = new[] { ".cs" };

        public void Initalize(IConfiguration configuration)
        {
            _options = new MSBuildOptions();
            ConfigurationBinder.Bind(configuration, _options);

            var solutionFilePath = _env.SolutionFilePath;

            if (string.IsNullOrEmpty(solutionFilePath))
            {
                var solutions = Directory.GetFiles(_env.Path, "*.sln");
                var result = SolutionPicker.ChooseSolution(_env.Path, solutions);

                if (result.Message != null)
                {
                    _logger.LogInformation(result.Message);
                }

                if (result.Solution == null)
                {
                    return;
                }

                solutionFilePath = result.Solution;
            }

            SolutionFile solutionFile = null;

            _context.SolutionPath = solutionFilePath;

            using (var stream = File.OpenRead(solutionFilePath))
            {
                using (var reader = new StreamReader(stream))
                {
                    solutionFile = SolutionFile.Parse(reader);
                }
            }
            _logger.LogInformation($"Detecting projects in '{solutionFilePath}'.");

            foreach (var block in solutionFile.ProjectBlocks)
            {
                if (!_supportsProjectTypes.Contains(block.ProjectTypeGuid))
                {
                    if (UnityTypeGuid(block.ProjectName) != block.ProjectTypeGuid)
                    {
                        _logger.LogWarning("Skipped unsupported project type '{0}'", block.ProjectPath);
                        continue;
                    }
                }

                if (_context.ProjectGuidToWorkspaceMapping.ContainsKey(block.ProjectGuid))
                {
                    continue;
                }

                var projectFilePath = Path.GetFullPath(Path.GetFullPath(Path.Combine(_env.Path, block.ProjectPath.Replace('\\', Path.DirectorySeparatorChar))));

                _logger.LogInformation($"Loading project from '{projectFilePath}'.");

                var projectFileInfo = CreateProject(projectFilePath);
                if (projectFileInfo == null)
                {
                    continue;
                }

                var projectId = _workspace.CreateNewProjectID();
                _workspace.AddProject(projectId,
                                      projectFileInfo.Name,
                                      projectFileInfo.AssemblyName,
                                      GeneralLanguageNames.CSharp,
                                      projectFileInfo.ProjectFilePath);

                var compilerOption = new GeneralCompilationOptions
                {
                    OutputKind = projectFileInfo.OutputKind,
                    AllowUnsafe = projectFileInfo.AllowUnsafe,
                    UseDefaultDesktopAssemblyIdentityComparer = true
                };
                
                if (projectFileInfo.SignAssembly && !string.IsNullOrEmpty(projectFileInfo.AssemblyOriginatorKeyFile))
                {
                    compilerOption.KeyFile = Path.Combine(projectFileInfo.ProjectDirectory, projectFileInfo.AssemblyOriginatorKeyFile);
                }
                
                _workspace.SetCSharpCompilationOptions(projectId, compilerOption);

                projectFileInfo.WorkspaceId = projectId;

                _context.Projects[projectFileInfo.ProjectFilePath] = projectFileInfo;
                _context.ProjectGuidToWorkspaceMapping[block.ProjectGuid] = projectId;

                _watcher.Watch(projectFilePath, OnProjectChanged);
            }

            foreach (var projectFileInfo in _context.Projects.Values)
            {
                UpdateProject(projectFileInfo);
            }
        }

        public static Guid UnityTypeGuid(string projectName)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(projectName);
                var hash = md5.ComputeHash(bytes);

                var bigEndianHash = new[] {
                    hash[3], hash[2], hash[1], hash[0],
                    hash[5], hash[4],
                    hash[7], hash[6],
                    hash[8], hash[9], hash[10], hash[11], hash[12], hash[13], hash[14], hash[15]
                };

                return new System.Guid(bigEndianHash);
            }
        }

        private ProjectFileInfo CreateProject(string projectFilePath)
        {
            ProjectFileInfo projectFileInfo = null;
            var diagnostics = new List<MSBuildDiagnosticsMessage>();

            try
            {
                projectFileInfo = ProjectFileInfo.Create(_options, _logger, _env.Path, projectFilePath, diagnostics);

                if (projectFileInfo == null)
                {
                    _logger.LogWarning($"Failed to process project file '{projectFilePath}'.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to process project file '{projectFilePath}'.", ex);
                _emitter.Emit(EventTypes.Error, new ErrorMessage()
                {
                    FileName = projectFilePath,
                    Text = ex.ToString()
                });
            }

            _emitter.Emit(EventTypes.MsBuildProjectDiagnostics, new MSBuildProjectDiagnostics()
            {
                FileName = projectFilePath,
                Warnings = diagnostics.Where(d => d.LogLevel == "Warning"),
                Errors = diagnostics.Where(d => d.LogLevel == "Error"),
            });

            return projectFileInfo;
        }

        private void OnProjectChanged(string projectFilePath)
        {
            var newProjectInfo = CreateProject(projectFilePath);

            // Should we remove the entry if the project is malformed?
            if (newProjectInfo != null)
            {
                lock (_context)
                {
                    ProjectFileInfo oldProjectFileInfo;
                    if (_context.Projects.TryGetValue(projectFilePath, out oldProjectFileInfo))
                    {
                        _context.Projects[projectFilePath] = newProjectInfo;
                        newProjectInfo.WorkspaceId = oldProjectFileInfo.WorkspaceId;
                        UpdateProject(newProjectInfo);
                    }
                }
            }
        }

        private void UpdateDocuments(ProjectFileInfo projectFileInfo)
        {
            var unusedDocuments = _workspace.GetDocuments(projectFileInfo.WorkspaceId);

            foreach (var file in projectFileInfo.SourceFiles)
            {
                if (unusedDocuments.Remove(file))
                {
                    continue;
                }

                _workspace.AddDocument(projectFileInfo.WorkspaceId, file);
            }

            _workspace.SetParsingOptions(projectFileInfo.WorkspaceId, new GeneralCompilationOptions
            {
                LanguageVersion = projectFileInfo.LanguageVersion,
                Defines = projectFileInfo.DefineConstants ?? Enumerable.Empty<string>()
            });

            foreach (var unused in unusedDocuments)
            {
                _workspace.RemoveDocument(projectFileInfo.WorkspaceId, unused.Value);
            }
        }

        private void UpdateProjectReferences(ProjectFileInfo projectFileInfo)
        {
            var unusedProjectReferences = new HashSet<Guid>(_workspace.GetProjectReferences(projectFileInfo.WorkspaceId));

            foreach (var projectReferencePath in projectFileInfo.ProjectReferences)
            {
                ProjectFileInfo projectReferenceInfo;
                if (_context.Projects.TryGetValue(projectReferencePath, out projectReferenceInfo))
                {
                    if (unusedProjectReferences.Remove(projectReferenceInfo.WorkspaceId))
                    {
                        // This reference already exists
                        continue;
                    }

                    _workspace.AddProjectReference(projectFileInfo.WorkspaceId, projectReferenceInfo.WorkspaceId);
                }
                else
                {
                    _logger.LogWarning($"Unable to resolve project reference '{projectReferencePath}' for '{projectFileInfo}'.");
                }
            }

            foreach (var unused in unusedProjectReferences)
            {
                _workspace.RemoveProjectReference(projectFileInfo.WorkspaceId, unused);
            }
        }

        private void UpdateAnalyzers(ProjectFileInfo projectFileInfo)
        {
            // var unusedAnalyzers = new Dictionary<string, AnalyzerReference>(
            //     project.AnalyzerReferences.ToDictionary(a => a.FullPath));
            var unusedAnalyzers = new HashSet<string>(_workspace.GetAnalyzersInPaths(projectFileInfo.WorkspaceId));

            foreach (var analyzerPath in projectFileInfo.Analyzers)
            {
                if (!File.Exists(analyzerPath))
                {
                    _logger.LogWarning($"Unable to resolve assembly '{analyzerPath}'");
                }
                else
                {
                    if (unusedAnalyzers.Remove(analyzerPath))
                    {
                        continue;
                    }

                    _workspace.AddAnalyzerReference(projectFileInfo.WorkspaceId, analyzerPath);
                }
            }

            foreach (var analyzerPath in unusedAnalyzers)
            {
                _workspace.RemoveAnalyzerReference(projectFileInfo.ProjectId, analyzerPath);
            }
        }

        private void UpdateMetadataReferences(ProjectFileInfo projectFileInfo)
        {
            var unusedReferences = new HashSet<string>(projectFileInfo.LoadedReferences);

            foreach (var referencePath in projectFileInfo.References)
            {
                if (!File.Exists(referencePath))
                {
                    _logger.LogWarning($"Unable to resolve assembly '{referencePath}'");
                }
                else
                {
                    if (unusedReferences.Remove(referencePath))
                    {
                        continue;
                    }
                    
                    _workspace.AddFileReference(projectFileInfo.WorkspaceId, referencePath);
                    _logger.LogTrace($"Adding reference '{referencePath}' to '{projectFileInfo.ProjectFilePath}'.");
                    projectFileInfo.LoadedReferences.Add(referencePath);
                }
            }

            foreach (var reference in unusedReferences)
            {
                _workspace.RemoveFileReference(projectFileInfo.WorkspaceId, reference);
                projectFileInfo.LoadedReferences.Remove(reference);
            }
        }

        private void UpdateProject(ProjectFileInfo project)
        {
            UpdateDocuments(project);
            UpdateProjectReferences(project);
            UpdateAnalyzers(project);
            UpdateMetadataReferences(project);
        }

        public ProjectFileInfo GetProject(string path)
        {
            ProjectFileInfo projectFileInfo;
            if (!_context.Projects.TryGetValue(path, out projectFileInfo))
            {
                return null;
            }

            return projectFileInfo;
        }

        Task<object> IProjectSystem.GetProjectModel(string path)
        {
            var projectPath = _workspace.GetProjectPathFromDocumentPath(path);
            if (projectPath == null)
            {
                return Task.FromResult<object>(null);
            }
            
            var project = GetProject(projectPath);
            if (project == null)
            {
                return Task.FromResult<object>(null);
            }

            return Task.FromResult<object>(new MSBuildProject(project));
        }

        Task<object> IProjectSystem.GetInformationModel(WorkspaceInformationRequest request)
        {
            return Task.FromResult<object>(new MsBuildWorkspaceInformation(_context, request?.ExcludeSourceFiles ?? false));
        }
    }
}
