using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugins
{
    class QualifyHelper
    {
        public string QualifyLead(EntityReference leadReference, IOrganizationService service)
        {
            var leadCols = new ColumnSet(new String[] { "subject", "new_businessstream", "new_verticalmarketid", "mcabl_country", "new_productcategory", "new_ueemanrepagencyid", "new_uesite", "new_projectrole", "firstname", "lastname", "telephone1", "createdby", "parentaccountid" });
            Entity lead = service.Retrieve(leadReference.LogicalName, leadReference.Id, leadCols);
            if (lead.GetAttributeValue<EntityReference>("parentaccountid") != null)
            {
                var accountCols = new ColumnSet(new String[] { "defaultpricelevelid", "transactioncurrencyid", "accountcategorycode", "new_supplyagrmntcat", "new_termsid" });
                Entity account = service.Retrieve("account", lead.GetAttributeValue<EntityReference>("parentaccountid").Id, accountCols);
                Entity target = MapQuoteFields(service, lead, account);
                service.Create(target);

                //fetch quote that was just created, pass id back to main function
                string quoteQuery = "<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>" +
                                                  "<entity name='quote' >" +
                                                    "<attribute name='name' />" +
                                                    "<attribute name='quoteid' />" +
                                                    "<attribute name='createdon' />" +
                                                    "<order attribute='createdon' descending='true' />" +
                                                    "<filter type='and' >" +
                                                      "<condition attribute='new_originatinglead' operator='eq' value='" + lead.Id + "' />" +
                                                    "</filter >" +
                                                  "</entity>" +
                                                 "</fetch>";
                EntityCollection quoteCollection = service.RetrieveMultiple(new FetchExpression(quoteQuery));
                if (quoteCollection.Entities.Count > 0)
                {
                    Entity quote = quoteCollection.Entities.FirstOrDefault();
                    string quoteId = quote.Id.ToString();
                    return quoteId;
                }
                else return null;
            }
            else return null;
        }
        public Entity MapQuoteFields(IOrganizationService service, Entity lead, Entity account)
        {
            Entity target = new Entity("quote");
            target["name"] = lead.GetAttributeValue<string>("subject");
            target["new_businessstream"] = lead.GetAttributeValue<EntityReference>("new_businessstream");
            //Fields mapped from Lead
            target["new_verticalmarketapplication"] = lead.GetAttributeValue<EntityReference>("new_verticalmarketid");
            target["new_primarycountryappl"] = lead.GetAttributeValue<EntityReference>("new_country");
            target["new_productcategory"] = lead.GetAttributeValue<OptionSetValue>("new_productcategory");

            if (lead["new_projectrole"] != null)//lead and quote entities don't use a global optionset for this field
            {
                var projectRole = lead.FormattedValues["new_projectrole"];
                Int32 targetOptionValue = MapEnum(service, "quote", "new_projectrole", projectRole, lead.GetAttributeValue<OptionSetValue>("new_projectrole"));
                if (targetOptionValue != -1)
                    target.Attributes.Add("new_projectrole", new OptionSetValue(targetOptionValue));
            }
            target["new_contact"] = lead.GetAttributeValue<string>("firstname") + " " + lead.GetAttributeValue<string>("lastname");
            target["new_phone"] = lead.GetAttributeValue<string>("telephone1");
            target["ownerid"] = lead.GetAttributeValue<EntityReference>("createdby");
            target["new_originatinglead"] = lead.ToEntityReference();
            target["customerid"] = lead.GetAttributeValue<string>("parentaccountid");

            //Fields mapped from Account

            target["pricelevelid"] = account.GetAttributeValue<EntityReference>("defaultpricelevelid");
            target["transactioncurrencyid"] = account.GetAttributeValue<EntityReference>("transactioncurrencyid");
            if (account.GetAttributeValue<OptionSetValue>("accountcategorycode") != null)//account and quote entities don't use global optionset for field
            {
                var accountGrade = account.FormattedValues["accountcategorycode"];
                Int32 targetOptionValue = MapEnum(service, "quote", "new_accountcategorycode", accountGrade, account.GetAttributeValue<OptionSetValue>("accountcategorycode"));
                if (targetOptionValue != -1)
                    target.Attributes.Add("new_accountcategorycode", new OptionSetValue(targetOptionValue));
            }
            if (account.GetAttributeValue<OptionSetValue>("new_supplyagrmntcat") != null)//account and quote entities don't use global optionset for field
            {
                var supplyAgrmntCat = account.FormattedValues["new_supplyagrmntcat"];
                Int32 targetOptionValue = MapEnum(service, "quote", "new_supplyagrmntcat", supplyAgrmntCat, account.GetAttributeValue<OptionSetValue>("new_supplyagrmntcat"));
                if (targetOptionValue != -1)
                    target.Attributes.Add("new_supplyagrmntcat", new OptionSetValue(targetOptionValue));
            }
            target["new_termscodeid"] = account.GetAttributeValue<EntityReference>("new_termsid");
            return target;
        }

        //Optionsets aren't all global, so there's some mismatch between int values of the enums being mapped across entities
        public Int32 MapEnum(IOrganizationService service, string entityLogicalName, string fieldLogicalName, string sourceEnumLabel, OptionSetValue sourceEnumValue)
        {
            Int32 targetEnumValue = -1;
            string FetchXml = "<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>" +
                                                  "<entity name ='stringmap' >" +
                                                    "<attribute name ='attributevalue' />" +
                                                    "<attribute name ='value' />" +
                                                    "<filter type ='and' >" +
                                                      "<condition attribute ='objecttypecodename' operator='eq' value = '" + entityLogicalName + "' />" +
                                                      "<condition attribute ='attributename' operator='eq' value = '" + fieldLogicalName + "' />" +
                                                    "</filter >" +
                                                  "</entity >" +
                                                 "</fetch >";
            FetchExpression FetchXmlQuery = new FetchExpression(FetchXml);

            EntityCollection FetchXmlResult = service.RetrieveMultiple(FetchXmlQuery);

            if (FetchXmlResult.Entities.Count > 0)
            {
                foreach (Entity Stringmap in FetchXmlResult.Entities)
                {
                    string OptionLabel = Stringmap.Attributes.Contains("value") ? (string)Stringmap.Attributes["value"] : string.Empty;
                    Int32 OptionValue = Stringmap.Attributes.Contains("attributevalue") ? (Int32)Stringmap.Attributes["attributevalue"] : 0;
                    if (OptionLabel.ToLower() == sourceEnumLabel.ToLower())
                    {
                        targetEnumValue = OptionValue;
                    }
                    else if (sourceEnumValue.Value == OptionValue)
                    {
                        targetEnumValue = OptionValue;
                    }

                }
            }
            return targetEnumValue;
        }
    }
}
