using System;

namespace OmniSharp.ProjectSystemSdk.Components
{
    public class StdioPluginEventEmitter : IPluginEventEmitter
    {
        public Guid Emit(string kind, object args)
        {
            return Emit(kind, args, Guid.NewGuid());
        }

        public Guid Emit(string kind, object args, Guid sessionId)
        {
            Console.WriteLine(Envelope.Serialize(sessionId, kind, args));

            return sessionId;
        }
    }
}