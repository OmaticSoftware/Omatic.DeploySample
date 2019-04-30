using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Framework;
using System.Xml;
using OmaticSoftware.Deploy.AppFxWebSvc;

namespace OmaticSoftware.Deploy
{
    public class CustomLoadCatalogItems : Microsoft.Build.Utilities.Task
    {
        // Internal struct to hold data related to the item we're loading
        private struct ItemInfo
        {
            public string ItemName { get; set; }
            public string ItemSource { get; set; }
            public int ItemTypeCode { get; set; }
            public Guid ItemId { get; set; }
        }

        private string infinityPassword;
        private string infinityUserName;
        private ITaskItem[] itemsToLoad;
        private int timeOutSeconds = 0;
        private string webServiceDbName;
        private string webServiceUrl;

        #region Properties

        [Required]
        public ITaskItem[] ItemsToLoad
        {
            get { return itemsToLoad; }
            set { itemsToLoad = value; }
        }

        [Required]
        public string WebServiceDbName
        {
            get { return webServiceDbName; }
            set { webServiceDbName = value; }
        }

        [Required]
        public string WebServiceUrl
        {
            get { return webServiceUrl; }
            set { webServiceUrl = value; }
        }

        public string InfinityUserName
        {
            get { return infinityUserName; }
            set { infinityUserName = value; }
        }

        public string InfinityPassword
        {
            get { return infinityPassword; }
            set { infinityPassword = value; }
        }

        public int TimeOutSeconds
        {
            get { return timeOutSeconds; }
            set { timeOutSeconds = value; }
        }

        #endregion


        #region ITask

        public override bool Execute()
        {
            bool successfulLoad = true;

            Log.LogMessage("Configuring AppFxWebServiceProvider");
            Log.LogMessage("User: {0}", string.IsNullOrEmpty(InfinityUserName) ? "[Default credentials]" : InfinityUserName);
            Log.LogMessage("URL: {0}\nDB: {1}", WebServiceUrl, WebServiceDbName);
            
            ClientAppInfoHeader header = new ClientAppInfoHeader();
            header.ClientAppName = "Deploy Utility";
            header.REDatabaseToUse = WebServiceDbName;

            AppFxWebService appFxWebService = new AppFxWebService();
            appFxWebService.Url = WebServiceUrl;
            appFxWebService.Timeout = (TimeOutSeconds > 0 ? TimeOutSeconds * 1000 : 3000000);

            if (!string.IsNullOrEmpty(InfinityUserName))
                appFxWebService.Credentials = new System.Net.NetworkCredential(InfinityUserName, InfinityPassword);
            else
                appFxWebService.Credentials = System.Net.CredentialCache.DefaultCredentials;

            try
            {
                successfulLoad = LoadItems(appFxWebService, header);
            }
            catch (Exception ex)
            {
                Log.LogErrorFromException(ex);
                successfulLoad = false;
            }

            return successfulLoad;
        }

        #endregion

        private bool LoadItems(AppFxWebService appFxWebService, ClientAppInfoHeader header)
        {
            bool successfulLoad = true;

            // Loop the items to load, calling the appfx load catalog item web service
            foreach (ITaskItem itemToLoad in ItemsToLoad)
            {
                string sourceInfo = itemToLoad.GetMetadata("SourceInfo");
                successfulLoad = ProcessItemToLoad(appFxWebService, header, itemToLoad.ItemSpec, sourceInfo);
            }

            return successfulLoad;
        }

        private bool ProcessItemToLoad(AppFxWebService appFxService, ClientAppInfoHeader header, string itemName, string sourceInfo)
        {
            Log.LogMessage("Loading '{0}' from '{1}'", itemName, sourceInfo);

            bool successfulLoad = true;

            ItemInfo itemToLoadInfo = GetItemInfo(appFxService, header, itemName, sourceInfo);

            // Load the item
            CatalogBrowserLoadCatalogItemReply loadReply = null;
            try
            {
                loadReply = LoadCatalogItem(appFxService, header, itemToLoadInfo);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("ItemAlreadyExistsException"))
                {
                    // Not sure why these keep occurring, but going to skip them and see what happens.
                    Log.LogMessage("Encountered ItemAlreadyExistsException, skipping this report.");
                }
                else
                {
                    Log.LogMessage("exception ToString: {0}", ex.ToString());
                    throw;
                }
            }

            if (loadReply != null)
            {
                Log.LogMessage("{0} item(s) loaded", loadReply.LoadedItems.Count());

                if (itemToLoadInfo.ItemTypeCode == 5) // PackageSpec
                {
                    IEnumerable<string> itemsFromPackageSpec = GetPackageDependencies(appFxService, header, itemToLoadInfo);

                    // See if our itemsFromPackageSpec items were loaded
                    foreach (string supposedToLoad in itemsFromPackageSpec)
                    {
                        // See if our "supposedToLoad" was loaded
                        if (loadReply.LoadedItems.Where(x => x.ItemResourceName.Equals(supposedToLoad, StringComparison.InvariantCultureIgnoreCase)).Count() < 1)
                        {
                            Log.LogError("Item '{0}' from package spec was not loaded.  Check to make sure it is present.", supposedToLoad);
                            successfulLoad = false;
                        }
                    }
                }
            }

            return successfulLoad;
        }


