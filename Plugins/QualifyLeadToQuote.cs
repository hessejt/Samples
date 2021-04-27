using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace Plugins
{
    public partial class QualifyLeadToQuote : BasePlugin
    {
        public QualifyLeadToQuote(string unsecureConfig, string secureConfig) : base(unsecureConfig, secureConfig)
        {
            // Register for any specific events by instantiating a new instance of the 'PluginEvent' class and registering it
            base.RegisteredEvents.Add(new PluginEvent()
            {
                Stage = eStage.PostOperation,
                MessageName = MessageNames.mca_QualifyLeadToQuoteAction,
                PluginAction = ExecutePluginLogic
            });
        }
        public void ExecutePluginLogic(IServiceProvider serviceProvider)
        {
            // Use a 'using' statement to dispose of the service context properly
            // To use a specific early bound entity replace the 'Entity' below with the appropriate class type
            using (var localContext = new LocalPluginContext<Entity>(serviceProvider))
            {
                // Todo: Place your logic here for the plugin
                ITracingService tracingService = localContext.TracingService;
                tracingService.Trace("Plugin execution started");
                bool success = false;
                string ResultMessage = "Unknown Error";
                EntityReference leadReference = null;
                if (localContext.PluginExecutionContext.InputParameters.Contains("Target"))
                {
                    leadReference = localContext.PluginExecutionContext.InputParameters["Target"] as EntityReference;
                }
                if (leadReference != null)
                {
                    try
                    {
                        QualifyHelper qualify = new QualifyHelper();
                        tracingService.Trace("Qualifying Lead");
                        string quoteId = qualify.QualifyLead(leadReference, localContext.OrganizationService);
                        if (quoteId != null)
                        {
                            tracingService.Trace("Lead qualified successfully");
                            success = true;
                            localContext.PluginExecutionContext.OutputParameters["Success"] = success;
                            localContext.PluginExecutionContext.OutputParameters["ResultMessage"] = "OK";
                            localContext.PluginExecutionContext.OutputParameters["ExecutionResult"] = quoteId;
                            SetStateRequest setStateRequest = new SetStateRequest()
                            {
                                EntityMoniker = new EntityReference
                                {
                                    Id = leadReference.Id,
                                    LogicalName = leadReference.LogicalName,
                                },
                                State = new OptionSetValue(1),
                                Status = new OptionSetValue(3)
                            };
                            localContext.OrganizationService.Execute(setStateRequest);
                            tracingService.Trace("Plugin execution completed successfully");
                        }
                        else
                        {
                            throw new InvalidPluginExecutionException("There was an issue creating the quote");
                        }
                    }
                    catch (Exception ex)
                    {
                        success = false;
                        ResultMessage = ex.Message;
                        throw new InvalidPluginExecutionException(ex.Message);
                    }
                }
                else
                {
                    localContext.PluginExecutionContext.OutputParameters["Success"] = success;
                    localContext.PluginExecutionContext.OutputParameters["ResultMessage"] = ResultMessage;
                }
            }
        }
    }
}
