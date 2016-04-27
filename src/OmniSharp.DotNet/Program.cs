using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using OmniSharp.ProjectSystemSdk.Components;
using OmniSharp.ProjectSystemSdk.Models;

namespace OmniSharp.DotNet
{
    public class Program
    {
        private ProcessQueue _listeningQueue;

        public void Run()
        {
            _listeningQueue = new ProcessQueue(new StdioPluginEventEmitter());
            _listeningQueue.OnInitialize += (envelope, emitter) =>
            {
                emitter.Emit(EventTypes.Trace, new { message = $"initalize at {envelope.Data.Value<string>("root")}" });

                new System.Threading.Thread(() =>
                {
                    var projectSystem = new DotNetProjectSystem(
                        root: envelope.Data.Value<string>("root"),
                        workspace: new RemoteCompilationWorkspace(emitter, _listeningQueue),
                        emitter: emitter,
                        listener: _listeningQueue);

                    projectSystem.Initalize(envelope.Data["settings"] as JObject);
                }).Start();
            };

            _listeningQueue.Run();
        }

        public static int Main(string[] args)
        {
            var idx = 0;
            var done = false;
            var hostPid = -1;
            while (!done && idx < args.Length)
            {
                switch (args[idx++])
                {
                    case "--host-pid":
                        hostPid = int.Parse(args[idx++]);
                        break;
                    default:
                        done = true;
                        break;
                }
            }

            Console.WriteLine($".NET CLI Project System. Depend on {hostPid} ...");

            var arguments = new List<string>();
            for (int i = idx; i < args.Length; ++i)
            {
                arguments.Add(args[i]);
            }

            var program = new Program();

            if (hostPid == -1)
            {
                Console.Error.WriteLine("Host PID is expected");
                return 1;
            }
            else
            {
                var hostProcess = Process.GetProcessById(hostPid);
                hostProcess.EnableRaisingEvents = true;
                hostProcess.Exited += (s, e) =>
                {
                    Process.GetCurrentProcess().Kill();
                };
            }

            program.Run();

            return 0;
        }
    }
}