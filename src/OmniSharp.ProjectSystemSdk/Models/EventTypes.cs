namespace OmniSharp.ProjectSystemSdk.Models
{
    // TODO: this is a copy from OmniSharp.Abstraction. It should be removed from that project
    //       eventually.
    public static class EventTypes
    {
        public static readonly string ProjectAdded = "ProjectAdded";
        public static readonly string ProjectChanged = "ProjectChanged";
        public static readonly string ProjectRemoved = "ProjectRemoved";
        public static readonly string Error = "Error";
        public static readonly string MsBuildProjectDiagnostics = "MsBuildProjectDiagnostics";
        public static readonly string PackageRestoreStarted = "PackageRestoreStarted";
        public static readonly string PackageRestoreFinished = "PackageRestoreFinished";
        public static readonly string UnresolvedDependencies = "UnresolvedDependencies";
        
        // Plugin-Host protocal
        public const string CompilationWorkspace = nameof(CompilationWorkspace);
        
        public const string ProjectSystemInitialize = nameof(ProjectSystemInitialize);
        
        public const string Trace = nameof(Trace);
        
        public const string WorkspaceInformation = nameof(WorkspaceInformation);
    }
}