        private ItemInfo GetItemInfo(AppFxWebService appFxService, ClientAppInfoHeader header, string itemName, string sourceInfo)
        {
            ItemInfo itemInfo = new ItemInfo();


            // Retrieve item info from 'Catalog Browser List' datalist so we can determine how we want to load it
            DataListLoadRequest listLoadReq = new DataListLoadRequest();
            listLoadReq.ClientAppInfo = header;
            listLoadReq.DataListID = new Guid("91907a4f-14a3-4433-b780-a030c01ca452"); // Catalog Browser List

            // Create params with SOURCE set to the source from the msbuild item metadata
            listLoadReq.Parameters = new DataFormItem();
            listLoadReq.Parameters.Values = new DataFormFieldValue[] 
            {
                new DataFormFieldValue() { ID = "SOURCE", Value = sourceInfo }
            };

            // Let any exceptions from this bubble upwards
            DataListLoadReply listLoadReply = null;
            listLoadReply = appFxService.DataListLoad(listLoadReq);

            // Loading a few constants for clarity's sake
            int itemSourceColNum = 3;
            int itemIdColNum = 5;
            int itemResourceNameColNum = 8;
            int itemTypeCodeColNum = 14;

            // Look for the item we're supposed to load in the datalist results
            DataListResultRow rowToLoad = listLoadReply.Rows.Where(r => r.Values[itemResourceNameColNum].Equals(itemName, StringComparison.InvariantCultureIgnoreCase)).Select(r => r).FirstOrDefault();
            if (rowToLoad == null)
                throw new Exception(string.Format("Did not find '{0}' in items loaded from '{1}'.", itemName, sourceInfo));

            int tmpItemTypeCode = int.MinValue;
            if (!int.TryParse(rowToLoad.Values[itemTypeCodeColNum], out tmpItemTypeCode))
                throw new Exception(string.Format("Failed to parse item type code from '{0}'", rowToLoad.Values[itemTypeCodeColNum]));

            itemInfo.ItemTypeCode = tmpItemTypeCode;
            itemInfo.ItemId = new Guid(rowToLoad.Values[itemIdColNum]);
            itemInfo.ItemName = rowToLoad.Values[itemResourceNameColNum];
            itemInfo.ItemSource = rowToLoad.Values[itemSourceColNum];

            return itemInfo;
        }


        private CatalogBrowserLoadCatalogItemReply LoadCatalogItem(AppFxWebService appFxService, ClientAppInfoHeader header, ItemInfo itemToLoad)
        {
            Log.LogMessage("Calling CatalogBrowserLoadCatalogItem for '{0}'.", itemToLoad.ItemName);

            // Build the request
            CatalogBrowserLoadCatalogItemRequest loadReq = new CatalogBrowserLoadCatalogItemRequest();
            loadReq.ClientAppInfo = header;
            loadReq.ItemName = itemToLoad.ItemName;
            loadReq.SourceInfo = new SourceInfo() { Name = itemToLoad.ItemSource, Type = SourceType.Assembly };
            loadReq.ForceReload = true;
            loadReq.ForceReloadDependencies = false;
            loadReq.TrackLoadedItems = true;

            CatalogBrowserLoadCatalogItemReply loadReply = null;
            loadReply = appFxService.CatalogBrowserLoadCatalogItem(loadReq);

            if (loadReply == null)
                throw new Exception("Null reply from CatalogBrowserLoadCatalogItem");

            return loadReply;
        }

        private IEnumerable<string> GetPackageDependencies(AppFxWebService appFxService, ClientAppInfoHeader header, ItemInfo itemToLoad)
        {
            Log.LogMessage("Reading package spec '{0}' (ID: {1})", itemToLoad.ItemName, itemToLoad.ItemId);

            DataFormLoadRequest dfLoadReq = new DataFormLoadRequest();
            dfLoadReq.ClientAppInfo = header;
            dfLoadReq.FormID = new Guid("8dcaab58-3b6a-405a-9430-5d07cb000d22"); // Catalog Item Xml View Form
            dfLoadReq.RecordID = string.Format("5|{0}", itemToLoad.ItemId);  // Record ID is "[item type code]|[item ID]"

            DataFormLoadReply dfLoadReply = appFxService.DataFormLoad(dfLoadReq);

            if (dfLoadReply == null)
                throw new Exception("Null reply from DataFormLoad.");

            // Get item XML
            string itemXml = dfLoadReply.DataFormItem.Values.Where(x => x.ID == "ITEMXML").Select(x => x.Value.ToString()).FirstOrDefault();
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(itemXml);

            // Load <Dependency> nodes, add items to load to list
            XmlNamespaceManager nsMgr = new XmlNamespaceManager(xmlDoc.NameTable);
            nsMgr.AddNamespace("c", "bb_appfx_commontypes");

            XmlNodeList nodeList = xmlDoc.SelectNodes("//c:Dependency", nsMgr);
            if (nodeList.Count <= 0)
                Log.LogWarning("Failed to find any Dependency nodes in package spec.");

            List<string> itemsFromPackageSpec = new List<string>();
            foreach (XmlNode dependencyNode in nodeList)
            {
                itemsFromPackageSpec.Add(dependencyNode.Attributes["CatalogItem"].Value);
            }

            return itemsFromPackageSpec;
        }
    }
}
