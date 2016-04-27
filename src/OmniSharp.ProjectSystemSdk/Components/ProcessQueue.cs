using System;
using System.Threading;

namespace OmniSharp.ProjectSystemSdk.Components
{
    public class ProcessQueue
    {
        private readonly IPluginEventEmitter _emitter;

        private Thread _listener;

        public ProcessQueue(IPluginEventEmitter emitter)
        {
            _emitter = emitter;
        }

        public void Run()
        {
            _listener = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        var line = Console.ReadLine();
                        var envelope = Envelope.Deserialize(line);

                        if (envelope == null)
                        {
                            Console.Error.WriteLine($"invalid:{line}");
                        }
                        else
                        {
                            switch (envelope.Kind)
                            {
                                case Models.EventTypes.ProjectSystemInitialize:
                                    OnInitialize(envelope, _emitter);
                                    break;
                                case Models.EventTypes.CompilationWorkspace:
                                    OnCompilationEvent(envelope, _emitter);
                                    break;
                                case Models.EventTypes.WorkspaceInformation:
                                    OnWorkspaceInformation(envelope, _emitter);
                                    break;
                                default:
                                    Console.Error.WriteLine($"unknown type of request [{line}]");
                                    break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _emitter?.Emit(Models.EventTypes.Trace, new { message = ex.Message, stack = ex.StackTrace });
                    }
                }
            });

            _listener.Start();
            _listener.Join();
        }

        public event Action<Envelope, IPluginEventEmitter> OnInitialize;

        public event Action<Envelope, IPluginEventEmitter> OnCompilationEvent;

        public event Action<Envelope, IPluginEventEmitter> OnWorkspaceInformation;
    }
}
