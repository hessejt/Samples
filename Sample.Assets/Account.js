/// <reference path="IntellisenseFiles/Account Form - Client Account Form.js" />
/// <reference path="IntellisenseFiles/Xrm.js" />

mcaConnect = window.mcaConnect || { __namespace: true, __typeName: "mcaConnect" };
mcaConnect.Client = mcaConnect.Client || { __namespace: true, __typeName: "mcaConnect.Client" };
mcaConnect.Client.Account = mcaConnect.Client.Account || ({

    onLoad: function () {
        mcaConnect.Client.Account.ShowHideState();
    },
    DoesCountryHaveAState: function () {
        var returnVar = false;
        if (Xrm.Page.getAttribute("mcabl_country") && Xrm.Page.getAttribute("mcabl_country").getValue()) {
            var country = Xrm.Page.getAttribute("mcabl_country").getValue()[0].id;
            var req = new XMLHttpRequest();
            req.open("GET", Xrm.Page.context.getClientUrl() + "/api/data/v8.2/mcabl_states?$select=mcabl_stateid&$filter=_new_countryid_value eq " + mcaConnect.Client.Account.stripCurlies(country) + "&$count=true", false);
            req.setRequestHeader("OData-MaxVersion", "4.0");
            req.setRequestHeader("OData-Version", "4.0");
            req.setRequestHeader("Accept", "application/json");
            req.setRequestHeader("Content-Type", "application/json; charset=utf-8");
            req.setRequestHeader("Prefer", "odata.include-annotations=\"*\"");
            req.onreadystatechange = function () {
                if (this.readyState === 4) {
                    req.onreadystatechange = null;
                    if (this.status === 200) {
                        var results = JSON.parse(this.response);
                        var recordCount = results["@odata.count"];
                        if (recordCount > 0) {
                            returnVar = true;
                            return true;

                        }

                    } else {
                        Xrm.Utility.alertDialog(this.statusText);
                    }
                }
            };
            req.send();

            return returnVar;
        }
    },

    OnChangeofCountry: function () {
        var isStateThere = false;
        isStateThere = mcaConnect.Client.Account.DoesCountryHaveAState();
        mcaConnect.Client.Account.ShowHideState(isStateThere);
    },   
    ShowHideState: function (isStateThere) {
        if (Xrm.Page.getAttribute("mcabl_country") && Xrm.Page.getAttribute("mcabl_country").getValue()) {
            if (isStateThere) {
                Xrm.Page.getControl("apm02_statelookup").setVisible(true);
                Xrm.Page.getControl("address1_stateorprovince").setVisible(false);
            }
            else {
                Xrm.Page.getControl("apm02_statelookup").setVisible(false);
                Xrm.Page.getControl("address1_stateorprovince").setVisible(true);
            }
        }

        if (Xrm.Page.getAttribute("apm02_sapid").getValue() !== null) {
            Xrm.Page.getControl("apm02_statelookup").setVisible(false);
            Xrm.Page.getControl("address1_stateorprovince").setVisible(true);
            return;
        }

    },

    __namespace: true,
    __typeName: "mcaConnect.Client.Account"
});