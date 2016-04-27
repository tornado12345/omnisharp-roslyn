using System.Composition;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Mef;
using OmniSharp.Models;
using OmniSharp.Models.v1;
using OmniSharp.ProjectSystemSdk.Server;

namespace OmniSharp
{
    [OmniSharpHandler(OmnisharpEndpoints.WorkspaceInformation, "Projects")]
    public class WorkspaceInformationService : RequestHandler<WorkspaceInformationRequest, WorkspaceInformationResponse>
    {
        private readonly PluginManager _pluginManager;
        private readonly ILogger _logger;

        [ImportingConstructor]
        public WorkspaceInformationService([Import] PluginManager pluginManager,
                                           [Import] ILoggerFactory loggerFactory)
        {
            _pluginManager = pluginManager;
           _logger = loggerFactory.CreateLogger<WorkspaceInformationService>();
        }

        public async Task<WorkspaceInformationResponse> Handle(WorkspaceInformationRequest request)
        {
            var response = new WorkspaceInformationResponse();

            var models = await _pluginManager.GetInformationModels(request);

            foreach (var model in models)
            {
                response.Add(model.Key, model.Value);
            }
            
            _logger.LogInformation($"set response with {models.Count} model.");

            return response;
        }
    }
}