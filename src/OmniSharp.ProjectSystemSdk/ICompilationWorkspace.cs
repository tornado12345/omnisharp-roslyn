using System;
using System.Collections.Generic;

namespace OmniSharp.ProjectSystemSdk
{
    public interface ICompilationWorkspace
    {
        Guid CreateNewProjectID();

        string GetProjectPathFromDocumentPath(string path);

        void AddProject(Guid id, string name, string assemblyName, string language, string filePath);

        void RemoveProject(Guid id);

        void AddFileReference(Guid projectId, string filePath);

        void RemoveFileReference(Guid projectId, string filePath);

        IEnumerable<Guid> GetProjectReferences(Guid projectId);

        void AddProjectReference(Guid projectId, Guid referencedProjectId);

        void RemoveProjectReference(Guid projectId, Guid referencedProjectId);

        IDictionary<string, Guid> GetDocuments(Guid projectId);

        Guid AddDocument(Guid projectId, string filePath);

        void RemoveDocument(Guid projectId, Guid id);

        void SetCSharpCompilationOptions(Guid projectId, GeneralCompilationOptions options);

        void SetCSharpCompilationOptions(Guid projectId, string projectPath, GeneralCompilationOptions options);

        void SetParsingOptions(Guid projectId, GeneralCompilationOptions option);

        IEnumerable<string> GetAnalyzersInPaths(Guid projectId);

        void AddAnalyzerReference(Guid projectId, string path);

        void RemoveAnalyzerReference(Guid projectId, string path);
    }
}