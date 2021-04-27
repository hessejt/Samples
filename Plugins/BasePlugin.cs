﻿using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;

namespace Plugins
{
    public abstract partial class BasePlugin : IPlugin
    {
        protected class LocalPluginContext<T> : IDisposable where T : Entity
        {
            internal Microsoft.Xrm.Sdk.Client.OrganizationServiceContext CrmContext { get; private set; }
            internal IServiceProvider ServiceProvider { get; private set; }
            internal IOrganizationServiceFactory ServiceFactory { get; private set; }
            internal IOrganizationService OrganizationService { get; private set; }
            internal IPluginExecutionContext PluginExecutionContext { get; private set; }
            internal ITracingService TracingService { get; private set; }
            internal eStage Stage { get { return (eStage)this.PluginExecutionContext.Stage; } }
            internal int Depth { get { return this.PluginExecutionContext.Depth; } }
            internal string MessageName { get { return this.PluginExecutionContext.MessageName; } }
            internal LocalPluginContext(IServiceProvider serviceProvider)
            {
                if (serviceProvider == null)
                    throw new ArgumentNullException("serviceProvider");

                // Obtain the tracing service from the service provider.
                this.TracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

                // Obtain the execution context service from the service provider.
                this.PluginExecutionContext = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

                // Obtain the Organization Service factory service from the service provider
                this.ServiceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

                // Use the factory to generate the Organization Service.
                this.OrganizationService = this.ServiceFactory.CreateOrganizationService(this.PluginExecutionContext.UserId);

                // Generate the CrmContext to use with LINQ etc
                this.CrmContext = new Microsoft.Xrm.Sdk.Client.OrganizationServiceContext(this.OrganizationService);
            }

            internal void Trace(string message)
            {
                if (string.IsNullOrWhiteSpace(message) || this.TracingService == null) return;

                if (this.PluginExecutionContext == null)
                    this.TracingService.Trace(message);
                else
                {
                    this.TracingService.Trace(
                        "{0}, Correlation Id: {1}, Initiating User: {2}",
                        message,
                        this.PluginExecutionContext.CorrelationId,
                        this.PluginExecutionContext.InitiatingUserId);
                }
            }

            public void Dispose()
            {
                if (this.CrmContext != null)
                    this.CrmContext.Dispose();
            }
            /// <summary>
            /// Returns the first registered 'Pre' image for the pipeline execution
            /// </summary>
            internal T PreImage
            {
                get
                {
                    if (this.PluginExecutionContext.PreEntityImages.Any())
                        return GetEntityAsType(this.PluginExecutionContext.PreEntityImages[this.PluginExecutionContext.PreEntityImages.FirstOrDefault().Key]);
                    return null;
                }
            }
            /// <summary>
            /// Returns the first registered 'Post' image for the pipeline execution
            /// </summary>
            internal T PostImage
            {
                get
                {
                    if (this.PluginExecutionContext.PostEntityImages.Any())
                        return GetEntityAsType(this.PluginExecutionContext.PostEntityImages[this.PluginExecutionContext.PostEntityImages.FirstOrDefault().Key]);
                    return null;
                }
            }
            /// <summary>
            /// Returns the 'Target' of the message if available
            /// This is an 'Entity' instead of the specified type in order to retain the same instance of the 'Entity' object. This allows for updates to the target in a 'Pre' stage that
            /// will get persisted during the transaction.
            /// </summary>
            internal Entity TargetEntity
            {
                get
                {
                    if (this.PluginExecutionContext.InputParameters.Contains("Target"))
                        return this.PluginExecutionContext.InputParameters["Target"] as Entity;
                    return null;
                }
            }
            /// <summary>
            /// Returns the 'Target' of the message as an EntityReference if available
            /// </summary>
            internal EntityReference TargetEntityReference
            {
                get
                {
                    if (this.PluginExecutionContext.InputParameters.Contains("Target"))
                        return this.PluginExecutionContext.InputParameters["Target"] as EntityReference;
                    return null;
                }
            }
            private T GetEntityAsType(Entity entity)
            {
                if (typeof(T) == entity.GetType())
                    return entity as T;
                else
                    return entity.ToEntity<T>();
            }
        }
        protected enum eStage
        {
            PreValidation = 10,
            PreOperation = 20,
            PostOperation = 40
        }
        protected class PluginEvent
        {
            /// <summary>
            /// Execution pipeline stage that the plugin should be registered against.
            /// </summary>
            public eStage Stage { get; set; }
            /// <summary>
            /// Logical name of the entity that the plugin should be registered against. Leave 'null' to register against all entities.
            /// </summary>
            public string EntityName { get; set; }
            /// <summary>
            /// Name of the message that the plugin should be triggered off of.
            /// </summary>
            public string MessageName { get; set; }
            /// <summary>
            /// Method that should be executed when the conditions of the Plugin Event have been met.
            /// </summary>
            public Action<IServiceProvider> PluginAction { get; set; }
        }

        private Collection<PluginEvent> registeredEvents;

        /// <summary>
        /// Gets the List of events that the plug-in should fire for. Each List
        /// </summary>
        protected Collection<PluginEvent> RegisteredEvents
        {
            get
            {
                if (this.registeredEvents == null)
                    this.registeredEvents = new Collection<PluginEvent>();
                return this.registeredEvents;
            }
        }

        /// <summary>
        /// Initializes a new instance of the BasePlugin class.
        /// </summary>
        internal BasePlugin(string unsecureConfig, string secureConfig)
        {
            this.UnsecureConfig = unsecureConfig;
            this.SecureConfig = secureConfig;
        }
        /// <summary>
        /// Un secure configuration specified during the registration of the plugin step
        /// </summary>
        public string UnsecureConfig { get; private set; }

        /// <summary>
        /// Secure configuration specified during the registration of the plugin step
        /// </summary>
        public string SecureConfig { get; private set; }

