Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports Microsoft.Build.Framework
Imports Microsoft.Build.Utilities
Imports System.Xml
Imports System.Web.Services.Protocols
Imports Omatic.Deploy.ServiceProxy

Namespace Omatic.Deploy
    Public Class LoadCatalogItems
        Inherits Task
        ' Internal struct to hold data related to the item we're loading
        Private Structure ItemInfo
            Public Property ItemName As String
            Public Property ItemSource As String
            Public Property ItemTypeCode As Integer
            Public Property ItemId As Guid
        End Structure



        Private proxy As AppFxWebService
        Private header As ClientAppInfoHeader

#Region "Properties"

        <Required()>
        Public Property ItemsToLoad As ITaskItem()
        <Required()>
        Public Property WebServiceDbName As String
        <Required()>
        Public Property WebServiceUrl As String
        Public Property InfinityUserName As String
        Public Property InfinityPassword As String
        Public Property TimeOutSeconds As Integer

#End Region

        Public Sub New()
            proxy = New AppFxWebService()
            header = New ClientAppInfoHeader()
            header.ClientAppName = "Deploy Utility"
        End Sub

#Region "ITask"

        Public Overrides Function Execute() As Boolean
            Dim successfulLoad As Boolean = True

            ' Set up header for use in web service calls
            header.REDatabaseToUse = WebServiceDbName

            Try
                successfulLoad = LoadItems()
            Catch ex As Exception
                Log.LogErrorFromException(ex)
                successfulLoad = False
            End Try

            Return successfulLoad
        End Function

