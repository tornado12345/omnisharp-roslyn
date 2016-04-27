using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using OmniSharp.ProjectSystemSdk;
using OmniSharp.Roslyn.Analyzer;
using OmniSharp.Roslyn.Tools;
using OmniSharp.Services;

namespace OmniSharp.Roslyn
{
    [Export(typeof(ICompilationWorkspace)), Shared]
    public class RoslynWorkspace : ICompilationWorkspace
    {
        private readonly OmnisharpWorkspace _inner;
        private readonly IMetadataFileReferenceCache _metadataFileReferenceCache;
        private readonly ILogger _logger;

        [ImportingConstructor]
        public RoslynWorkspace(OmnisharpWorkspace inner,
                               IMetadataFileReferenceCache metadataFileRefenceCache,
                               ILoggerFactory loggerFactory)
        {
            _inner = inner;
            _metadataFileReferenceCache = metadataFileRefenceCache;
            _logger = loggerFactory.CreateLogger<RoslynWorkspace>();
        }

        public Guid CreateNewProjectID()
        {
            return ProjectId.CreateNewId().Id;
        }

        public string GetProjectPathFromDocumentPath(string documentPath)
        {
            var document = _inner.GetDocument(documentPath);
            return document?.Project?.FilePath;
        }

        public void AddProject(Guid id, string name, string assemblyName, string language, string filePath)
        {
            var info = ProjectInfo.Create(
                ProjectId.CreateFromSerialized(id),
                VersionStamp.Create(),
                name,
                assemblyName,
                language,
                filePath);

            _inner.AddProject(info);
        }

        public void RemoveProject(Guid id)
        {
            _inner.RemoveProject(ProjectId.CreateFromSerialized(id));
        }

        public void AddFileReference(Guid projectId, string filePath)
        {
            _logger.LogInformation($"AddFileReference({projectId}, {filePath})");

            var metaRef = _metadataFileReferenceCache.GetMetadataReference(filePath);
            _inner.AddMetadataReference(ProjectId.CreateFromSerialized(projectId), metaRef);
        }

        public void RemoveFileReference(Guid projectId, string filePath)
        {
            _logger.LogInformation($"RemoveFileReference({projectId}, {filePath})");

            var metaRef = _metadataFileReferenceCache.GetMetadataReference(filePath);
            _inner.RemoveMetadataReference(ProjectId.CreateFromSerialized(projectId), metaRef);
        }

        public void AddProjectReference(Guid projectId, Guid referencedProjectId)
        {
            _logger.LogInformation($"AddProjectReference({projectId}, {referencedProjectId})");

            _inner.AddProjectReference(
                ProjectId.CreateFromSerialized(projectId),
                new ProjectReference(ProjectId.CreateFromSerialized(referencedProjectId)));
        }

        public void RemoveProjectReference(Guid projectId, Guid referencedProjectId)
        {
            _logger.LogInformation($"RemoveProjectReference({projectId}, {referencedProjectId})");

            _inner.RemoveProjectReference(
                ProjectId.CreateFromSerialized(projectId),
                new ProjectReference(ProjectId.CreateFromSerialized(referencedProjectId)));
        }

        public IEnumerable<Guid> GetProjectReferences(Guid projectId)
        {
            var project = _inner.CurrentSolution.GetProject(ProjectId.CreateFromSerialized(projectId));
            return project?.ProjectReferences.Select(reference => reference.ProjectId.Id) ??
                   Enumerable.Empty<Guid>();
        }

        public Guid AddDocument(Guid projectId, string filePath)
        {
            _logger.LogInformation($"AddDocument({projectId}, {filePath})");

            using (var stream = File.OpenRead(filePath))
            {
                // TODO: other encoding option?
                var sourceText = SourceText.From(stream, encoding: Encoding.UTF8);
                var docId = DocumentId.CreateNewId(ProjectId.CreateFromSerialized(projectId));
                var version = VersionStamp.Create();

                var loader = TextLoader.From(TextAndVersion.Create(sourceText, version));

                var doc = DocumentInfo.Create(docId, filePath, filePath: filePath, loader: loader);
                _inner.AddDocument(doc);

                return doc.Id.Id;
            }
        }

        public void RemoveDocument(Guid projectId, Guid id)
        {
            _logger.LogInformation($"RemoveDocument({projectId}, {id})");

            _inner.RemoveDocument(DocumentId.CreateFromSerialized(ProjectId.CreateFromSerialized(projectId), id));
        }

        public IDictionary<string, Guid> GetDocuments(Guid projectId)
        {
            return _inner.CurrentSolution
                         .GetProject(ProjectId.CreateFromSerialized(projectId))
                         .Documents
                         .ToDictionary(doc => doc.FilePath, doc => doc.Id.Id);
        }

