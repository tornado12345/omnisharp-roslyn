using System;

namespace OmniSharp.ProjectSystemSdk
{
    public interface IPluginEventEmitter
    {
        Guid Emit(string kind, object args);
        
        Guid Emit(string kind, object args, Guid sessionId);
    }
}