#End Region

        Private Function LoadItems() As Boolean
            ' Set up web service
            proxy.Url = WebServiceUrl

            Dim successfulLoad As Boolean = True

            If Not [String].IsNullOrEmpty(InfinityUserName) Then
                proxy.Credentials = New System.Net.NetworkCredential(InfinityUserName, InfinityPassword)
            Else
                proxy.UseDefaultCredentials = True
            End If

            If TimeOutSeconds > 0 Then
                proxy.Timeout = TimeOutSeconds * 1000
            Else
                proxy.Timeout = 3000000
            End If


            Log.LogMessage("User: {0}", If([String].IsNullOrEmpty(InfinityUserName), "[Default credentials]", InfinityUserName))
            Log.LogMessage("URL: {0}" & vbLf & "DB: {1}", WebServiceUrl, WebServiceDbName)
            Log.LogMessage("Timeout: {0}s", proxy.Timeout / 1000)

            ' Loop the items to load, calling the appfx load catalog item web service
            For Each itemToLoad As ITaskItem In ItemsToLoad
                Dim sourceInfo As String = itemToLoad.GetMetadata("SourceInfo")
                successfulLoad = ProcessItemToLoad(itemToLoad.ItemSpec, sourceInfo)
            Next

            Return successfulLoad
        End Function

        Private Function ProcessItemToLoad(itemName As String, sourceInfo As String) As Boolean
            Log.LogMessage("Loading '{0}' from '{1}'", itemName, sourceInfo)

            Dim successfulLoad As Boolean = True

            Dim itemToLoadInfo As ItemInfo = GetItemInfo(itemName, sourceInfo)

            ' Load the item
            Dim loadReply As CatalogBrowserLoadCatalogItemReply = Nothing
            Try
                loadReply = LoadCatalogItem(itemToLoadInfo)
            Catch soapEx As SoapException
                If soapEx.Message.Contains("ItemAlreadyExistsException") Then
                    ' Seem to get these on occassion for no apparent reason
                    Log.LogMessage("Encountered ItemAlreadyExistsException, skipping this report.")
                Else
                    Log.LogMessage("exception ToString: {0}", soapEx.ToString())
                    Throw
                End If
            End Try

            If loadReply IsNot Nothing Then
                Log.LogMessage("{0} item(s) loaded", loadReply.LoadedItems.Count())

                If itemToLoadInfo.ItemTypeCode = 5 Then
                    ' PackageSpec
                    Dim itemsFromPackageSpec As IEnumerable(Of String) = GetPackageDependencies(itemToLoadInfo)

                    ' See if our itemsFromPackageSpec items were loaded
                    For Each supposedToLoad As String In itemsFromPackageSpec
                        Dim supposedToLoadCopy = supposedToLoad ' Create copy of iteration variable to avoid warning about using iteration variable in lambda
                        ' See if our "supposedToLoad" was loaded
                        If loadReply.LoadedItems.Where(Function(x) x.ItemResourceName.Equals(supposedToLoadCopy, StringComparison.InvariantCultureIgnoreCase)).Count() < 1 Then
                            Log.LogError("Item '{0}' from package spec was not loaded.  Check to make sure it is present.", supposedToLoadCopy)
                            successfulLoad = False
                        End If
                    Next
                End If
            End If

            Return successfulLoad
        End Function


        Private Function GetItemInfo(itemName As String, sourceInfo As String) As ItemInfo
            Dim itemInfo As New ItemInfo()

            ' Retrieve item info from 'Catalog Browser List' datalist so we can determine how we want to load it
            Dim listLoadReq As New DataListLoadRequest()
            listLoadReq.ClientAppInfo = header
            ' Catalog Browser List
            listLoadReq.DataListID = New Guid("91907a4f-14a3-4433-b780-a030c01ca452")
            listLoadReq.Parameters = New DataFormItem()
            listLoadReq.Parameters.Values = New DataFormFieldValue() {New DataFormFieldValue() With {
             .ID = "SOURCE",
             .Value = sourceInfo
            }}

            ' Let any exceptions from this bubble upwards
            Dim listLoadReply As DataListLoadReply = Nothing
            listLoadReply = proxy.DataListLoad(listLoadReq)

            Dim itemSourceColNum As Integer = 3
            Dim itemIdColNum As Integer = 5
            Dim itemResourceNameColNum As Integer = 8
            Dim itemTypeCodeColNum As Integer = 14

            ' Look for the item we're supposed to load in the datalist results
            Dim rowToLoad As DataListResultRow = listLoadReply.Rows.Where(Function(r) r.Values(itemResourceNameColNum).Equals(itemName, StringComparison.InvariantCultureIgnoreCase)).[Select](Function(r) r).FirstOrDefault()
            If rowToLoad Is Nothing Then
                Throw New Exception([String].Format("Did not find '{0}' in items loaded from '{1}'.", itemName, sourceInfo))
            End If

            Dim tmpItemTypeCode As Int32 = Int32.MinValue
            If Not Int32.TryParse(rowToLoad.Values(itemTypeCodeColNum), tmpItemTypeCode) Then
                Throw New Exception([String].Format("Failed to parse item type code from '{0}'", rowToLoad.Values(itemTypeCodeColNum)))
            End If

            itemInfo.ItemTypeCode = tmpItemTypeCode
            itemInfo.ItemId = New Guid(rowToLoad.Values(itemIdColNum))
            itemInfo.ItemName = rowToLoad.Values(itemResourceNameColNum)
            itemInfo.ItemSource = rowToLoad.Values(itemSourceColNum)

            Return itemInfo
        End Function


        Private Function LoadCatalogItem(itemToLoad As ItemInfo) As CatalogBrowserLoadCatalogItemReply
            Log.LogMessage("Calling CatalogBrowserLoadCatalogItem for '{0}'.", itemToLoad.ItemName)

            ' Build the request
            Dim loadReq As New CatalogBrowserLoadCatalogItemRequest()
            loadReq.ClientAppInfo = header
            loadReq.ItemName = itemToLoad.ItemName
            loadReq.SourceInfo = New SourceInfo() With {
             .Name = itemToLoad.ItemSource,
             .Type = SourceType.Assembly
            }
            loadReq.ForceReload = True
            loadReq.ForceReloadDependencies = False
            loadReq.TrackLoadedItems = True

            Dim loadReply As CatalogBrowserLoadCatalogItemReply = Nothing
            loadReply = proxy.CatalogBrowserLoadCatalogItem(loadReq)

            If loadReply Is Nothing Then
                Throw New Exception("Null reply from CatalogBrowserLoadCatalogItem")
            End If

            Return loadReply
        End Function

        Private Function GetPackageDependencies(itemToLoad As ItemInfo) As IEnumerable(Of String)
            Log.LogMessage("Reading package spec '{0}' (ID: {1})", itemToLoad.ItemName, itemToLoad.ItemId)

            Dim dfLoadReq As New DataFormLoadRequest()
            dfLoadReq.ClientAppInfo = header
            dfLoadReq.FormID = New Guid("8dcaab58-3b6a-405a-9430-5d07cb000d22")
            ' Catalog Item Xml View Form
            dfLoadReq.RecordID = [String].Format("5|{0}", itemToLoad.ItemId)
            ' Record ID is "[item type code]|[item ID]"
            Dim dfLoadReply As DataFormLoadReply = proxy.DataFormLoad(dfLoadReq)

            If dfLoadReply Is Nothing Then
                Throw New Exception("Null reply from DataFormLoad.")
            End If

            ' Get item XML
            Dim itemXml As String = dfLoadReply.DataFormItem.Values.Where(Function(x) x.ID = "ITEMXML").[Select](Function(x) x.Value.ToString()).FirstOrDefault()
            Dim xmlDoc As New XmlDocument()
            xmlDoc.LoadXml(itemXml)

            ' Load <Dependency> nodes, add items to load to list
            Dim nsMgr As New XmlNamespaceManager(xmlDoc.NameTable)
            nsMgr.AddNamespace("c", "bb_appfx_commontypes")

            Dim nodeList As XmlNodeList = xmlDoc.SelectNodes("//c:Dependency", nsMgr)
            If nodeList.Count <= 0 Then
                Log.LogWarning("Failed to find any Dependency nodes in package spec.")
            End If

            Dim itemsFromPackageSpec As New List(Of String)()
            For Each dependencyNode As XmlNode In nodeList
                itemsFromPackageSpec.Add(dependencyNode.Attributes("CatalogItem").Value)
            Next

            Return itemsFromPackageSpec
        End Function

    End Class
End Namespace