        /// <summary>
        /// Executes the plug-in.
        /// </summary>
        /// <param name="serviceProvider">The service provider.</param>
        /// <remarks>
        /// For improved performance, Microsoft Dynamics CRM caches plug-in instances. 
        /// The plug-in's Execute method should be written to be stateless as the constructor 
        /// is not called for every invocation of the plug-in. Also, multiple system threads 
        /// could execute the plug-in at the same time. All per invocation state information 
        /// is stored in the context. This means that you should not use global variables in plug-ins.
        /// </remarks>
        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new ArgumentNullException("serviceProvider");

            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var pluginContext = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            tracingService.Trace(string.Format(CultureInfo.InvariantCulture, "Entered {0}.Execute()", this.GetType().ToString()));
            try
            {
                // Iterate over all of the expected registered events to ensure that the plugin
                // has been invoked by an expected event
                var entityActions =
                    (from a in this.RegisteredEvents
                     where (
                        (int)a.Stage == pluginContext.Stage &&
                         (string.IsNullOrWhiteSpace(a.MessageName) ? true : a.MessageName.ToLowerInvariant() == pluginContext.MessageName.ToLowerInvariant()) &&
                         (string.IsNullOrWhiteSpace(a.EntityName) ? true : a.EntityName.ToLowerInvariant() == pluginContext.PrimaryEntityName.ToLowerInvariant())
                     )
                     select a.PluginAction);

                if (entityActions.Any())
                {
                    foreach (var entityAction in entityActions)
                    {
                        tracingService.Trace(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} is firing for Entity: {1}, Message: {2}, Method: {3}",
                            this.GetType().ToString(),
                            pluginContext.PrimaryEntityName,
                            pluginContext.MessageName,
                            entityAction.Method.Name));

                        entityAction.Invoke(serviceProvider);
                    }
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace(string.Format(CultureInfo.InvariantCulture, "Exception: {0}", ex.ToString()));
                throw;
            }
            finally
            {
                tracingService.Trace(string.Format(CultureInfo.InvariantCulture, "Exiting {0}.Execute()", this.GetType().ToString()));
            }
        }
    }
    public struct EntityNames
    {
        public static readonly string abdg_datagrid = "abdg_datagrid";
        public static readonly string abdg_datagridcolumn = "abdg_datagridcolumn";
        public static readonly string abdg_datagridcriteria = "abdg_datagridcriteria";
        public static readonly string abdg_datagridcriteriagroup = "abdg_datagridcriteriagroup";
        public static readonly string abdg_datagridevent = "abdg_datagridevent";
        public static readonly string account = "account";
        public static readonly string accountleads = "accountleads";
        public static readonly string aciviewmapper = "aciviewmapper";
        public static readonly string actioncard = "actioncard";
        public static readonly string actioncardusersettings = "actioncardusersettings";
        public static readonly string actioncarduserstate = "actioncarduserstate";
        public static readonly string activitymimeattachment = "activitymimeattachment";
        public static readonly string activityparty = "activityparty";
        public static readonly string activitypointer = "activitypointer";
        public static readonly string adminsettingsentity = "adminsettingsentity";
        public static readonly string advancedsimilarityrule = "advancedsimilarityrule";
        public static readonly string adx_accesscontrolrule_publishingstate = "adx_accesscontrolrule_publishingstate";
        public static readonly string adx_ad = "adx_ad";
        public static readonly string adx_adplacement = "adx_adplacement";
        public static readonly string adx_adplacement_ad = "adx_adplacement_ad";
        public static readonly string adx_alertsubscription = "adx_alertsubscription";
        public static readonly string adx_bpf_c2857b638fa7473d8e2f112c232cebd8 = "adx_bpf_c2857b638fa7473d8e2f112c232cebd8";
        public static readonly string adx_contentsnippet = "adx_contentsnippet";
        public static readonly string adx_entityform = "adx_entityform";
        public static readonly string adx_entityformmetadata = "adx_entityformmetadata";
        public static readonly string adx_entitylist = "adx_entitylist";
        public static readonly string adx_entitypermission = "adx_entitypermission";
        public static readonly string adx_entitypermission_webrole = "adx_entitypermission_webrole";
        public static readonly string adx_externalidentity = "adx_externalidentity";
        public static readonly string adx_invitation = "adx_invitation";
        public static readonly string adx_invitation_invitecontacts = "adx_invitation_invitecontacts";
        public static readonly string adx_invitation_redeemedcontacts = "adx_invitation_redeemedcontacts";
        public static readonly string adx_invitation_webrole = "adx_invitation_webrole";
        public static readonly string adx_inviteredemption = "adx_inviteredemption";
        public static readonly string adx_kbarticle_kbarticle = "adx_kbarticle_kbarticle";
        public static readonly string adx_pagealert = "adx_pagealert";
        public static readonly string adx_pagenotification = "adx_pagenotification";
        public static readonly string adx_pagetag = "adx_pagetag";
        public static readonly string adx_pagetag_webpage = "adx_pagetag_webpage";
        public static readonly string adx_pagetemplate = "adx_pagetemplate";
        public static readonly string adx_poll = "adx_poll";
        public static readonly string adx_polloption = "adx_polloption";
        public static readonly string adx_pollplacement = "adx_pollplacement";
        public static readonly string adx_pollplacement_poll = "adx_pollplacement_poll";
        public static readonly string adx_pollsubmission = "adx_pollsubmission";
        public static readonly string adx_portalcomment = "adx_portalcomment";
        public static readonly string adx_portallanguage = "adx_portallanguage";
        public static readonly string adx_publishingstate = "adx_publishingstate";
        public static readonly string adx_publishingstatetransitionrule = "adx_publishingstatetransitionrule";
        public static readonly string adx_publishingstatetransitionrule_webrole = "adx_publishingstatetransitionrule_webrole";
        public static readonly string adx_redirect = "adx_redirect";
        public static readonly string adx_setting = "adx_setting";
        public static readonly string adx_shortcut = "adx_shortcut";
        public static readonly string adx_sitemarker = "adx_sitemarker";
        public static readonly string adx_sitesetting = "adx_sitesetting";
        public static readonly string adx_tag = "adx_tag";
        public static readonly string adx_urlhistory = "adx_urlhistory";
        public static readonly string adx_webfile = "adx_webfile";
        public static readonly string adx_webfilelog = "adx_webfilelog";
        public static readonly string adx_webform = "adx_webform";
        public static readonly string adx_webformmetadata = "adx_webformmetadata";
        public static readonly string adx_webformsession = "adx_webformsession";
        public static readonly string adx_webformstep = "adx_webformstep";
        public static readonly string adx_weblink = "adx_weblink";
        public static readonly string adx_weblinkset = "adx_weblinkset";
        public static readonly string adx_webnotificationentity = "adx_webnotificationentity";
        public static readonly string adx_webnotificationurl = "adx_webnotificationurl";
        public static readonly string adx_webpage = "adx_webpage";
        public static readonly string adx_webpage_tag = "adx_webpage_tag";
        public static readonly string adx_webpageaccesscontrolrule = "adx_webpageaccesscontrolrule";
        public static readonly string adx_webpageaccesscontrolrule_webrole = "adx_webpageaccesscontrolrule_webrole";
        public static readonly string adx_webpagehistory = "adx_webpagehistory";
        public static readonly string adx_webpagelog = "adx_webpagelog";
        public static readonly string adx_webrole = "adx_webrole";
        public static readonly string adx_webrole_account = "adx_webrole_account";
        public static readonly string adx_webrole_contact = "adx_webrole_contact";
        public static readonly string adx_webrole_systemuser = "adx_webrole_systemuser";
        public static readonly string adx_website = "adx_website";
        public static readonly string adx_website_list = "adx_website_list";
        public static readonly string adx_website_sponsor = "adx_website_sponsor";
        public static readonly string adx_websiteaccess = "adx_websiteaccess";
        public static readonly string adx_websiteaccess_webrole = "adx_websiteaccess_webrole";
        public static readonly string adx_websitebinding = "adx_websitebinding";
        public static readonly string adx_websitelanguage = "adx_websitelanguage";
        public static readonly string adx_webtemplate = "adx_webtemplate";
        public static readonly string annotation = "annotation";
        public static readonly string annualfiscalcalendar = "annualfiscalcalendar";
        public static readonly string apisettings = "apisettings";
        public static readonly string appconfig = "appconfig";
        public static readonly string appconfiginstance = "appconfiginstance";
        public static readonly string appconfigmaster = "appconfigmaster";
        public static readonly string appelement = "appelement";
        public static readonly string applicationfile = "applicationfile";
        public static readonly string applicationuser = "applicationuser";
        public static readonly string applicationuserrole = "applicationuserrole";
        public static readonly string appmodule = "appmodule";
        public static readonly string appmodulecomponent = "appmodulecomponent";
        public static readonly string appmodulecomponentedge = "appmodulecomponentedge";
        public static readonly string appmodulecomponentnode = "appmodulecomponentnode";
        public static readonly string appmodulemetadata = "appmodulemetadata";
        public static readonly string appmodulemetadatadependency = "appmodulemetadatadependency";
        public static readonly string appmodulemetadataoperationlog = "appmodulemetadataoperationlog";
        public static readonly string appmoduleroles = "appmoduleroles";
        public static readonly string appointment = "appointment";
        public static readonly string appsetting = "appsetting";
        public static readonly string asyncoperation = "asyncoperation";
        public static readonly string attachment = "attachment";
        public static readonly string attribute = "attribute";
        public static readonly string attributeimageconfig = "attributeimageconfig";
        public static readonly string attributemap = "attributemap";
        public static readonly string audit = "audit";
        public static readonly string authorizationserver = "authorizationserver";
        public static readonly string azureserviceconnection = "azureserviceconnection";
        public static readonly string bookableresource = "bookableresource";
        public static readonly string bookableresourcebooking = "bookableresourcebooking";
        public static readonly string bookableresourcebookingexchangesyncidmapping = "bookableresourcebookingexchangesyncidmapping";
        public static readonly string bookableresourcebookingheader = "bookableresourcebookingheader";
        public static readonly string bookableresourcecategory = "bookableresourcecategory";
        public static readonly string bookableresourcecategoryassn = "bookableresourcecategoryassn";
        public static readonly string bookableresourcecharacteristic = "bookableresourcecharacteristic";
        public static readonly string bookableresourcegroup = "bookableresourcegroup";
        public static readonly string bookingstatus = "bookingstatus";
        public static readonly string bulkdeletefailure = "bulkdeletefailure";
        public static readonly string bulkdeleteoperation = "bulkdeleteoperation";
        public static readonly string bulkoperation = "bulkoperation";
        public static readonly string bulkoperationlog = "bulkoperationlog";
        public static readonly string businessdatalocalizedlabel = "businessdatalocalizedlabel";
        public static readonly string businessprocessflowinstance = "businessprocessflowinstance";
        public static readonly string businessunit = "businessunit";
        public static readonly string businessunitmap = "businessunitmap";
        public static readonly string businessunitnewsarticle = "businessunitnewsarticle";
        public static readonly string calendar = "calendar";
        public static readonly string calendarrule = "calendarrule";
        public static readonly string callbackregistration = "callbackregistration";
        public static readonly string campaign = "campaign";
        public static readonly string campaignactivity = "campaignactivity";
        public static readonly string campaignactivityitem = "campaignactivityitem";
        public static readonly string campaignitem = "campaignitem";
        public static readonly string campaignresponse = "campaignresponse";
        public static readonly string canvasapp = "canvasapp";
        public static readonly string canvasappextendedmetadata = "canvasappextendedmetadata";
        public static readonly string cardtype = "cardtype";
        public static readonly string category = "category";
        public static readonly string cdi_anonymousvisitor = "cdi_anonymousvisitor";
        public static readonly string cdi_automation = "cdi_automation";
        public static readonly string cdi_bulktxtmessage = "cdi_bulktxtmessage";
        public static readonly string cdi_bulktxtmessage_list = "cdi_bulktxtmessage_list";
        public static readonly string cdi_category = "cdi_category";
        public static readonly string cdi_cdi_datasync_list = "cdi_cdi_datasync_list";
        public static readonly string cdi_datasync = "cdi_datasync";
        public static readonly string cdi_domain = "cdi_domain";
        public static readonly string cdi_emailcname = "cdi_emailcname";
        public static readonly string cdi_emailevent = "cdi_emailevent";
        public static readonly string cdi_emailsend = "cdi_emailsend";
        public static readonly string cdi_emailsend_account = "cdi_emailsend_account";
        public static readonly string cdi_emailsend_cdi_webcontent = "cdi_emailsend_cdi_webcontent";
        public static readonly string cdi_emailsend_contact = "cdi_emailsend_contact";
        public static readonly string cdi_emailsend_lead = "cdi_emailsend_lead";
        public static readonly string cdi_emailsend_list = "cdi_emailsend_list";
        public static readonly string cdi_emailsend_suppressed_list = "cdi_emailsend_suppressed_list";
        public static readonly string cdi_emailstatistics = "cdi_emailstatistics";
        public static readonly string cdi_emailtemplate = "cdi_emailtemplate";
        public static readonly string cdi_event = "cdi_event";
        public static readonly string cdi_event_cdi_automation = "cdi_event_cdi_automation";
        public static readonly string cdi_eventparticipation = "cdi_eventparticipation";
        public static readonly string cdi_excludedemail = "cdi_excludedemail";
        public static readonly string cdi_executesend = "cdi_executesend";
        public static readonly string cdi_executesocialpost = "cdi_executesocialpost";
        public static readonly string cdi_filter = "cdi_filter";
        public static readonly string cdi_formcapture = "cdi_formcapture";
        public static readonly string cdi_formcapturefield = "cdi_formcapturefield";
        public static readonly string cdi_formfield = "cdi_formfield";
        public static readonly string cdi_import = "cdi_import";
        public static readonly string cdi_importlog = "cdi_importlog";
        public static readonly string cdi_iporganization = "cdi_iporganization";
        public static readonly string cdi_lead_product = "cdi_lead_product";
        public static readonly string cdi_nurturebuilder = "cdi_nurturebuilder";
        public static readonly string cdi_nurturebuilder_list = "cdi_nurturebuilder_list";
        public static readonly string cdi_optionmapping = "cdi_optionmapping";
        public static readonly string cdi_pageview = "cdi_pageview";
        public static readonly string cdi_postedfield = "cdi_postedfield";
        public static readonly string cdi_postedform = "cdi_postedform";
        public static readonly string cdi_postedsubscription = "cdi_postedsubscription";
        public static readonly string cdi_postedsurvey = "cdi_postedsurvey";
        public static readonly string cdi_profile = "cdi_profile";
        public static readonly string cdi_quicksendprivilege = "cdi_quicksendprivilege";
        public static readonly string cdi_runnurture = "cdi_runnurture";
        public static readonly string cdi_securitysession = "cdi_securitysession";
        public static readonly string cdi_sendemail = "cdi_sendemail";
        public static readonly string cdi_sentemail = "cdi_sentemail";
        public static readonly string cdi_setting = "cdi_setting";
        public static readonly string cdi_socialclick = "cdi_socialclick";
        public static readonly string cdi_socialpost = "cdi_socialpost";
        public static readonly string cdi_subscriptionlist = "cdi_subscriptionlist";
        public static readonly string cdi_subscriptionpreference = "cdi_subscriptionpreference";
        public static readonly string cdi_surveyanswer = "cdi_surveyanswer";
        public static readonly string cdi_surveyquestion = "cdi_surveyquestion";
        public static readonly string cdi_txtmessage = "cdi_txtmessage";
        public static readonly string cdi_unsubscribe = "cdi_unsubscribe";
        public static readonly string cdi_unsubscribe_cdi_subscriptionlist = "cdi_unsubscribe_cdi_subscriptionlist";
        public static readonly string cdi_usersession = "cdi_usersession";
        public static readonly string cdi_visit = "cdi_visit";
        public static readonly string cdi_webcontent = "cdi_webcontent";
        public static readonly string channelaccessprofile = "channelaccessprofile";
        public static readonly string channelaccessprofileentityaccesslevel = "channelaccessprofileentityaccesslevel";
        public static readonly string channelaccessprofilerule = "channelaccessprofilerule";
        public static readonly string channelaccessprofileruleitem = "channelaccessprofileruleitem";
        public static readonly string channelproperty = "channelproperty";
        public static readonly string channelpropertygroup = "channelpropertygroup";
        public static readonly string characteristic = "characteristic";
        public static readonly string childincidentcount = "childincidentcount";
        public static readonly string clientupdate = "clientupdate";
        public static readonly string columnmapping = "columnmapping";
        public static readonly string commitment = "commitment";
        public static readonly string competitor = "competitor";
        public static readonly string competitoraddress = "competitoraddress";
        public static readonly string competitorproduct = "competitorproduct";
        public static readonly string competitorsalesliterature = "competitorsalesliterature";
        public static readonly string complexcontrol = "complexcontrol";
        public static readonly string connection = "connection";
        public static readonly string connectionreference = "connectionreference";
        public static readonly string connectionrole = "connectionrole";
        public static readonly string connectionroleassociation = "connectionroleassociation";
        public static readonly string connectionroleobjecttypecode = "connectionroleobjecttypecode";
        public static readonly string connector = "connector";
        public static readonly string constraintbasedgroup = "constraintbasedgroup";
        public static readonly string contact = "contact";
        public static readonly string contactinvoices = "contactinvoices";
        public static readonly string contactleads = "contactleads";
        public static readonly string contactorders = "contactorders";
        public static readonly string contactquotes = "contactquotes";
        public static readonly string contract = "contract";
        public static readonly string contractdetail = "contractdetail";
        public static readonly string contracttemplate = "contracttemplate";
        public static readonly string convertrule = "convertrule";
        public static readonly string convertruleitem = "convertruleitem";
        public static readonly string crowe_nameduser = "crowe_nameduser";
        public static readonly string crowe_solutionactivation = "crowe_solutionactivation";
        public static readonly string crowe_solutionsettings = "crowe_solutionsettings";
        public static readonly string customcontrol = "customcontrol";
        public static readonly string customcontroldefaultconfig = "customcontroldefaultconfig";
        public static readonly string customcontrolresource = "customcontrolresource";
        public static readonly string customeraddress = "customeraddress";
        public static readonly string customeropportunityrole = "customeropportunityrole";
        public static readonly string customerrelationship = "customerrelationship";
        public static readonly string datalakeworkspace = "datalakeworkspace";
        public static readonly string datalakeworkspacepermission = "datalakeworkspacepermission";
        public static readonly string dataperformance = "dataperformance";
        public static readonly string delveactionhub = "delveactionhub";
        public static readonly string dependency = "dependency";
        public static readonly string dependencyfeature = "dependencyfeature";
        public static readonly string dependencynode = "dependencynode";
        public static readonly string discount = "discount";
        public static readonly string discounttype = "discounttype";
        public static readonly string displaystring = "displaystring";
        public static readonly string displaystringmap = "displaystringmap";
        public static readonly string documentindex = "documentindex";
        public static readonly string documenttemplate = "documenttemplate";
        public static readonly string duplicaterecord = "duplicaterecord";
        public static readonly string duplicaterule = "duplicaterule";
        public static readonly string duplicaterulecondition = "duplicaterulecondition";
        public static readonly string dynamicproperty = "dynamicproperty";
        public static readonly string dynamicpropertyassociation = "dynamicpropertyassociation";
        public static readonly string dynamicpropertyinstance = "dynamicpropertyinstance";
        public static readonly string dynamicpropertyoptionsetitem = "dynamicpropertyoptionsetitem";
        public static readonly string email = "email";
        public static readonly string emailhash = "emailhash";
        public static readonly string emailsearch = "emailsearch";
        public static readonly string emailserverprofile = "emailserverprofile";
        public static readonly string emailsignature = "emailsignature";
        public static readonly string entitlement = "entitlement";
        public static readonly string entitlementchannel = "entitlementchannel";
        public static readonly string entitlementcontacts = "entitlementcontacts";
        public static readonly string entitlemententityallocationtypemapping = "entitlemententityallocationtypemapping";
        public static readonly string entitlementproducts = "entitlementproducts";
        public static readonly string entitlementtemplate = "entitlementtemplate";
        public static readonly string entitlementtemplatechannel = "entitlementtemplatechannel";
        public static readonly string entitlementtemplateproducts = "entitlementtemplateproducts";
        public static readonly string entity = "entity";
        public static readonly string entityanalyticsconfig = "entityanalyticsconfig";
        public static readonly string entitydataprovider = "entitydataprovider";
        public static readonly string entitydatasource = "entitydatasource";
        public static readonly string entityimageconfig = "entityimageconfig";
        public static readonly string entitykey = "entitykey";
        public static readonly string entitymap = "entitymap";
        public static readonly string entityrelationship = "entityrelationship";
        public static readonly string environmentvariabledefinition = "environmentvariabledefinition";
        public static readonly string environmentvariablevalue = "environmentvariablevalue";
        public static readonly string equipment = "equipment";
        public static readonly string exchangesyncidmapping = "exchangesyncidmapping";
        public static readonly string expanderevent = "expanderevent";
        public static readonly string expiredprocess = "expiredprocess";
        public static readonly string exportsolutionupload = "exportsolutionupload";
        public static readonly string externalparty = "externalparty";
        public static readonly string externalpartyitem = "externalpartyitem";
        public static readonly string fax = "fax";
        public static readonly string feedback = "feedback";
        public static readonly string fieldpermission = "fieldpermission";
        public static readonly string fieldsecurityprofile = "fieldsecurityprofile";
        public static readonly string fileattachment = "fileattachment";
        public static readonly string filtertemplate = "filtertemplate";
        public static readonly string fixedmonthlyfiscalcalendar = "fixedmonthlyfiscalcalendar";
        public static readonly string flowsession = "flowsession";
        public static readonly string globalsearchconfiguration = "globalsearchconfiguration";
        public static readonly string goal = "goal";
        public static readonly string goalrollupquery = "goalrollupquery";
        public static readonly string hierarchyrule = "hierarchyrule";
        public static readonly string hierarchysecurityconfiguration = "hierarchysecurityconfiguration";
        public static readonly string holidaywrapper = "holidaywrapper";
        public static readonly string imagedescriptor = "imagedescriptor";
        public static readonly string import = "import";
        public static readonly string importdata = "importdata";
        public static readonly string importentitymapping = "importentitymapping";
        public static readonly string importfile = "importfile";
        public static readonly string importjob = "importjob";
        public static readonly string importlog = "importlog";
        public static readonly string importmap = "importmap";
        public static readonly string incident = "incident";
        public static readonly string incidentknowledgebaserecord = "incidentknowledgebaserecord";
        public static readonly string incidentresolution = "incidentresolution";
        public static readonly string integrationstatus = "integrationstatus";
        public static readonly string interactionforemail = "interactionforemail";
        public static readonly string internaladdress = "internaladdress";
        public static readonly string interprocesslock = "interprocesslock";
        public static readonly string invaliddependency = "invaliddependency";
        public static readonly string invoice = "invoice";
        public static readonly string invoicedetail = "invoicedetail";
        public static readonly string isvconfig = "isvconfig";
        public static readonly string iv_insightsconfiguration = "iv_insightsconfiguration";
        public static readonly string kbarticle = "kbarticle";
        public static readonly string kbarticlecomment = "kbarticlecomment";
        public static readonly string kbarticletemplate = "kbarticletemplate";
        public static readonly string knowledgearticle = "knowledgearticle";
        public static readonly string knowledgearticleincident = "knowledgearticleincident";
        public static readonly string knowledgearticlescategories = "knowledgearticlescategories";
        public static readonly string knowledgearticleviews = "knowledgearticleviews";
        public static readonly string knowledgebaserecord = "knowledgebaserecord";
        public static readonly string knowledgesearchmodel = "knowledgesearchmodel";
        public static readonly string languagelocale = "languagelocale";
        public static readonly string languageprovisioningstate = "languageprovisioningstate";
        public static readonly string lead = "lead";
        public static readonly string leadaddress = "leadaddress";
        public static readonly string leadcompetitors = "leadcompetitors";
        public static readonly string leadproduct = "leadproduct";
        public static readonly string leadtoopportunitysalesprocess = "leadtoopportunitysalesprocess";
        public static readonly string letter = "letter";
        public static readonly string li_inmail = "li_inmail";
        public static readonly string li_message = "li_message";
        public static readonly string li_pointdrivepresentationviewed = "li_pointdrivepresentationviewed";
        public static readonly string license = "license";
        public static readonly string list = "list";
        public static readonly string listmember = "listmember";
        public static readonly string listoperation = "listoperation";
        public static readonly string localconfigstore = "localconfigstore";
        public static readonly string lookupmapping = "lookupmapping";
        public static readonly string mailbox = "mailbox";
        public static readonly string mailboxstatistics = "mailboxstatistics";
        public static readonly string mailboxtrackingcategory = "mailboxtrackingcategory";
        public static readonly string mailboxtrackingfolder = "mailboxtrackingfolder";
        public static readonly string mailmergetemplate = "mailmergetemplate";
        public static readonly string managedproperty = "managedproperty";
        public static readonly string mbs_pluginprofile = "mbs_pluginprofile";
        public static readonly string mca_competitoractivity = "mca_competitoractivity";
        public static readonly string mcabl_account_nn_systemuser = "mcabl_account_nn_systemuser";
        public static readonly string mcabl_area = "mcabl_area";
        public static readonly string mcabl_configurationlocationmanager = "mcabl_configurationlocationmanager";
        public static readonly string mcabl_contact_nn_account = "mcabl_contact_nn_account";
        public static readonly string mcabl_country = "mcabl_country";
        public static readonly string mcabl_county = "mcabl_county";
        public static readonly string mcabl_district = "mcabl_district";
        public static readonly string mcabl_location = "mcabl_location";
        public static readonly string mcabl_mcabl_nn_location_account = "mcabl_mcabl_nn_location_account";
        public static readonly string mcabl_opportunityhistory = "mcabl_opportunityhistory";
        public static readonly string mcabl_region = "mcabl_region";
        public static readonly string mcabl_state = "mcabl_state";
        public static readonly string mcabl_statusupdate = "mcabl_statusupdate";
        public static readonly string mcace_administrator = "mcace_administrator";
        public static readonly string mcace_configuration = "mcace_configuration";
        public static readonly string mcace_excludeattribute = "mcace_excludeattribute";
        public static readonly string mcace_log = "mcace_log";
        public static readonly string mcace_relationship = "mcace_relationship";
        public static readonly string mcace_relationshipitem = "mcace_relationshipitem";
        public static readonly string mcads_customconfiguration = "mcads_customconfiguration";
        public static readonly string mcads_entitymapping = "mcads_entitymapping";
        public static readonly string mcads_entitymappingattribute = "mcads_entitymappingattribute";
        public static readonly string mcads_entitymappingrelationship = "mcads_entitymappingrelationship";
        public static readonly string mcads_integrationlog = "mcads_integrationlog";
        public static readonly string mcamm_metadataattribute = "mcamm_metadataattribute";
        public static readonly string mcamm_metadataentity = "mcamm_metadataentity";
        public static readonly string mcamm_metadataform = "mcamm_metadataform";
        public static readonly string mcamm_metadataprocess = "mcamm_metadataprocess";
        public static readonly string mcamm_metadatarelationship = "mcamm_metadatarelationship";
        public static readonly string mcamm_metadatasyncjob = "mcamm_metadatasyncjob";
        public static readonly string mcamm_metadatasyncjobitem = "mcamm_metadatasyncjobitem";
        public static readonly string mcamm_metadataview = "mcamm_metadataview";
        public static readonly string metadatadifference = "metadatadifference";
        public static readonly string metric = "metric";
        public static readonly string mobileofflineprofile = "mobileofflineprofile";
        public static readonly string mobileofflineprofileitem = "mobileofflineprofileitem";
        public static readonly string mobileofflineprofileitemassociation = "mobileofflineprofileitemassociation";
        public static readonly string monthlyfiscalcalendar = "monthlyfiscalcalendar";
        public static readonly string msdyn_actioncardregarding = "msdyn_actioncardregarding";
        public static readonly string msdyn_actioncardrolesetting = "msdyn_actioncardrolesetting";
        public static readonly string msdyn_aibdataset = "msdyn_aibdataset";
        public static readonly string msdyn_aibdatasetfile = "msdyn_aibdatasetfile";
        public static readonly string msdyn_aibdatasetrecord = "msdyn_aibdatasetrecord";
        public static readonly string msdyn_aibdatasetscontainer = "msdyn_aibdatasetscontainer";
        public static readonly string msdyn_aibfile = "msdyn_aibfile";
        public static readonly string msdyn_aibfileattacheddata = "msdyn_aibfileattacheddata";
        public static readonly string msdyn_aiconfiguration = "msdyn_aiconfiguration";
        public static readonly string msdyn_aifptrainingdocument = "msdyn_aifptrainingdocument";
        public static readonly string msdyn_aimodel = "msdyn_aimodel";
        public static readonly string msdyn_aiodimage = "msdyn_aiodimage";
        public static readonly string msdyn_aiodlabel = "msdyn_aiodlabel";
        public static readonly string msdyn_aiodlabel_msdyn_aiconfiguration = "msdyn_aiodlabel_msdyn_aiconfiguration";
        public static readonly string msdyn_aiodtrainingboundingbox = "msdyn_aiodtrainingboundingbox";
        public static readonly string msdyn_aiodtrainingimage = "msdyn_aiodtrainingimage";
        public static readonly string msdyn_aitemplate = "msdyn_aitemplate";
        public static readonly string msdyn_analysiscomponent = "msdyn_analysiscomponent";
        public static readonly string msdyn_analysisjob = "msdyn_analysisjob";
        public static readonly string msdyn_analysisresult = "msdyn_analysisresult";
        public static readonly string msdyn_analysisresultdetail = "msdyn_analysisresultdetail";
        public static readonly string msdyn_analyticsadminsettings = "msdyn_analyticsadminsettings";
        public static readonly string msdyn_analyticsforcs = "msdyn_analyticsforcs";
        public static readonly string msdyn_answer = "msdyn_answer";
        public static readonly string msdyn_autocapturerule = "msdyn_autocapturerule";
        public static readonly string msdyn_autocapturesettings = "msdyn_autocapturesettings";
        public static readonly string msdyn_azuredeployment = "msdyn_azuredeployment";
        public static readonly string msdyn_callablecontext = "msdyn_callablecontext";
        public static readonly string msdyn_callablecontext_msdyn_playbooktemplate = "msdyn_callablecontext_msdyn_playbooktemplate";
        public static readonly string msdyn_collabgraphresource = "msdyn_collabgraphresource";
        public static readonly string msdyn_componentlayer = "msdyn_componentlayer";
        public static readonly string msdyn_componentlayerdatasource = "msdyn_componentlayerdatasource";
        public static readonly string msdyn_connector = "msdyn_connector";
        public static readonly string msdyn_customerasset = "msdyn_customerasset";
        public static readonly string msdyn_customerassetcategory = "msdyn_customerassetcategory";
        public static readonly string msdyn_dataanalyticsreport = "msdyn_dataanalyticsreport";
        public static readonly string msdyn_databaseversion = "msdyn_databaseversion";
        public static readonly string msdyn_dataflow = "msdyn_dataflow";
        public static readonly string msdyn_entityrankingrule = "msdyn_entityrankingrule";
        public static readonly string msdyn_feedbackmapping = "msdyn_feedbackmapping";
        public static readonly string msdyn_feedbacksubsurvey = "msdyn_feedbacksubsurvey";
        public static readonly string msdyn_feedbacksubsurvey_msdyn_survey = "msdyn_feedbacksubsurvey_msdyn_survey";
        public static readonly string msdyn_flowcardtype = "msdyn_flowcardtype";
        public static readonly string msdyn_forecastconfiguration = "msdyn_forecastconfiguration";
        public static readonly string msdyn_forecastdefinition = "msdyn_forecastdefinition";
        public static readonly string msdyn_forecastinstance = "msdyn_forecastinstance";
        public static readonly string msdyn_forecastrecurrence = "msdyn_forecastrecurrence";
        public static readonly string msdyn_helppage = "msdyn_helppage";
        public static readonly string msdyn_icebreakersconfig = "msdyn_icebreakersconfig";
        public static readonly string msdyn_image = "msdyn_image";
        public static readonly string msdyn_imagetokencache = "msdyn_imagetokencache";
        public static readonly string msdyn_import = "msdyn_import";
        public static readonly string msdyn_incident_msdyn_customerasset = "msdyn_incident_msdyn_customerasset";
        public static readonly string msdyn_iotalert = "msdyn_iotalert";
        public static readonly string msdyn_iotdevice = "msdyn_iotdevice";
        public static readonly string msdyn_iotdevicecategory = "msdyn_iotdevicecategory";
        public static readonly string msdyn_iotdevicecategorycommands = "msdyn_iotdevicecategorycommands";
        public static readonly string msdyn_iotdevicecommand = "msdyn_iotdevicecommand";
        public static readonly string msdyn_iotdevicecommanddefinition = "msdyn_iotdevicecommanddefinition";
        public static readonly string msdyn_iotdevicecommandparameters = "msdyn_iotdevicecommandparameters";
        public static readonly string msdyn_iotdevicedatahistory = "msdyn_iotdevicedatahistory";
        public static readonly string msdyn_iotdeviceproperty = "msdyn_iotdeviceproperty";
        public static readonly string msdyn_iotdeviceregistrationhistory = "msdyn_iotdeviceregistrationhistory";
        public static readonly string msdyn_iotdevicevisualizationconfiguration = "msdyn_iotdevicevisualizationconfiguration";
        public static readonly string msdyn_iotfieldmapping = "msdyn_iotfieldmapping";
        public static readonly string msdyn_iotpropertydefinition = "msdyn_iotpropertydefinition";
        public static readonly string msdyn_iotprovider = "msdyn_iotprovider";
        public static readonly string msdyn_iotproviderinstance = "msdyn_iotproviderinstance";
        public static readonly string msdyn_iotsettings = "msdyn_iotsettings";
        public static readonly string msdyn_iottocaseprocess = "msdyn_iottocaseprocess";
        public static readonly string msdyn_knowledgearticleimage = "msdyn_knowledgearticleimage";
        public static readonly string msdyn_knowledgearticletemplate = "msdyn_knowledgearticletemplate";
        public static readonly string msdyn_linkedanswer = "msdyn_linkedanswer";
        public static readonly string msdyn_msteamssetting = "msdyn_msteamssetting";
        public static readonly string msdyn_msteamssettingsv2 = "msdyn_msteamssettingsv2";
        public static readonly string msdyn_nonrelationalds = "msdyn_nonrelationalds";
        public static readonly string msdyn_notesanalysisconfig = "msdyn_notesanalysisconfig";
        public static readonly string msdyn_odatav4ds = "msdyn_odatav4ds";
        public static readonly string msdyn_page = "msdyn_page";
        public static readonly string msdyn_playbookactivity = "msdyn_playbookactivity";
        public static readonly string msdyn_playbookactivityattribute = "msdyn_playbookactivityattribute";
        public static readonly string msdyn_playbookcategory = "msdyn_playbookcategory";
        public static readonly string msdyn_playbookinstance = "msdyn_playbookinstance";
        public static readonly string msdyn_playbooktemplate = "msdyn_playbooktemplate";
        public static readonly string msdyn_postalbum = "msdyn_postalbum";
        public static readonly string msdyn_postconfig = "msdyn_postconfig";
        public static readonly string msdyn_postruleconfig = "msdyn_postruleconfig";
        public static readonly string msdyn_question = "msdyn_question";
        public static readonly string msdyn_question_answer_n_n = "msdyn_question_answer_n_n";
        public static readonly string msdyn_questiongroup = "msdyn_questiongroup";
        public static readonly string msdyn_questionresponse = "msdyn_questionresponse";
        public static readonly string msdyn_questiontype = "msdyn_questiontype";
        public static readonly string msdyn_relationshipinsightsunifiedconfig = "msdyn_relationshipinsightsunifiedconfig";
        public static readonly string msdyn_responseaction = "msdyn_responseaction";
        public static readonly string msdyn_responseblobstore = "msdyn_responseblobstore";
        public static readonly string msdyn_responsecondition = "msdyn_responsecondition";
        public static readonly string msdyn_responseerror = "msdyn_responseerror";
        public static readonly string msdyn_responseoutcome = "msdyn_responseoutcome";
        public static readonly string msdyn_responserouting = "msdyn_responserouting";
        public static readonly string msdyn_responserouting_msdyn_responseaction = "msdyn_responserouting_msdyn_responseaction";
        public static readonly string msdyn_responserouting_responseaction_else = "msdyn_responserouting_responseaction_else";
        public static readonly string msdyn_richtextfile = "msdyn_richtextfile";
        public static readonly string msdyn_salesinsightssettings = "msdyn_salesinsightssettings";
        public static readonly string msdyn_section = "msdyn_section";
        public static readonly string msdyn_serviceconfiguration = "msdyn_serviceconfiguration";
        public static readonly string msdyn_siconfig = "msdyn_siconfig";
        public static readonly string msdyn_sikeyvalueconfig = "msdyn_sikeyvalueconfig";
        public static readonly string msdyn_slakpi = "msdyn_slakpi";
        public static readonly string msdyn_solutioncomponentdatasource = "msdyn_solutioncomponentdatasource";
        public static readonly string msdyn_solutioncomponentsummary = "msdyn_solutioncomponentsummary";
        public static readonly string msdyn_solutionhealthrule = "msdyn_solutionhealthrule";
        public static readonly string msdyn_solutionhealthruleargument = "msdyn_solutionhealthruleargument";
        public static readonly string msdyn_solutionhealthruleset = "msdyn_solutionhealthruleset";
        public static readonly string msdyn_solutionhistory = "msdyn_solutionhistory";
        public static readonly string msdyn_solutionhistorydatasource = "msdyn_solutionhistorydatasource";
        public static readonly string msdyn_suggestedactivity = "msdyn_suggestedactivity";
        public static readonly string msdyn_suggestedactivitydatasource = "msdyn_suggestedactivitydatasource";
        public static readonly string msdyn_suggestedcontact = "msdyn_suggestedcontact";
        public static readonly string msdyn_suggestedcontactsdatasource = "msdyn_suggestedcontactsdatasource";
        public static readonly string msdyn_survey = "msdyn_survey";
        public static readonly string msdyn_surveyinvite = "msdyn_surveyinvite";
        public static readonly string msdyn_surveylog = "msdyn_surveylog";
        public static readonly string msdyn_surveyresponse = "msdyn_surveyresponse";
        public static readonly string msdyn_teamscollaboration = "msdyn_teamscollaboration";
        public static readonly string msdyn_theme = "msdyn_theme";
        public static readonly string msdyn_untrackedappointment = "msdyn_untrackedappointment";
        public static readonly string msdyn_upgraderun = "msdyn_upgraderun";
        public static readonly string msdyn_upgradestep = "msdyn_upgradestep";
        public static readonly string msdyn_upgradeversion = "msdyn_upgradeversion";
        public static readonly string msdyn_vocconfiguration = "msdyn_vocconfiguration";
        public static readonly string msdyn_wallsavedquery = "msdyn_wallsavedquery";
        public static readonly string msdyn_wallsavedqueryusersettings = "msdyn_wallsavedqueryusersettings";
        public static readonly string msfp_emailtemplate = "msfp_emailtemplate";
        public static readonly string msfp_localizedemailtemplate = "msfp_localizedemailtemplate";
        public static readonly string msfp_project = "msfp_project";
        public static readonly string msfp_question = "msfp_question";
        public static readonly string msfp_questionresponse = "msfp_questionresponse";
        public static readonly string msfp_satisfactionmetric = "msfp_satisfactionmetric";
        public static readonly string msfp_survey = "msfp_survey";
        public static readonly string msfp_surveyinvite = "msfp_surveyinvite";
        public static readonly string msfp_surveyresponse = "msfp_surveyresponse";
        public static readonly string msfp_unsubscribedrecipient = "msfp_unsubscribedrecipient";
        public static readonly string multientitysearch = "multientitysearch";
        public static readonly string multientitysearchentities = "multientitysearchentities";
        public static readonly string multiselectattributeoptionvalues = "multiselectattributeoptionvalues";
        public static readonly string navigationsetting = "navigationsetting";
        public static readonly string new_approvalrequest = "new_approvalrequest";
        public static readonly string new_bpf_028a0c7f7f464d5ea3c5715953a64408 = "new_bpf_028a0c7f7f464d5ea3c5715953a64408";
        public static readonly string new_bpf_47b59f41f76f43fc9359bfeab7a56bb6 = "new_bpf_47b59f41f76f43fc9359bfeab7a56bb6";
        public static readonly string new_bpf_4e2bd70ab4844d2dab60b193381618f9 = "new_bpf_4e2bd70ab4844d2dab60b193381618f9";
        public static readonly string new_bpf_645b544c0db24871b39e7194f2fb5acb = "new_bpf_645b544c0db24871b39e7194f2fb5acb";
        public static readonly string new_bpf_caadda59e35b46b4b3eb23860f89c5c7 = "new_bpf_caadda59e35b46b4b3eb23860f89c5c7";
        public static readonly string newprocess = "newprocess";
        public static readonly string notification = "notification";
        public static readonly string nspi_businessstream = "nspi_businessstream";
        public static readonly string nspi_commissiondistribution = "nspi_commissiondistribution";
        public static readonly string nspi_commissionsdue = "nspi_commissionsdue";
        public static readonly string nspi_customercontract = "nspi_customercontract";
        public static readonly string nspi_customercontractprice = "nspi_customercontractprice";
        public static readonly string nspi_deliveryterm = "nspi_deliveryterm";
        public static readonly string nspi_incident_nspi_username = "nspi_incident_nspi_username";
        public static readonly string nspi_language = "nspi_language";
        public static readonly string nspi_nspi_project_account = "nspi_nspi_project_account";
        public static readonly string nspi_nspi_project_contact = "nspi_nspi_project_contact";
        public static readonly string nspi_nspi_project_systemuser = "nspi_nspi_project_systemuser";
        public static readonly string nspi_nspicustomsettings = "nspi_nspicustomsettings";
        public static readonly string nspi_orderblanketline = "nspi_orderblanketline";
        public static readonly string nspi_productcode = "nspi_productcode";
        public static readonly string nspi_project = "nspi_project";
        public static readonly string nspi_rmaline = "nspi_rmaline";
        public static readonly string nspi_salesperson = "nspi_salesperson";
        public static readonly string nspi_shippingmethod = "nspi_shippingmethod";
        public static readonly string nspi_taxcode = "nspi_taxcode";
        public static readonly string nspi_termscode = "nspi_termscode";
        public static readonly string nspi_username = "nspi_username";
        public static readonly string nspi_verticalmarket = "nspi_verticalmarket";
        public static readonly string officedocument = "officedocument";
        public static readonly string officegraphdocument = "officegraphdocument";
        public static readonly string offlinecommanddefinition = "offlinecommanddefinition";
        public static readonly string opportunity = "opportunity";
        public static readonly string opportunityclose = "opportunityclose";
        public static readonly string opportunitycompetitors = "opportunitycompetitors";
        public static readonly string opportunityproduct = "opportunityproduct";
        public static readonly string opportunitysalesprocess = "opportunitysalesprocess";
        public static readonly string optionset = "optionset";
        public static readonly string orderclose = "orderclose";
        public static readonly string organization = "organization";
        public static readonly string organizationstatistic = "organizationstatistic";
        public static readonly string organizationui = "organizationui";
        public static readonly string orginsightsmetric = "orginsightsmetric";
        public static readonly string orginsightsnotification = "orginsightsnotification";
        public static readonly string owner = "owner";
        public static readonly string ownermapping = "ownermapping";
        public static readonly string partnerapplication = "partnerapplication";
        public static readonly string pdfsetting = "pdfsetting";
        public static readonly string personaldocumenttemplate = "personaldocumenttemplate";
        public static readonly string phonecall = "phonecall";
        public static readonly string phonetocaseprocess = "phonetocaseprocess";
        public static readonly string picklistmapping = "picklistmapping";
        public static readonly string pluginassembly = "pluginassembly";
        public static readonly string plugintracelog = "plugintracelog";
        public static readonly string plugintype = "plugintype";
        public static readonly string plugintypestatistic = "plugintypestatistic";
        public static readonly string position = "position";
        public static readonly string post = "post";
        public static readonly string postcomment = "postcomment";
        public static readonly string postfollow = "postfollow";
        public static readonly string postlike = "postlike";
        public static readonly string postregarding = "postregarding";
        public static readonly string postrole = "postrole";
        public static readonly string pricelevel = "pricelevel";
        public static readonly string principalattributeaccessmap = "principalattributeaccessmap";
        public static readonly string principalentitymap = "principalentitymap";
        public static readonly string principalobjectaccess = "principalobjectaccess";
        public static readonly string principalobjectaccessreadsnapshot = "principalobjectaccessreadsnapshot";
        public static readonly string principalobjectattributeaccess = "principalobjectattributeaccess";
        public static readonly string principalsyncattributemap = "principalsyncattributemap";
        public static readonly string privilege = "privilege";
        public static readonly string privilegeobjecttypecodes = "privilegeobjecttypecodes";
        public static readonly string processsession = "processsession";
        public static readonly string processstage = "processstage";
        public static readonly string processstageparameter = "processstageparameter";
        public static readonly string processtrigger = "processtrigger";
        public static readonly string product = "product";
        public static readonly string productassociation = "productassociation";
        public static readonly string productpricelevel = "productpricelevel";
        public static readonly string productsalesliterature = "productsalesliterature";
        public static readonly string productsubstitute = "productsubstitute";
        public static readonly string ptm_automergeworkingitems = "ptm_automergeworkingitems";
        public static readonly string ptm_dcpspssiteconfig2011 = "ptm_dcpspssiteconfig2011";
        public static readonly string ptm_mscrmaddons_dcptemplates = "ptm_mscrmaddons_dcptemplates";
        public static readonly string ptm_mscrmaddons_key = "ptm_mscrmaddons_key";
        public static readonly string ptm_mscrmaddons_scheduler = "ptm_mscrmaddons_scheduler";
        public static readonly string ptm_mscrmaddons_settings = "ptm_mscrmaddons_settings";
        public static readonly string ptm_mscrmaddonscom_debug = "ptm_mscrmaddonscom_debug";
        public static readonly string ptm_mscrmaddonscomamtrigger = "ptm_mscrmaddonscomamtrigger";
        public static readonly string ptm_mscrmaddonscommetadata = "ptm_mscrmaddonscommetadata";
        public static readonly string ptm_mscrmaddonstemp = "ptm_mscrmaddonstemp";
        public static readonly string publisher = "publisher";
        public static readonly string publisheraddress = "publisheraddress";
        public static readonly string quarterlyfiscalcalendar = "quarterlyfiscalcalendar";
        public static readonly string queue = "queue";
        public static readonly string queueitem = "queueitem";
        public static readonly string queueitemcount = "queueitemcount";
        public static readonly string queuemembercount = "queuemembercount";
        public static readonly string queuemembership = "queuemembership";
        public static readonly string quote = "quote";
        public static readonly string quoteclose = "quoteclose";
        public static readonly string quotedetail = "quotedetail";
        public static readonly string ratingmodel = "ratingmodel";
        public static readonly string ratingvalue = "ratingvalue";
        public static readonly string recommendeddocument = "recommendeddocument";
        public static readonly string recordcountsnapshot = "recordcountsnapshot";
        public static readonly string recurrencerule = "recurrencerule";
        public static readonly string recurringappointmentmaster = "recurringappointmentmaster";
        public static readonly string relationship = "relationship";
        public static readonly string relationshipattribute = "relationshipattribute";
        public static readonly string relationshiprole = "relationshiprole";
        public static readonly string relationshiprolemap = "relationshiprolemap";
        public static readonly string replicationbacklog = "replicationbacklog";
        public static readonly string report = "report";
        public static readonly string reportcategory = "reportcategory";
        public static readonly string reportentity = "reportentity";
        public static readonly string reportlink = "reportlink";
        public static readonly string reportvisibility = "reportvisibility";
        public static readonly string resource = "resource";
        public static readonly string resourcegroup = "resourcegroup";
        public static readonly string resourcegroupexpansion = "resourcegroupexpansion";
        public static readonly string resourcespec = "resourcespec";
        public static readonly string ribbonclientmetadata = "ribbonclientmetadata";
        public static readonly string ribboncommand = "ribboncommand";
        public static readonly string ribboncontextgroup = "ribboncontextgroup";
        public static readonly string ribboncustomization = "ribboncustomization";
        public static readonly string ribbondiff = "ribbondiff";
        public static readonly string ribbonmetadatatoprocess = "ribbonmetadatatoprocess";
        public static readonly string ribbonrule = "ribbonrule";
        public static readonly string ribbontabtocommandmap = "ribbontabtocommandmap";
        public static readonly string role = "role";
        public static readonly string roleprivileges = "roleprivileges";
        public static readonly string roletemplate = "roletemplate";
        public static readonly string roletemplateprivileges = "roletemplateprivileges";
        public static readonly string rollupfield = "rollupfield";
        public static readonly string rollupjob = "rollupjob";
        public static readonly string rollupproperties = "rollupproperties";
        public static readonly string routingrule = "routingrule";
        public static readonly string routingruleitem = "routingruleitem";
        public static readonly string runtimedependency = "runtimedependency";
        public static readonly string sales_linkedin_configuration = "sales_linkedin_configuration";
        public static readonly string sales_linkedin_profileassociations = "sales_linkedin_profileassociations";
        public static readonly string salesliterature = "salesliterature";
        public static readonly string salesliteratureitem = "salesliteratureitem";
        public static readonly string salesorder = "salesorder";
        public static readonly string salesorderdetail = "salesorderdetail";
        public static readonly string salesprocessinstance = "salesprocessinstance";
        public static readonly string savedorginsightsconfiguration = "savedorginsightsconfiguration";
        public static readonly string savedquery = "savedquery";
        public static readonly string savedqueryvisualization = "savedqueryvisualization";
        public static readonly string sdkmessage = "sdkmessage";
        public static readonly string sdkmessagefilter = "sdkmessagefilter";
        public static readonly string sdkmessagepair = "sdkmessagepair";
        public static readonly string sdkmessageprocessingstep = "sdkmessageprocessingstep";
        public static readonly string sdkmessageprocessingstepimage = "sdkmessageprocessingstepimage";
        public static readonly string sdkmessageprocessingstepsecureconfig = "sdkmessageprocessingstepsecureconfig";
        public static readonly string sdkmessagerequest = "sdkmessagerequest";
        public static readonly string sdkmessagerequestfield = "sdkmessagerequestfield";
        public static readonly string sdkmessageresponse = "sdkmessageresponse";
        public static readonly string sdkmessageresponsefield = "sdkmessageresponsefield";
        public static readonly string semiannualfiscalcalendar = "semiannualfiscalcalendar";
        public static readonly string service = "service";
        public static readonly string serviceappointment = "serviceappointment";
        public static readonly string servicecontractcontacts = "servicecontractcontacts";
        public static readonly string serviceendpoint = "serviceendpoint";
        public static readonly string serviceplan = "serviceplan";
        public static readonly string serviceplanappmodules = "serviceplanappmodules";
        public static readonly string settingdefinition = "settingdefinition";
        public static readonly string sharedobjectsforread = "sharedobjectsforread";
        public static readonly string sharepointdata = "sharepointdata";
        public static readonly string sharepointdocument = "sharepointdocument";
        public static readonly string sharepointdocumentlocation = "sharepointdocumentlocation";
        public static readonly string sharepointsite = "sharepointsite";
        public static readonly string similarityrule = "similarityrule";
        public static readonly string site = "site";
        public static readonly string sitemap = "sitemap";
        public static readonly string sla = "sla";
        public static readonly string slaitem = "slaitem";
        public static readonly string slakpiinstance = "slakpiinstance";
        public static readonly string smxan_autonumberconfiguration = "smxan_autonumberconfiguration";
        public static readonly string socialactivity = "socialactivity";
        public static readonly string socialinsightsconfiguration = "socialinsightsconfiguration";
        public static readonly string socialprofile = "socialprofile";
        public static readonly string solution = "solution";
        public static readonly string solutioncomponent = "solutioncomponent";
        public static readonly string solutioncomponentattributeconfiguration = "solutioncomponentattributeconfiguration";
        public static readonly string solutioncomponentconfiguration = "solutioncomponentconfiguration";
        public static readonly string solutioncomponentdefinition = "solutioncomponentdefinition";
        public static readonly string solutioncomponentrelationshipconfiguration = "solutioncomponentrelationshipconfiguration";
        public static readonly string solutionhistorydata = "solutionhistorydata";
        public static readonly string sqlencryptionaudit = "sqlencryptionaudit";
        public static readonly string stagesolutionupload = "stagesolutionupload";
        public static readonly string statusmap = "statusmap";
        public static readonly string stringmap = "stringmap";
        public static readonly string subject = "subject";
        public static readonly string subscription = "subscription";
        public static readonly string subscriptionclients = "subscriptionclients";
        public static readonly string subscriptionmanuallytrackedobject = "subscriptionmanuallytrackedobject";
        public static readonly string subscriptionstatisticsoffline = "subscriptionstatisticsoffline";
        public static readonly string subscriptionstatisticsoutlook = "subscriptionstatisticsoutlook";
        public static readonly string subscriptionsyncentryoffline = "subscriptionsyncentryoffline";
        public static readonly string subscriptionsyncentryoutlook = "subscriptionsyncentryoutlook";
        public static readonly string subscriptionsyncinfo = "subscriptionsyncinfo";
        public static readonly string subscriptiontrackingdeletedobject = "subscriptiontrackingdeletedobject";
        public static readonly string suggestioncardtemplate = "suggestioncardtemplate";
        public static readonly string syncattributemapping = "syncattributemapping";
        public static readonly string syncattributemappingprofile = "syncattributemappingprofile";
        public static readonly string syncerror = "syncerror";
        public static readonly string systemapplicationmetadata = "systemapplicationmetadata";
        public static readonly string systemform = "systemform";
        public static readonly string systemuser = "systemuser";
        public static readonly string systemuserbusinessunitentitymap = "systemuserbusinessunitentitymap";
        public static readonly string systemuserlicenses = "systemuserlicenses";
        public static readonly string systemusermanagermap = "systemusermanagermap";
        public static readonly string systemuserprincipals = "systemuserprincipals";
        public static readonly string systemuserprofiles = "systemuserprofiles";
        public static readonly string systemuserroles = "systemuserroles";
        public static readonly string systemusersyncmappingprofiles = "systemusersyncmappingprofiles";
        public static readonly string task = "task";
        public static readonly string team = "team";
        public static readonly string teammembership = "teammembership";
        public static readonly string teamprofiles = "teamprofiles";
        public static readonly string teamroles = "teamroles";
        public static readonly string teamsyncattributemappingprofiles = "teamsyncattributemappingprofiles";
        public static readonly string teamtemplate = "teamtemplate";
        public static readonly string template = "template";
        public static readonly string territory = "territory";
        public static readonly string textanalyticsentitymapping = "textanalyticsentitymapping";
        public static readonly string theme = "theme";
        public static readonly string timestampdatemapping = "timestampdatemapping";
        public static readonly string timezonedefinition = "timezonedefinition";
        public static readonly string timezonelocalizedname = "timezonelocalizedname";
        public static readonly string timezonerule = "timezonerule";
        public static readonly string topic = "topic";
        public static readonly string topichistory = "topichistory";
        public static readonly string topicmodel = "topicmodel";
        public static readonly string topicmodelconfiguration = "topicmodelconfiguration";
        public static readonly string topicmodelexecutionhistory = "topicmodelexecutionhistory";
        public static readonly string traceassociation = "traceassociation";
        public static readonly string tracelog = "tracelog";
        public static readonly string traceregarding = "traceregarding";
        public static readonly string transactioncurrency = "transactioncurrency";
        public static readonly string transformationmapping = "transformationmapping";
        public static readonly string transformationparametermapping = "transformationparametermapping";
        public static readonly string translationprocess = "translationprocess";
        public static readonly string unresolvedaddress = "unresolvedaddress";
        public static readonly string untrackedemail = "untrackedemail";
        public static readonly string uom = "uom";
        public static readonly string uomschedule = "uomschedule";
        public static readonly string userapplicationmetadata = "userapplicationmetadata";
        public static readonly string userentityinstancedata = "userentityinstancedata";
        public static readonly string userentityuisettings = "userentityuisettings";
        public static readonly string userfiscalcalendar = "userfiscalcalendar";
        public static readonly string userform = "userform";
        public static readonly string usermapping = "usermapping";
        public static readonly string userquery = "userquery";
        public static readonly string userqueryvisualization = "userqueryvisualization";
        public static readonly string usersearchfacet = "usersearchfacet";
        public static readonly string usersettings = "usersettings";
        public static readonly string webresource = "webresource";
        public static readonly string webwizard = "webwizard";
        public static readonly string wizardaccessprivilege = "wizardaccessprivilege";
        public static readonly string wizardpage = "wizardpage";
        public static readonly string workflow = "workflow";
        public static readonly string workflowbinary = "workflowbinary";
        public static readonly string workflowdependency = "workflowdependency";
        public static readonly string workflowlog = "workflowlog";
        public static readonly string workflowwaitsubscription = "workflowwaitsubscription";

    }
    public struct MessageNames
    {
        public static readonly string _AddMembersBatch = "_AddMembersBatch";
        public static readonly string _EvaluateRuleAndRoute = "_EvaluateRuleAndRoute";
        public static readonly string AddAppComponents = "AddAppComponents";
        public static readonly string AddChannelAccessProfilePrivileges = "AddChannelAccessProfilePrivileges";
        public static readonly string AddItem = "AddItem";
        public static readonly string AddListMembers = "AddListMembers";
        public static readonly string AddMember = "AddMember";
        public static readonly string AddMembers = "AddMembers";
        public static readonly string AddMembersBatch = "AddMembersBatch";
        public static readonly string AddPrincipalToQueue = "AddPrincipalToQueue";
        public static readonly string AddPrivileges = "AddPrivileges";
        public static readonly string AddProductToKit = "AddProductToKit";
        public static readonly string AddRecurrence = "AddRecurrence";
        public static readonly string AddSolutionComponent = "AddSolutionComponent";
        public static readonly string AddSubstitute = "AddSubstitute";
        public static readonly string AddToQueue = "AddToQueue";
        public static readonly string AddUserToRecordTeam = "AddUserToRecordTeam";
        public static readonly string adx_AzureBlobStorageEnabled = "adx_AzureBlobStorageEnabled";
        public static readonly string adx_AzureBlobStorageUrl = "adx_AzureBlobStorageUrl";
        public static readonly string adx_GenerateSharedAccessSignature = "adx_GenerateSharedAccessSignature";
        public static readonly string adx_SendEmailConfirmationToContact = "adx_SendEmailConfirmationToContact";
        public static readonly string adx_SendEmailTwoFactorCodeToContact = "adx_SendEmailTwoFactorCodeToContact";
        public static readonly string adx_SendPasswordResetToContact = "adx_SendPasswordResetToContact";
        public static readonly string adx_WebNotificationsBuildConfigurationXML = "adx_WebNotificationsBuildConfigurationXML";
        public static readonly string AlmHandler = "AlmHandler";
        public static readonly string AnalyzeSentiment = "AnalyzeSentiment";
        public static readonly string ApplyRecordCreationAndUpdateRule = "ApplyRecordCreationAndUpdateRule";
        public static readonly string ApplyRoutingRule = "ApplyRoutingRule";
        public static readonly string Assign = "Assign";
        public static readonly string AssignUserRoles = "AssignUserRoles";
        public static readonly string Associate = "Associate";
        public static readonly string AssociateEntities = "AssociateEntities";
        public static readonly string AutoMapEntity = "AutoMapEntity";
        public static readonly string BackgroundSend = "BackgroundSend";
        public static readonly string BatchPrediction = "BatchPrediction";
        public static readonly string Book = "Book";
        public static readonly string BulkDelete = "BulkDelete";
        public static readonly string BulkDelete2 = "BulkDelete2";
        public static readonly string BulkDetectDuplicates = "BulkDetectDuplicates";
        public static readonly string BulkMail = "BulkMail";
        public static readonly string CalculateActualValue = "CalculateActualValue";
        public static readonly string CalculatePrice = "CalculatePrice";
        public static readonly string CalculateRollupField = "CalculateRollupField";
        public static readonly string CalculateTotalTime = "CalculateTotalTime";
        public static readonly string CanBeReferenced = "CanBeReferenced";
        public static readonly string CanBeReferencing = "CanBeReferencing";
        public static readonly string Cancel = "Cancel";
        public static readonly string CancelTraining = "CancelTraining";
        public static readonly string CanManyToMany = "CanManyToMany";
        public static readonly string CategorizeText = "CategorizeText";
        public static readonly string CheckIncoming = "CheckIncoming";
        public static readonly string CheckPromote = "CheckPromote";
        public static readonly string Clone = "Clone";
        public static readonly string CloneAsPatch = "CloneAsPatch";
        public static readonly string CloneAsSolution = "CloneAsSolution";
        public static readonly string CloneMobileOfflineProfile = "CloneMobileOfflineProfile";
        public static readonly string CloneProduct = "CloneProduct";
        public static readonly string Close = "Close";
        public static readonly string CommitAnnotationBlocksUpload = "CommitAnnotationBlocksUpload";
        public static readonly string CommitAttachmentBlocksUpload = "CommitAttachmentBlocksUpload";
        public static readonly string CommitFileBlocksUpload = "CommitFileBlocksUpload";
        public static readonly string CompoundCreate = "CompoundCreate";
        public static readonly string CompoundUpdate = "CompoundUpdate";
        public static readonly string CompoundUpdateDuplicateDetectionRule = "CompoundUpdateDuplicateDetectionRule";
        public static readonly string ConvertDateAndTimeBehavior = "ConvertDateAndTimeBehavior";
        public static readonly string ConvertKitToProduct = "ConvertKitToProduct";
        public static readonly string ConvertOwnerTeamToAccessTeam = "ConvertOwnerTeamToAccessTeam";
        public static readonly string ConvertProductToKit = "ConvertProductToKit";
        public static readonly string ConvertQuoteToSalesOrder = "ConvertQuoteToSalesOrder";
        public static readonly string ConvertSalesOrderToInvoice = "ConvertSalesOrderToInvoice";
        public static readonly string Copy = "Copy";
        public static readonly string CopyCampaignResponse = "CopyCampaignResponse";
        public static readonly string CopyDynamicListToStatic = "CopyDynamicListToStatic";
        public static readonly string CopyMembers = "CopyMembers";
        public static readonly string CopySystemForm = "CopySystemForm";
        public static readonly string Create = "Create";
        public static readonly string CreateActivities = "CreateActivities";
        public static readonly string CreateAsyncJobToRevokeInheritedAccess = "CreateAsyncJobToRevokeInheritedAccess";
        public static readonly string CreateAttribute = "CreateAttribute";
        public static readonly string CreateCustomerRelationships = "CreateCustomerRelationships";
        public static readonly string CreateEntity = "CreateEntity";
        public static readonly string CreateEntityKey = "CreateEntityKey";
        public static readonly string CreateException = "CreateException";
        public static readonly string CreateInstance = "CreateInstance";
        public static readonly string CreateKnowledgeArticleTranslation = "CreateKnowledgeArticleTranslation";
        public static readonly string CreateKnowledgeArticleVersion = "CreateKnowledgeArticleVersion";
        public static readonly string CreateManyToMany = "CreateManyToMany";
        public static readonly string CreateOneToMany = "CreateOneToMany";
        public static readonly string CreateOptionSet = "CreateOptionSet";
        public static readonly string CreateWorkflowFromTemplate = "CreateWorkflowFromTemplate";
        public static readonly string Delete = "Delete";
        public static readonly string DeleteAndPromote = "DeleteAndPromote";
        public static readonly string DeleteAttribute = "DeleteAttribute";
        public static readonly string DeleteAuditData = "DeleteAuditData";
        public static readonly string DeleteEntity = "DeleteEntity";
        public static readonly string DeleteEntityKey = "DeleteEntityKey";
        public static readonly string DeleteFile = "DeleteFile";
        public static readonly string DeleteOpenInstances = "DeleteOpenInstances";
        public static readonly string DeleteOptionSet = "DeleteOptionSet";
        public static readonly string DeleteOptionValue = "DeleteOptionValue";
        public static readonly string DeleteOQOILineWithSkipPricingCalculation = "DeleteOQOILineWithSkipPricingCalculation";
        public static readonly string DeleteRecordChangeHistory = "DeleteRecordChangeHistory";
        public static readonly string DeleteRelationship = "DeleteRelationship";
        public static readonly string DeliverImmediatePromote = "DeliverImmediatePromote";
        public static readonly string DeliverIncoming = "DeliverIncoming";
        public static readonly string DeliverPromote = "DeliverPromote";
        public static readonly string DeprovisionLanguage = "DeprovisionLanguage";
        public static readonly string DetachFromQueue = "DetachFromQueue";
        public static readonly string DetectLanguage = "DetectLanguage";
        public static readonly string Disassociate = "Disassociate";
        public static readonly string DisassociateEntities = "DisassociateEntities";
        public static readonly string DistributeCampaignActivity = "DistributeCampaignActivity";
        public static readonly string DownloadBlock = "DownloadBlock";
        public static readonly string DownloadReportDefinition = "DownloadReportDefinition";
        public static readonly string DownloadSolutionExportData = "DownloadSolutionExportData";
        public static readonly string EntityExpressionToFetchXml = "EntityExpressionToFetchXml";
        public static readonly string Execute = "Execute";
        public static readonly string ExecuteAsync = "ExecuteAsync";
        public static readonly string ExecuteById = "ExecuteById";
        public static readonly string ExecuteMultiple = "ExecuteMultiple";
        public static readonly string ExecuteTransaction = "ExecuteTransaction";
        public static readonly string ExecuteWorkflow = "ExecuteWorkflow";
        public static readonly string Expand = "Expand";
        public static readonly string Export = "Export";
        public static readonly string ExportAll = "ExportAll";
        public static readonly string ExportCompressed = "ExportCompressed";
        public static readonly string ExportCompressedAll = "ExportCompressedAll";
        public static readonly string ExportCompressedTranslations = "ExportCompressedTranslations";
        public static readonly string ExportFieldTranslation = "ExportFieldTranslation";
        public static readonly string ExportMappings = "ExportMappings";
        public static readonly string ExportSolution = "ExportSolution";
        public static readonly string ExportSolutionAsync = "ExportSolutionAsync";
        public static readonly string ExportTranslation = "ExportTranslation";
        public static readonly string ExportTranslations = "ExportTranslations";
        public static readonly string ExtractKeyPhrases = "ExtractKeyPhrases";
        public static readonly string ExtractTextEntities = "ExtractTextEntities";
        public static readonly string FetchXmlToEntityExpression = "FetchXmlToEntityExpression";
        public static readonly string FindParent = "FindParent";
        public static readonly string FormatAddress = "FormatAddress";
        public static readonly string Fulfill = "Fulfill";
        public static readonly string FullTextSearchKnowledgeArticle = "FullTextSearchKnowledgeArticle";
        public static readonly string GenerateInvoiceFromOpportunity = "GenerateInvoiceFromOpportunity";
        public static readonly string GenerateQuoteFromOpportunity = "GenerateQuoteFromOpportunity";
        public static readonly string GenerateSalesOrderFromOpportunity = "GenerateSalesOrderFromOpportunity";
        public static readonly string GenerateSocialProfile = "GenerateSocialProfile";
        public static readonly string GetAllTimeZonesWithDisplayName = "GetAllTimeZonesWithDisplayName";
        public static readonly string GetAutoNumberSeed = "GetAutoNumberSeed";
        public static readonly string GetDecryptionKey = "GetDecryptionKey";
        public static readonly string GetDefaultPriceLevel = "GetDefaultPriceLevel";
        public static readonly string GetDistinctValues = "GetDistinctValues";
        public static readonly string GetHeaderColumns = "GetHeaderColumns";
        public static readonly string GetInvoiceProductsFromOpportunity = "GetInvoiceProductsFromOpportunity";
        public static readonly string GetJobStatus = "GetJobStatus";
        public static readonly string GetNextAutoNumberValue = "GetNextAutoNumberValue";
        public static readonly string GetQuantityDecimal = "GetQuantityDecimal";
        public static readonly string GetQuoteProductsFromOpportunity = "GetQuoteProductsFromOpportunity";
        public static readonly string GetReportHistoryLimit = "GetReportHistoryLimit";
        public static readonly string GetSalesOrderProductsFromOpportunity = "GetSalesOrderProductsFromOpportunity";
        public static readonly string GetTimeZoneCodeByLocalizedName = "GetTimeZoneCodeByLocalizedName";
        public static readonly string GetTrackingToken = "GetTrackingToken";
        public static readonly string GetValidManyToMany = "GetValidManyToMany";
        public static readonly string GetValidReferencedEntities = "GetValidReferencedEntities";
        public static readonly string GetValidReferencingEntities = "GetValidReferencingEntities";
        public static readonly string GrantAccess = "GrantAccess";
        public static readonly string Handle = "Handle";
        public static readonly string ImmediateBook = "ImmediateBook";
        public static readonly string Import = "Import";
        public static readonly string ImportAll = "ImportAll";
        public static readonly string ImportCardTypeSchema = "ImportCardTypeSchema";
        public static readonly string ImportCompressedAll = "ImportCompressedAll";
        public static readonly string ImportCompressedTranslationsWithProgress = "ImportCompressedTranslationsWithProgress";
        public static readonly string ImportCompressedWithProgress = "ImportCompressedWithProgress";
        public static readonly string ImportFieldTranslation = "ImportFieldTranslation";
        public static readonly string ImportMappings = "ImportMappings";
        public static readonly string ImportRecords = "ImportRecords";
        public static readonly string ImportSolution = "ImportSolution";
        public static readonly string ImportSolutions = "ImportSolutions";
        public static readonly string ImportTranslation = "ImportTranslation";
        public static readonly string ImportTranslationsWithProgress = "ImportTranslationsWithProgress";
        public static readonly string ImportWithProgress = "ImportWithProgress";
        public static readonly string IncrementKnowledgeArticleViewCount = "IncrementKnowledgeArticleViewCount";
        public static readonly string InitializeAnnotationBlocksDownload = "InitializeAnnotationBlocksDownload";
        public static readonly string InitializeAnnotationBlocksUpload = "InitializeAnnotationBlocksUpload";
        public static readonly string InitializeAttachmentBlocksDownload = "InitializeAttachmentBlocksDownload";
        public static readonly string InitializeAttachmentBlocksUpload = "InitializeAttachmentBlocksUpload";
        public static readonly string InitializeFileBlocksDownload = "InitializeFileBlocksDownload";
        public static readonly string InitializeFileBlocksUpload = "InitializeFileBlocksUpload";
        public static readonly string InitializeFrom = "InitializeFrom";
        public static readonly string InitializeModernFlowFromAsyncWorkflow = "InitializeModernFlowFromAsyncWorkflow";
        public static readonly string InsertOptionValue = "InsertOptionValue";
        public static readonly string InsertStatusValue = "InsertStatusValue";
        public static readonly string InstallSampleData = "InstallSampleData";
        public static readonly string Instantiate = "Instantiate";
        public static readonly string InstantiateFilters = "InstantiateFilters";
        public static readonly string IsBackOfficeInstalled = "IsBackOfficeInstalled";
        public static readonly string IsComponentCustomizable = "IsComponentCustomizable";
        public static readonly string IsDataEncryptionActive = "IsDataEncryptionActive";
        public static readonly string IsPaiEnabled = "IsPaiEnabled";
        public static readonly string IsValidStateTransition = "IsValidStateTransition";
        public static readonly string LocalTimeFromUtcTime = "LocalTimeFromUtcTime";
        public static readonly string LockInvoicePricing = "LockInvoicePricing";
        public static readonly string LockSalesOrderPricing = "LockSalesOrderPricing";
        public static readonly string Lose = "Lose";
        public static readonly string MakeAvailableToOrganization = "MakeAvailableToOrganization";
        public static readonly string MakeUnavailableToOrganization = "MakeUnavailableToOrganization";
        public static readonly string mca_QualifyLeadToQuoteAction = "mca_QualifyLeadToQuoteAction";
        public static readonly string mcace_EntityCopier = "mcace_EntityCopier";
        public static readonly string mcace_GlobalValidationProcess = "mcace_GlobalValidationProcess";
        public static readonly string mcace_LoadDefaultExcludeAttribute = "mcace_LoadDefaultExcludeAttribute";
        public static readonly string mcads_CalculatePrice = "mcads_CalculatePrice";
        public static readonly string mcads_CreateSAPAccount = "mcads_CreateSAPAccount";
        public static readonly string mcads_CreateSAPContact = "mcads_CreateSAPContact";
        public static readonly string mcads_CreateSAPQuote = "mcads_CreateSAPQuote";
        public static readonly string mcads_CreateSAPSalesOrder = "mcads_CreateSAPSalesOrder";
        public static readonly string mcads_ReteriveSAPPricing = "mcads_ReteriveSAPPricing";
        public static readonly string mcamm_CreateMetadataEntity = "mcamm_CreateMetadataEntity";
        public static readonly string mcamm_PublishMetadataEntity = "mcamm_PublishMetadataEntity";
        public static readonly string Merge = "Merge";
        public static readonly string ModifyAccess = "ModifyAccess";
        public static readonly string msdyn_ActivateProcesses = "msdyn_ActivateProcesses";
        public static readonly string msdyn_ActivateSdkMessageProcessingSteps = "msdyn_ActivateSdkMessageProcessingSteps";
        public static readonly string msdyn_AddSuggestedCards = "msdyn_AddSuggestedCards";
        public static readonly string msdyn_AnalyticsSaveDataInConfigStore = "msdyn_AnalyticsSaveDataInConfigStore";
        public static readonly string msdyn_cesinvoke = "msdyn_cesinvoke";
        public static readonly string msdyn_CheckAnyUserIsIntegrationUser = "msdyn_CheckAnyUserIsIntegrationUser";
        public static readonly string msdyn_CheckFetchXmlAndLayoutXmlOfSavedQueries = "msdyn_CheckFetchXmlAndLayoutXmlOfSavedQueries";
        public static readonly string msdyn_CheckForCustomizedOptionSet = "msdyn_CheckForCustomizedOptionSet";
        public static readonly string msdyn_CheckForCustomizedSitemap = "msdyn_CheckForCustomizedSitemap";
        public static readonly string msdyn_CheckForCustomizedWebResources = "msdyn_CheckForCustomizedWebResources";
        public static readonly string msdyn_CheckForDeletedProcess = "msdyn_CheckForDeletedProcess";
        public static readonly string msdyn_CheckForDeletedSDKMessageProcessingSteps = "msdyn_CheckForDeletedSDKMessageProcessingSteps";
        public static readonly string msdyn_CheckForDeletedWebResources = "msdyn_CheckForDeletedWebResources";
        public static readonly string msdyn_CheckForPendingProcesses = "msdyn_CheckForPendingProcesses";
        public static readonly string msdyn_CheckIfProcessesAreActive = "msdyn_CheckIfProcessesAreActive";
        public static readonly string msdyn_CheckIfProcessesOwnedByDisabledUsers = "msdyn_CheckIfProcessesOwnedByDisabledUsers";
        public static readonly string msdyn_CheckIfSalesFormsFromUnmanagedLayer = "msdyn_CheckIfSalesFormsFromUnmanagedLayer";
        public static readonly string msdyn_CheckIfSDKMessageProcessingStepsAreActive = "msdyn_CheckIfSDKMessageProcessingStepsAreActive";
        public static readonly string msdyn_CheckOrgSettingIsSOPIntegrationEnabled = "msdyn_CheckOrgSettingIsSOPIntegrationEnabled";
        public static readonly string msdyn_CheckOrgSettingOOBPriceCalculationEnabled = "msdyn_CheckOrgSettingOOBPriceCalculationEnabled";
        public static readonly string msdyn_CheckReqRibbonCommandDef = "msdyn_CheckReqRibbonCommandDef";
        public static readonly string msdyn_CheckReqWebResourceOnSystemForm = "msdyn_CheckReqWebResourceOnSystemForm";
        public static readonly string msdyn_ConditionXmlConversion = "msdyn_ConditionXmlConversion";
        public static readonly string msdyn_CreateActionFlow = "msdyn_CreateActionFlow";
        public static readonly string msdyn_CreateNewAnalysisJobForRuleSet = "msdyn_CreateNewAnalysisJobForRuleSet";
        public static readonly string msdyn_CreateSimilarOpportunitiyPredictionModel = "msdyn_CreateSimilarOpportunitiyPredictionModel";
        public static readonly string msdyn_CreateSuggestedActivity = "msdyn_CreateSuggestedActivity";
        public static readonly string msdyn_CreateSuggestedContact = "msdyn_CreateSuggestedContact";
        public static readonly string msdyn_DataValidationApi = "msdyn_DataValidationApi";
        public static readonly string msdyn_DeleteCalendar = "msdyn_DeleteCalendar";
        public static readonly string msdyn_DeleteFlow = "msdyn_DeleteFlow";
        public static readonly string msdyn_DismissSuggestedActivity = "msdyn_DismissSuggestedActivity";
        public static readonly string msdyn_DismissSuggestedContact = "msdyn_DismissSuggestedContact";
        public static readonly string msdyn_EditAndSaveSuggestedContact = "msdyn_EditAndSaveSuggestedContact";
        public static readonly string msdyn_EnableLinkedInDataValidation = "msdyn_EnableLinkedInDataValidation";
        public static readonly string msdyn_EnableSharePoint = "msdyn_EnableSharePoint";
        public static readonly string msdyn_ExecuteARC = "msdyn_ExecuteARC";
        public static readonly string msdyn_ExecutePrimaryCreatePostActions = "msdyn_ExecutePrimaryCreatePostActions";
        public static readonly string msdyn_ExecuteSIRequest = "msdyn_ExecuteSIRequest";
        public static readonly string msdyn_Feedback = "msdyn_Feedback";
        public static readonly string msdyn_FetchLISNProfileAssociations = "msdyn_FetchLISNProfileAssociations";
        public static readonly string msdyn_ForecastApi = "msdyn_ForecastApi";
        public static readonly string msdyn_ForecastGenerateHierarchy = "msdyn_ForecastGenerateHierarchy";
        public static readonly string msdyn_ForecastInstanceActions = "msdyn_ForecastInstanceActions";
        public static readonly string msdyn_ForecastRecalculate = "msdyn_ForecastRecalculate";
        public static readonly string msdyn_ForecastRecalculateAll = "msdyn_ForecastRecalculateAll";
        public static readonly string msdyn_ForecastRecalculateAsync = "msdyn_ForecastRecalculateAsync";
        public static readonly string msdyn_GDPROptoutContact = "msdyn_GDPROptoutContact";
        public static readonly string msdyn_GDPROptoutLead = "msdyn_GDPROptoutLead";
        public static readonly string msdyn_GDPROptoutUser = "msdyn_GDPROptoutUser";
        public static readonly string msdyn_GetACIMarsConnectorStatus = "msdyn_GetACIMarsConnectorStatus";
        public static readonly string msdyn_GetAdditionalPropertiesMetadata = "msdyn_GetAdditionalPropertiesMetadata";
        public static readonly string msdyn_GetAnalyticsReportToken = "msdyn_GetAnalyticsReportToken";
        public static readonly string msdyn_GetAutoCaptureUri = "msdyn_GetAutoCaptureUri";
        public static readonly string msdyn_GetCaseAggregation = "msdyn_GetCaseAggregation";
        public static readonly string msdyn_GetIoTAlertAggregation = "msdyn_GetIoTAlertAggregation";
        public static readonly string msdyn_GetIoTCommandMetadata = "msdyn_GetIoTCommandMetadata";
        public static readonly string msdyn_GetIoTVisualizationSummary = "msdyn_GetIoTVisualizationSummary";
        public static readonly string msdyn_GetKAObjectFromTemplate = "msdyn_GetKAObjectFromTemplate";
        public static readonly string msdyn_GetLegalAcceptanceStatus = "msdyn_GetLegalAcceptanceStatus";
        public static readonly string msdyn_GetNotesAnalysis = "msdyn_GetNotesAnalysis";
        public static readonly string msdyn_GetOrganizationProvisioningStatus = "msdyn_GetOrganizationProvisioningStatus";
        public static readonly string msdyn_GetRecordUsers = "msdyn_GetRecordUsers";
        public static readonly string msdyn_GetRelatedIoTDevicesByEntity = "msdyn_GetRelatedIoTDevicesByEntity";
        public static readonly string msdyn_GetRIProvisioningStatus = "msdyn_GetRIProvisioningStatus";
        public static readonly string msdyn_GetRITenantEndpoint = "msdyn_GetRITenantEndpoint";
        public static readonly string msdyn_GetServiceBaseUrl = "msdyn_GetServiceBaseUrl";
        public static readonly string msdyn_GetSILicenseStatus = "msdyn_GetSILicenseStatus";
        public static readonly string msdyn_GetSIPackageStatus = "msdyn_GetSIPackageStatus";
        public static readonly string msdyn_GetTalkingPoints = "msdyn_GetTalkingPoints";
        public static readonly string msdyn_GetTimeLineRecords = "msdyn_GetTimeLineRecords";
        public static readonly string msdyn_GetUserConsentStatus = "msdyn_GetUserConsentStatus";
        public static readonly string msdyn_InvokeServiceStoredProc = "msdyn_InvokeServiceStoredProc";
        public static readonly string msdyn_IoTGetDeviceEvents = "msdyn_IoTGetDeviceEvents";
        public static readonly string msdyn_IoTGetEditablePropertiesMetadata = "msdyn_IoTGetEditablePropertiesMetadata";
        public static readonly string msdyn_IoTGetIoTAlertCustomAttributes = "msdyn_IoTGetIoTAlertCustomAttributes";
        public static readonly string msdyn_IoTGetModelStatusInternal = "msdyn_IoTGetModelStatusInternal";
        public static readonly string msdyn_IoTGetTagsMetadata = "msdyn_IoTGetTagsMetadata";
        public static readonly string msdyn_IoTHubGetAggregatedDeviceReadings = "msdyn_IoTHubGetAggregatedDeviceReadings";
        public static readonly string msdyn_IoTHubPullDeviceData = "msdyn_IoTHubPullDeviceData";
        public static readonly string msdyn_IoTHubQueryDeviceReadings = "msdyn_IoTHubQueryDeviceReadings";
        public static readonly string msdyn_IoTHubRegisterDevice = "msdyn_IoTHubRegisterDevice";
        public static readonly string msdyn_IoTNewPrimaryAlert = "msdyn_IoTNewPrimaryAlert";
        public static readonly string msdyn_IoTPullDeviceDataActionHandler = "msdyn_IoTPullDeviceDataActionHandler";
        public static readonly string msdyn_IoTRegisterActionHandler = "msdyn_IoTRegisterActionHandler";
        public static readonly string msdyn_IoTSendTestAlert = "msdyn_IoTSendTestAlert";
        public static readonly string msdyn_IoTSetConfiguration = "msdyn_IoTSetConfiguration";
        public static readonly string msdyn_IsLinkedInDataValidationEnabled = "msdyn_IsLinkedInDataValidationEnabled";
        public static readonly string msdyn_IsMSTeamsUserSyncFeatureEnabled = "msdyn_IsMSTeamsUserSyncFeatureEnabled";
        public static readonly string msdyn_IsSharePointEnabled = "msdyn_IsSharePointEnabled";
        public static readonly string msdyn_JsonGetBoolean = "msdyn_JsonGetBoolean";
        public static readonly string msdyn_JsonGetNumber = "msdyn_JsonGetNumber";
        public static readonly string msdyn_JsonGetString = "msdyn_JsonGetString";
        public static readonly string msdyn_ManageSLAInstances = "msdyn_ManageSLAInstances";
        public static readonly string msdyn_MarketingListMetadataUpdate = "msdyn_MarketingListMetadataUpdate";
        public static readonly string msdyn_MarketingMetadataUpdate = "msdyn_MarketingMetadataUpdate";
        public static readonly string msdyn_MarketingMetadataUpdatePostImport = "msdyn_MarketingMetadataUpdatePostImport";
        public static readonly string msdyn_ParentIoTAlerts = "msdyn_ParentIoTAlerts";
        public static readonly string msdyn_PerformNotesAnalysisAction = "msdyn_PerformNotesAnalysisAction";
        public static readonly string msdyn_PostOperationRevokeUsrConsent = "msdyn_PostOperationRevokeUsrConsent";
        public static readonly string msdyn_PostOrganizationProvisioningStatus = "msdyn_PostOrganizationProvisioningStatus";
        public static readonly string msdyn_PostRetrieveRealTimeSuggestedActivities = "msdyn_PostRetrieveRealTimeSuggestedActivities";
        public static readonly string msdyn_ProvisionIoTSuggestionsMLModel = "msdyn_ProvisionIoTSuggestionsMLModel";
        public static readonly string msdyn_ProvisionIoTSuggestionsMLModelInternal = "msdyn_ProvisionIoTSuggestionsMLModelInternal";
        public static readonly string msdyn_ProvisionSharePointDocumentLibraries = "msdyn_ProvisionSharePointDocumentLibraries";
        public static readonly string msdyn_PullDataForIoTDevice = "msdyn_PullDataForIoTDevice";
        public static readonly string msdyn_QueryExchange = "msdyn_QueryExchange";
        public static readonly string msdyn_RegisterCustomEntity = "msdyn_RegisterCustomEntity";
        public static readonly string msdyn_RegisterIoTDevice = "msdyn_RegisterIoTDevice";
        public static readonly string msdyn_RegisterSolutionHealthRule = "msdyn_RegisterSolutionHealthRule";
        public static readonly string msdyn_ResolveSolutionHealthRuleFailure = "msdyn_ResolveSolutionHealthRuleFailure";
        public static readonly string msdyn_RetrieveActivities = "msdyn_RetrieveActivities";
        public static readonly string msdyn_RetrieveKPIvaluesfromDCI = "msdyn_RetrieveKPIvaluesfromDCI";
        public static readonly string msdyn_RetrieveKpiValuesFromRI = "msdyn_RetrieveKpiValuesFromRI";
        public static readonly string msdyn_RetrieveTypeValuesFromDCI = "msdyn_RetrieveTypeValuesFromDCI";
        public static readonly string msdyn_RunSolutionCheckerRules = "msdyn_RunSolutionCheckerRules";
        public static readonly string msdyn_SaveCalendar = "msdyn_SaveCalendar";
        public static readonly string msdyn_SendAnalyticsProvisionRequest = "msdyn_SendAnalyticsProvisionRequest";
        public static readonly string msdyn_SetFeatureStatus = "msdyn_SetFeatureStatus";
        public static readonly string msdyn_SetLegalAcceptanceStatus = "msdyn_SetLegalAcceptanceStatus";
        public static readonly string msdyn_SetSharePointDocumentStatus = "msdyn_SetSharePointDocumentStatus";
        public static readonly string msdyn_SetTalkingPointLikedStatus = "msdyn_SetTalkingPointLikedStatus";
        public static readonly string msdyn_SetTeamsDocumentStatus = "msdyn_SetTeamsDocumentStatus";
        public static readonly string msdyn_SIGetFCBStatus = "msdyn_SIGetFCBStatus";
        public static readonly string msdyn_StartRIProvisioning = "msdyn_StartRIProvisioning";
        public static readonly string msdyn_TrackExchangeActivity = "msdyn_TrackExchangeActivity";
        public static readonly string msdyn_UpdateAutoCaptureSettings = "msdyn_UpdateAutoCaptureSettings";
        public static readonly string msdyn_Updatefeatureconfig = "msdyn_Updatefeatureconfig";
        public static readonly string msdyn_UpdateRITenantInfo = "msdyn_UpdateRITenantInfo";
        public static readonly string msdyn_UpdateUserConsentBasedonSecurityRoles = "msdyn_UpdateUserConsentBasedonSecurityRoles";
        public static readonly string msdyn_UpgradeTelemetry = "msdyn_UpgradeTelemetry";
        public static readonly string msdyn_ValidateTSIKey = "msdyn_ValidateTSIKey";
        public static readonly string none_RemoveMarketingListMembersByIds = "none_RemoveMarketingListMembersByIds";
        public static readonly string nspi_OrderCancelOrderinsyteline = "nspi_OrderCancelOrderinsyteline";
        public static readonly string nspi_SendAccountToSyteLine = "nspi_SendAccountToSyteLine";
        public static readonly string nspi_SendAddressLocationToSyteLine = "nspi_SendAddressLocationToSyteLine";
        public static readonly string nspi_SendIncidentToSharePoint = "nspi_SendIncidentToSharePoint";
        public static readonly string nspi_SendIncidentToSyteLine = "nspi_SendIncidentToSyteLine";
        public static readonly string nspi_SendSalesOrderToSyteLine = "nspi_SendSalesOrderToSyteLine";
        public static readonly string OrderOption = "OrderOption";
        public static readonly string Parse = "Parse";
        public static readonly string PickFromQueue = "PickFromQueue";
        public static readonly string Predict = "Predict";
        public static readonly string PredictByReference = "PredictByReference";
        public static readonly string PredictionSchema = "PredictionSchema";
        public static readonly string ProcessInbound = "ProcessInbound";
        public static readonly string PropagateByExpression = "PropagateByExpression";
        public static readonly string ProvisionLanguage = "ProvisionLanguage";
        public static readonly string ProvisionLanguageAsync = "ProvisionLanguageAsync";
        public static readonly string Publish = "Publish";
        public static readonly string PublishAIConfiguration = "PublishAIConfiguration";
        public static readonly string PublishAll = "PublishAll";
        public static readonly string PublishProductHierarchy = "PublishProductHierarchy";
        public static readonly string PublishTheme = "PublishTheme";
        public static readonly string QualifyLead = "QualifyLead";
        public static readonly string QualifyMember = "QualifyMember";
        public static readonly string Query = "Query";
        public static readonly string QueryMultiple = "QueryMultiple";
        public static readonly string QueueUpdateRibbonClientMetadata = "QueueUpdateRibbonClientMetadata";
        public static readonly string QuickTest = "QuickTest";
        public static readonly string ReactivateEntityKey = "ReactivateEntityKey";
        public static readonly string ReassignObjects = "ReassignObjects";
        public static readonly string ReassignObjectsEx = "ReassignObjectsEx";
        public static readonly string Recalculate = "Recalculate";
        public static readonly string RecalculatePrice = "RecalculatePrice";
        public static readonly string RecognizeText = "RecognizeText";
        public static readonly string ReleaseToQueue = "ReleaseToQueue";
        public static readonly string RemoveActiveCustomizations = "RemoveActiveCustomizations";
        public static readonly string RemoveAppComponents = "RemoveAppComponents";
        public static readonly string RemoveFromQueue = "RemoveFromQueue";
        public static readonly string RemoveItem = "RemoveItem";
        public static readonly string RemoveListMembers = "RemoveListMembers";
        public static readonly string RemoveMember = "RemoveMember";
        public static readonly string RemoveMembers = "RemoveMembers";
        public static readonly string RemoveParent = "RemoveParent";
        public static readonly string RemovePrivilege = "RemovePrivilege";
        public static readonly string RemoveProductFromKit = "RemoveProductFromKit";
        public static readonly string RemoveRelated = "RemoveRelated";
        public static readonly string RemoveSolutionComponent = "RemoveSolutionComponent";
        public static readonly string RemoveSubstitute = "RemoveSubstitute";
        public static readonly string RemoveUserFromRecordTeam = "RemoveUserFromRecordTeam";
        public static readonly string RemoveUserRoles = "RemoveUserRoles";
        public static readonly string Renew = "Renew";
        public static readonly string RenewEntitlement = "RenewEntitlement";
        public static readonly string ReplacePrivileges = "ReplacePrivileges";
        public static readonly string Reschedule = "Reschedule";
        public static readonly string ResetOfflineFilters = "ResetOfflineFilters";
        public static readonly string ResetUserFilters = "ResetUserFilters";
        public static readonly string Retrieve = "Retrieve";
        public static readonly string RetrieveAbsoluteAndSiteCollectionUrl = "RetrieveAbsoluteAndSiteCollectionUrl";
        public static readonly string RetrieveActivePath = "RetrieveActivePath";
        public static readonly string RetrieveAllChildUsers = "RetrieveAllChildUsers";
        public static readonly string RetrieveAllEntities = "RetrieveAllEntities";
        public static readonly string RetrieveAllManagedProperties = "RetrieveAllManagedProperties";
        public static readonly string RetrieveAllOptionSets = "RetrieveAllOptionSets";
        public static readonly string RetrieveAnalyticsStoreDetails = "RetrieveAnalyticsStoreDetails";
        public static readonly string RetrieveAppComponents = "RetrieveAppComponents";
        public static readonly string RetrieveApplicationRibbon = "RetrieveApplicationRibbon";
        public static readonly string RetrieveAppSetting = "RetrieveAppSetting";
        public static readonly string RetrieveAppSettingList = "RetrieveAppSettingList";
        public static readonly string RetrieveAttribute = "RetrieveAttribute";
        public static readonly string RetrieveAttributeChangeHistory = "RetrieveAttributeChangeHistory";
        public static readonly string RetrieveAuditDetails = "RetrieveAuditDetails";
        public static readonly string RetrieveAuditPartitionList = "RetrieveAuditPartitionList";
        public static readonly string RetrieveAvailableLanguages = "RetrieveAvailableLanguages";
        public static readonly string RetrieveBusinessHierarchy = "RetrieveBusinessHierarchy";
        public static readonly string RetrieveByGroup = "RetrieveByGroup";
        public static readonly string RetrieveByResource = "RetrieveByResource";
        public static readonly string RetrieveByResources = "RetrieveByResources";
        public static readonly string RetrieveByTopIncidentProduct = "RetrieveByTopIncidentProduct";
        public static readonly string RetrieveByTopIncidentSubject = "RetrieveByTopIncidentSubject";
        public static readonly string RetrieveChannelAccessProfilePrivileges = "RetrieveChannelAccessProfilePrivileges";
        public static readonly string RetrieveCurrentOrganization = "RetrieveCurrentOrganization";
        public static readonly string RetrieveDataEncryptionKey = "RetrieveDataEncryptionKey";
        public static readonly string RetrieveDependenciesForDelete = "RetrieveDependenciesForDelete";
        public static readonly string RetrieveDependenciesForUninstall = "RetrieveDependenciesForUninstall";
        public static readonly string RetrieveDependentComponents = "RetrieveDependentComponents";
        public static readonly string RetrieveDeploymentLicenseType = "RetrieveDeploymentLicenseType";
        public static readonly string RetrieveDeprovisionedLanguages = "RetrieveDeprovisionedLanguages";
        public static readonly string RetrieveDuplicates = "RetrieveDuplicates";
        public static readonly string RetrieveEntity = "RetrieveEntity";
        public static readonly string RetrieveEntityChanges = "RetrieveEntityChanges";
        public static readonly string RetrieveEntityKey = "RetrieveEntityKey";
        public static readonly string RetrieveEntityRibbon = "RetrieveEntityRibbon";
        public static readonly string RetrieveExchangeAppointments = "RetrieveExchangeAppointments";
        public static readonly string RetrieveExchangeRate = "RetrieveExchangeRate";
        public static readonly string RetrieveFilteredForms = "RetrieveFilteredForms";
        public static readonly string RetrieveFormattedImportJobResults = "RetrieveFormattedImportJobResults";
        public static readonly string RetrieveFormXml = "RetrieveFormXml";
        public static readonly string RetrieveInstalledLanguagePacks = "RetrieveInstalledLanguagePacks";
        public static readonly string RetrieveInstalledLanguagePackVersion = "RetrieveInstalledLanguagePackVersion";
        public static readonly string RetrieveLicenseInfo = "RetrieveLicenseInfo";
        public static readonly string RetrieveLocLabels = "RetrieveLocLabels";
        public static readonly string RetrieveMailboxTrackingFolders = "RetrieveMailboxTrackingFolders";
        public static readonly string RetrieveManagedProperty = "RetrieveManagedProperty";
        public static readonly string RetrieveMembers = "RetrieveMembers";
        public static readonly string RetrieveMembersBulkOperation = "RetrieveMembersBulkOperation";
        public static readonly string RetrieveMetadataChanges = "RetrieveMetadataChanges";
        public static readonly string RetrieveMissingComponents = "RetrieveMissingComponents";
        public static readonly string RetrieveMissingDependencies = "RetrieveMissingDependencies";
        public static readonly string RetrieveMultiple = "RetrieveMultiple";
        public static readonly string RetrieveOptionSet = "RetrieveOptionSet";
        public static readonly string RetrieveOrganizationInfo = "RetrieveOrganizationInfo";
        public static readonly string RetrieveOrganizationResources = "RetrieveOrganizationResources";
        public static readonly string RetrieveParentGroups = "RetrieveParentGroups";
        public static readonly string RetrieveParsedData = "RetrieveParsedData";
        public static readonly string RetrievePersonalWall = "RetrievePersonalWall";
        public static readonly string RetrievePrincipalAccess = "RetrievePrincipalAccess";
        public static readonly string RetrievePrincipalAccessInfo = "RetrievePrincipalAccessInfo";
        public static readonly string RetrievePrincipalAttributePrivileges = "RetrievePrincipalAttributePrivileges";
        public static readonly string RetrievePrincipalSyncAttributeMappings = "RetrievePrincipalSyncAttributeMappings";
        public static readonly string RetrievePrivilegeSet = "RetrievePrivilegeSet";
        public static readonly string RetrieveProcessInstances = "RetrieveProcessInstances";
        public static readonly string RetrieveProductProperties = "RetrieveProductProperties";
        public static readonly string RetrieveProvisionedLanguagePackVersion = "RetrieveProvisionedLanguagePackVersion";
        public static readonly string RetrieveProvisionedLanguages = "RetrieveProvisionedLanguages";
        public static readonly string RetrieveRecordChangeHistory = "RetrieveRecordChangeHistory";
        public static readonly string RetrieveRecordWall = "RetrieveRecordWall";
        public static readonly string RetrieveRelationship = "RetrieveRelationship";
        public static readonly string RetrieveRequiredComponents = "RetrieveRequiredComponents";
        public static readonly string RetrieveRolePrivileges = "RetrieveRolePrivileges";
        public static readonly string RetrieveSharedPrincipalsAndAccess = "RetrieveSharedPrincipalsAndAccess";
        public static readonly string RetrieveSubGroups = "RetrieveSubGroups";
        public static readonly string RetrieveSubsidiaryTeams = "RetrieveSubsidiaryTeams";
        public static readonly string RetrieveSubsidiaryUsers = "RetrieveSubsidiaryUsers";
        public static readonly string RetrieveTeamPrivileges = "RetrieveTeamPrivileges";
        public static readonly string RetrieveTeams = "RetrieveTeams";
        public static readonly string RetrieveTimelineWallRecords = "RetrieveTimelineWallRecords";
        public static readonly string RetrieveTimestamp = "RetrieveTimestamp";
        public static readonly string RetrieveTotalRecordCount = "RetrieveTotalRecordCount";
        public static readonly string RetrieveUnpublished = "RetrieveUnpublished";
        public static readonly string RetrieveUnpublishedMultiple = "RetrieveUnpublishedMultiple";
        public static readonly string RetrieveUserLicenseInfo = "RetrieveUserLicenseInfo";
        public static readonly string RetrieveUserPrivilegeByPrivilegeId = "RetrieveUserPrivilegeByPrivilegeId";
        public static readonly string RetrieveUserPrivilegeByPrivilegeName = "RetrieveUserPrivilegeByPrivilegeName";
        public static readonly string RetrieveUserPrivileges = "RetrieveUserPrivileges";
        public static readonly string RetrieveUserQueues = "RetrieveUserQueues";
        public static readonly string RetrieveUserSettings = "RetrieveUserSettings";
        public static readonly string RetrieveUsersPrivilegesThroughTeams = "RetrieveUsersPrivilegesThroughTeams";
        public static readonly string RetrieveVersion = "RetrieveVersion";
        public static readonly string RevertProduct = "RevertProduct";
        public static readonly string Revise = "Revise";
        public static readonly string RevokeAccess = "RevokeAccess";
        public static readonly string Rollup = "Rollup";
        public static readonly string Route = "Route";
        public static readonly string RouteTo = "RouteTo";
        public static readonly string SaveAppSetting = "SaveAppSetting";
        public static readonly string SchedulePrediction = "SchedulePrediction";
        public static readonly string ScheduleRetrain = "ScheduleRetrain";
        public static readonly string ScheduleTraining = "ScheduleTraining";
        public static readonly string Search = "Search";
        public static readonly string SearchByBody = "SearchByBody";
        public static readonly string SearchByBodyLegacy = "SearchByBodyLegacy";
        public static readonly string SearchByKeywords = "SearchByKeywords";
        public static readonly string SearchByKeywordsLegacy = "SearchByKeywordsLegacy";
        public static readonly string SearchByTitle = "SearchByTitle";
        public static readonly string SearchByTitleLegacy = "SearchByTitleLegacy";
        public static readonly string Send = "Send";
        public static readonly string SendFromTemplate = "SendFromTemplate";
        public static readonly string SetAutoNumberSeed = "SetAutoNumberSeed";
        public static readonly string SetBusiness = "SetBusiness";
        public static readonly string SetDataEncryptionKey = "SetDataEncryptionKey";
        public static readonly string SetFeatureStatus = "SetFeatureStatus";
        public static readonly string SetLocLabels = "SetLocLabels";
        public static readonly string SetParent = "SetParent";
        public static readonly string SetProcess = "SetProcess";
        public static readonly string SetRelated = "SetRelated";
        public static readonly string SetReportRelated = "SetReportRelated";
        public static readonly string SetState = "SetState";
        public static readonly string SetStateDynamicEntity = "SetStateDynamicEntity";
        public static readonly string StageAndUpgrade = "StageAndUpgrade";
        public static readonly string SyncBulkOperation = "SyncBulkOperation";
        public static readonly string Train = "Train";
        public static readonly string Transform = "Transform";
        public static readonly string TriggerServiceEndpointCheck = "TriggerServiceEndpointCheck";
        public static readonly string UninstallSampleData = "UninstallSampleData";
        public static readonly string UnlockInvoicePricing = "UnlockInvoicePricing";
        public static readonly string UnlockSalesOrderPricing = "UnlockSalesOrderPricing";
        public static readonly string Unpublish = "Unpublish";
        public static readonly string UnpublishAIConfiguration = "UnpublishAIConfiguration";
        public static readonly string UnschedulePrediction = "UnschedulePrediction";
        public static readonly string UnscheduleTraining = "UnscheduleTraining";
        public static readonly string Update = "Update";
        public static readonly string UpdateAttribute = "UpdateAttribute";
        public static readonly string UpdateEntity = "UpdateEntity";
        public static readonly string UpdateFeatureConfig = "UpdateFeatureConfig";
        public static readonly string UpdateOptionSet = "UpdateOptionSet";
        public static readonly string UpdateOptionValue = "UpdateOptionValue";
        public static readonly string UpdateProductProperties = "UpdateProductProperties";
        public static readonly string UpdateRelationship = "UpdateRelationship";
        public static readonly string UpdateRibbonClientMetadata = "UpdateRibbonClientMetadata";
        public static readonly string UpdateSolutionComponent = "UpdateSolutionComponent";
        public static readonly string UpdateStateValue = "UpdateStateValue";
        public static readonly string UpdateUserSettings = "UpdateUserSettings";
        public static readonly string UploadBlock = "UploadBlock";
        public static readonly string Upsert = "Upsert";
        public static readonly string UtcTimeFromLocalTime = "UtcTimeFromLocalTime";
        public static readonly string Validate = "Validate";
        public static readonly string ValidateAIConfiguration = "ValidateAIConfiguration";
        public static readonly string ValidateApp = "ValidateApp";
        public static readonly string ValidateFetchXmlExpression = "ValidateFetchXmlExpression";
        public static readonly string ValidateRecurrenceRule = "ValidateRecurrenceRule";
        public static readonly string WhoAmI = "WhoAmI";
        public static readonly string Win = "Win";

    }
}

