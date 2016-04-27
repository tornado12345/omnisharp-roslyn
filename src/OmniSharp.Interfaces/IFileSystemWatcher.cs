using System;

namespace OmniSharp.Interfaces
{
    // TODO: Flesh out this API more
    public interface IFileSystemWatcher
    {
        void Watch(string path, Action<string> callback);

        void TriggerChange(string path);
    }
}