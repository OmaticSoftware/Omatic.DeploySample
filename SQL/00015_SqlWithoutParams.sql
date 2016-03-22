declare @CHANGEAGENTID uniqueidentifier;
exec dbo.USP_CHANGEAGENT_GETORCREATECHANGEAGENT @CHANGEAGENTID output
 
declare @ID uniqueidentifier = '{CC1E909F-A9E2-413B-BF5D-811FBACCEDFE}';
declare @COUNTER nchar(5);
select @COUNTER = cast(count(*) as nchar(5)) from dbo.USR_DEPLOYSAMPLE;

insert into dbo.USR_DEPLOYSAMPLE (NAME, DESCRIPTION, ADDEDBYID, CHANGEDBYID) values (@COUNTER, 'sample entry', @CHANGEAGENTID, @CHANGEAGENTID);