        public void SetCSharpCompilationOptions(Guid projectId, GeneralCompilationOptions option)
        {
            SetCSharpCompilationOptions(projectId, projectPath: null, option: option);
        }

        public void SetCSharpCompilationOptions(Guid projectId, string projectPath, GeneralCompilationOptions option)
        {
            _logger.LogInformation($"SetCSharpCompilationOptions({nameof(projectId)}:{projectId}, {nameof(projectPath)}:{projectPath}, {nameof(option)}:{option}");

            var outputKind = (OutputKind)Enum.ToObject(typeof(OutputKind), (int)option.OutputKind);

            var generalDiagnosticOpt = option.WarningsAsErrors ?
                ReportDiagnostic.Error :
                ReportDiagnostic.Default;

            var optimize = option.Optimize ? OptimizationLevel.Release : OptimizationLevel.Debug;

            var csharpOptions = new CSharpCompilationOptions(outputKind)
                .WithAllowUnsafe(option.AllowUnsafe)
                .WithPlatform(ParsePlatfrom(option.Platform))
                .WithGeneralDiagnosticOption(generalDiagnosticOpt)
                .WithOptimizationLevel(optimize)
                .WithConcurrentBuild(option.ConcurrentBuild)
                .WithSpecificDiagnosticOptions(option.DiagnosticsOptions.ToDictionary(
                    pair => pair.Key,
                    pair => (ReportDiagnostic)Enum.ToObject(typeof(ReportDiagnostic), (int)pair.Value)
                ));

            if (!string.IsNullOrEmpty(option.KeyFile))
            {
                if (projectPath == null)
                {
                    _logger.LogError("KeyFile is specified in compilation option, but project path is not given.");
                }
                else
                {
                    var cryptoKeyFile = Path.GetFullPath(Path.Combine(projectPath, option.KeyFile));
                    if (File.Exists(cryptoKeyFile))
                    {
                        var strongNameProvider = new DesktopStrongNameProvider(ImmutableArray.Create(projectPath));
                        csharpOptions = csharpOptions
                            .WithStrongNameProvider(strongNameProvider)
                            .WithCryptoPublicKey(SnkUtils.ExtractPublicKey(File.ReadAllBytes(cryptoKeyFile)));
                    }
                }
            }

            if (option.UseDefaultDesktopAssemblyIdentityComparer)
            {
                csharpOptions = csharpOptions.WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default);
            }

            _inner.SetCompilationOptions(ProjectId.CreateFromSerialized(projectId), csharpOptions);
        }

        public void SetParsingOptions(Guid projectId, GeneralCompilationOptions option)
        {
            var parseOptions = new CSharpParseOptions(languageVersion: ParseLanguageVersion(option.LanguageVersion),
                                                      preprocessorSymbols: option.Defines);

            _inner.SetParseOptions(ProjectId.CreateFromSerialized(projectId), parseOptions);
        }

        public IEnumerable<string> GetAnalyzersInPaths(Guid projectId)
        {
            return _inner.CurrentSolution.GetProject(ProjectId.CreateFromSerialized(projectId))
                        ?.AnalyzerReferences.Select(reference => reference.FullPath)
                        ?? Enumerable.Empty<string>();
        }

        public void AddAnalyzerReference(Guid projectId, string path)
        {
            var project = _inner.CurrentSolution.GetProject(ProjectId.CreateFromSerialized(projectId));
            if (project != null)
            {
                var analyzerFileRef = new AnalyzerFileReference(path, new SimpleAnalyzerAssemblyLoader());
                project.AddAnalyzerReference(analyzerFileRef);
            }
        }

        public void RemoveAnalyzerReference(Guid projectId, string path)
        {
            var project = _inner.CurrentSolution.GetProject(ProjectId.CreateFromSerialized(projectId));
            if (project != null)
            {
                var analyzerReference = project.AnalyzerReferences.FirstOrDefault(
                    analyzer => string.Equals(analyzer.FullPath, path, StringComparison.OrdinalIgnoreCase));

                project.RemoveAnalyzerReference(analyzerReference);
            }
        }

        private static Platform ParsePlatfrom(string value)
        {
            Platform platform;
            if (!Enum.TryParse<Platform>(value, ignoreCase: true, result: out platform))
            {
                platform = Platform.AnyCpu;
            }

            return platform;
        }

        private static LanguageVersion ParseLanguageVersion(string value)
        {
            LanguageVersion languageVersion;
            if (!Enum.TryParse<LanguageVersion>(value, ignoreCase: true, result: out languageVersion))
            {
                languageVersion = LanguageVersion.CSharp6;
            }

            return languageVersion;
        }
    }
}