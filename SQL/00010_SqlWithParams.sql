declare @CHANGEAGENTID uniqueidentifier;
exec dbo.USP_CHANGEAGENT_GETORCREATECHANGEAGENT @CHANGEAGENTID output
 
declare @ID uniqueidentifier = 'AFA1B559-C9C0-4A26-BC13-66CC33B85726';
if exists(select null from dbo.IMPORTSOURCE where ID = @ID)
	update dbo.IMPORTSOURCE set URLORPATH = '$(NetworkImportPath)', CHANGEDBYID = @CHANGEAGENTID where ID = @ID;
else
	insert into dbo.IMPORTSOURCE (ID, NAME, URLORPATH, IMPORTSOURCETYPECODE, ADDEDBYID, CHANGEDBYID) values (@ID, 'Import Folder', '$(NetworkImportPath)', 1, @CHANGEAGENTID, @CHANGEAGENTID);