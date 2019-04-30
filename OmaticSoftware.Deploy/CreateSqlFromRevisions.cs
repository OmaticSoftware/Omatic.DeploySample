using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace Atlas.Deploy
{
    public class CreateSqlFromRevisions : Task
    {
        private string _revisionsDll;
        private string _outputFolder;
        private List<ITaskItem> _outputFiles;

        [Required()]
        public string RevisionsDll { get { return _revisionsDll; } set { _revisionsDll = value; } }

        [Required()]
        public string OutputFolder { get { return _outputFolder; } set { _outputFolder = value; } }


        [Output()]
        public ITaskItem[] OutputFiles
        {
            get { return _outputFiles.ToArray(); }
        }

        public override bool Execute()
        {
            Log.LogMessage("Creating SQL file in '{0}' from revisions file '{1}'", OutputFolder, RevisionsDll);

            bool success = true;

            FileInfo revisionsFile = null;
            try
            {
                revisionsFile = new FileInfo(RevisionsDll);
            }
            catch (Exception ex)
            {
                Log.LogErrorFromException(ex);
                success = false;
            }

            if (success)
            {
                _outputFiles = new List<ITaskItem>();

                foreach (string fileName in CreateRevisionFiles(revisionsFile, OutputFolder))
                {
                    _outputFiles.Add(new TaskItem(fileName));
                }
                Log.LogMessage("Created {0} output file(s)", _outputFiles.Count);
            }

            return success;
        }

        private List<string> CreateRevisionFiles(FileInfo revisionsDll, string outputFolder)
        {
            List<string> fileNames = new List<string>();

            Regex revLogFilenameRegex = new Regex(@"DBREV([0-9]+)\.xml", RegexOptions.IgnoreCase);

            // Iterated embedded resources
            using (SqlCommandBuilder cmdBuilder = new SqlCommandBuilder())
            {
                Assembly revisionsAssembly = Assembly.LoadFile(revisionsDll.FullName);
                foreach (string resourceName in revisionsAssembly.GetManifestResourceNames())
                {
                    Log.LogMessage("Reviewing {0}", resourceName);

                    // If it looks like a revision file, extract to a temp file
                    Match revLogFileMatch = revLogFilenameRegex.Match(resourceName);
                    if (revLogFileMatch.Success)
                    {
                        string tempFileName = Path.Combine(Path.GetTempPath(), resourceName);
                        try
                        {
                            using (var rsc = revisionsAssembly.GetManifestResourceStream(resourceName))
                            {
                                using (var tempFile = new FileStream(tempFileName, FileMode.Create, FileAccess.Write))
                                {
                                    rsc.CopyTo(tempFile);
                                }// using FileStream
                            }// using GetManifestResourceStream

                            int revisionId = Int32.Parse(revLogFileMatch.Groups[1].Value);
                            string newFileName = Path.Combine(OutputFolder, string.Format("SqlRevisions.{0}.sql", revisionId));
                            fileNames.Add(CreateSqlFileFromRevisionFile(cmdBuilder, tempFileName, newFileName, revisionId));
                        }
                        finally
                        {
                            if (File.Exists(tempFileName))
                                File.Delete(tempFileName);
                        }
                    }
                }
            }// using (SqlCommandBuilder)

            return fileNames;
        }

        private string CreateSqlFileFromRevisionFile(SqlCommandBuilder cmdBuilder, string revisionFileName, string newFileName, int revisionLogNumber)
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(revisionFileName);

            XmlNamespaceManager nsMgr = new XmlNamespaceManager(xmlDoc.NameTable);
            nsMgr.AddNamespace("x", "bb_appfx_dbrevisions");

            XmlNodeList nodeList = xmlDoc.SelectNodes("//x:DBRevision", nsMgr);

            Log.LogMessage("Found {0} revision(s) in {1}", nodeList.Count, (new FileInfo(revisionFileName)).Name);

            using (StreamWriter sw = new StreamWriter(newFileName))
            {
                // Set up Log Table
                sw.Write(GetCreateRevisionLogTableCommand(cmdBuilder, revisionLogNumber));
                foreach (XmlNode node in nodeList)
                {
                    string idAttr = node.Attributes["ID"].Value;
                    int id = 0;
                    if (!Int32.TryParse(idAttr, out id))
                    {
                        Log.LogError("Failed to parse ID from {0}", idAttr);
                        continue;
                    }


                    foreach (XmlNode childNode in node.ChildNodes)
                    {
                        if (string.Equals(childNode.Name, "ExecuteSql", StringComparison.OrdinalIgnoreCase))
                        {
                            sw.Write(GetRevisionStringForDBRevision(cmdBuilder, revisionFileName, revisionLogNumber, childNode.InnerText, id));
                            break;
                        }
                    }

                }

                // Update RevisionRunLog
                sw.Write(GetStringForRevisionRunLogUpdate(revisionLogNumber, GetRevisionLogTableName(cmdBuilder, revisionLogNumber), nodeList.Count));

            }

            return newFileName;
        }

        private static string GetRevisionStringForDBRevision(SqlCommandBuilder cmdBuilder, string revisionFileName, int revisionLogNumber, string dbRevision, int id)
        {
            string sqlString = string.Empty;
            string revText = string.Empty;

            if (!string.IsNullOrEmpty(dbRevision))
            {
                revText = dbRevision.Trim();
                if (revText.Length > 100)
                    revText = dbRevision.Substring(0, 100);

                revText = revText.Replace("'", "''").Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim();
            }

            sqlString = string.Format(@"
-- Begin revision ID {0}

    -- If there was an error before then skip the rest
    if @@ERROR != 0 set noexec on

    declare @REVISIONID int = {0};

    if ((select ISNULL(max(ID),0) from dbo.{2}) < @REVISIONID)
    begin
        -- Begin revision contents

{1}

        -- End revision contents

        insert into dbo.{2} (ASSEMBLY, ID, WINUSER, REVTEXT) values ('{4}', {0}, suser_sname(), '{3}')

    end

-- End revision ID {0}

GO

", id, dbRevision, GetRevisionLogTableName(cmdBuilder, revisionLogNumber), revText, Path.GetFileName(revisionFileName), revisionLogNumber);


            return sqlString;
        }
        private static string GetStringForRevisionRunLogUpdate(int revisionLogNumber, string revisionTableName, int totalRevisions)
        {
            string sqlString = string.Empty;

            sqlString = String.Format(@"
-- UPDATE REVISIONRUNLOG

set noexec off

-- Get new rows (if there are any)
IF EXISTS (SELECT * FROM dbo.{1} WHERE [NEW] = 1)
BEGIN

	DECLARE @FIRSTREVISIONID int
	DECLARE @CURRENTREVISIONID int

	DECLARE @ASSEMBLY nvarchar(260)
	DECLARE @WINUSER nvarchar(256)
	DECLARE @DATEFINISHED datetime
	DECLARE @CURRENTREVISION nvarchar(max)

	DECLARE @DATESTARTED datetime = GETDATE()

	DECLARE @REVISIONNUM int = {0} -- FILLED IN DURING FILE GENERATION
	DECLARE @TOTALREVISIONS int = {2} -- FILLED IN DURING FILE GENERATION

	-- Get the first and last revision ids
	SELECT @FIRSTREVISIONID = MIN(ID), @CURRENTREVISIONID = MAX(ID) FROM dbo.{1} WHERE [NEW] = 1

	-- Get info about the last revision
	SELECT 
		@ASSEMBLY = [ASSEMBLY],
		@DATEFINISHED = [DATEADDED],
		@WINUSER = [WINUSER],
		@CURRENTREVISION = [REVTEXT]
	FROM dbo.{1}
	WHERE [ID] = @CURRENTREVISIONID

	-- Get info about the first revision
	SELECT @DATESTARTED = [DATEADDED] FROM dbo.{1} WHERE [ID] = @FIRSTREVISIONID

	-- Insert new row in REVISIONRUNLOG if needed
	IF NOT EXISTS ( SELECT * FROM [dbo].[REVISIONRUNLOG] WHERE [STARTREVISIONNUM] = @REVISIONNUM )
		INSERT INTO [dbo].[REVISIONRUNLOG] (
				[ID],
				[STARTASSEMBLY],
				[STARTREVISIONNUM],
				[STARTREVISIONID],
				[TOTALREVISIONS],
				[WINUSER]
				) 
	        VALUES (
				NEWID(),
				@ASSEMBLY,
				@REVISIONNUM,
				@FIRSTREVISIONID,
				@TOTALREVISIONS,
				@WINUSER
				)

	-- At this point the row exists so update it
	UPDATE [dbo].[REVISIONRUNLOG]
		SET [DATESTARTED] = @DATESTARTED
			,[DATEFINISHED] = @DATEFINISHED
			,[CURRENTASSEMBLY] = @ASSEMBLY
			,[CURRENTREVISIONNUM] = @REVISIONNUM
			,[CURRENTREVISIONID] = @CURRENTREVISIONID
		    ,[CURRENTREVISION] = @CURRENTREVISION
			,[TOTALREVISIONS] = @TOTALREVISIONS
			,[TOTALREVISIONSCOMPLETED] = (SELECT COUNT(*) FROM dbo.{1})
			,[DATELASTUPDATED] = GETDATE()
			,[WINUSER] = @WINUSER
		WHERE [STARTREVISIONNUM] = @REVISIONNUM

	-- Reset the NEW flag
	UPDATE dbo.{1} SET [NEW] = 0

END

GO

", revisionLogNumber, revisionTableName, totalRevisions);

            return sqlString;
        }

        private static string GetRevisionLogTableName(SqlCommandBuilder sqlCmdBuilder, int revisionLogNumber)
        {
            return sqlCmdBuilder.QuoteIdentifier(string.Format("REVISIONLOG{0}", revisionLogNumber));
        }

        private static string GetCreateRevisionLogTableCommand(SqlCommandBuilder sqlCmdBuilder, int revisionLogNumber)
        {
            string cmd = string.Empty;
            string tableName = GetRevisionLogTableName(sqlCmdBuilder, revisionLogNumber);
            string indexName = string.Empty;
            string dateAddedConstraint = string.Empty;
            string dbUserConstraint = string.Empty;
            string sysUserConstraint = string.Empty;
            string newConstraint = string.Empty;

            indexName = sqlCmdBuilder.QuoteIdentifier(string.Format("PK_REVISIONLOG{0}", revisionLogNumber));
            dateAddedConstraint = sqlCmdBuilder.QuoteIdentifier(string.Format("DF_REVISIONLOG{0}_DATEADDED", revisionLogNumber));
            dbUserConstraint = sqlCmdBuilder.QuoteIdentifier(string.Format("DF_REVISIONLOG{0}_DBUSER", revisionLogNumber));
            sysUserConstraint = sqlCmdBuilder.QuoteIdentifier(string.Format("DF_REVISIONLOG{0}_SYSUSER", revisionLogNumber));
            newConstraint = sqlCmdBuilder.QuoteIdentifier(string.Format("DF_REVISIONLOG{0}_NEW", revisionLogNumber));

            cmd = string.Format(@"
set xact_abort on; -- Make sure transactions are rolled back in case of error
set noexec off; -- Used to stop execution in case of error

-- Create revision log table if it doesn't already exist
IF (OBJECT_ID('dbo.{0}', N'U') is null)
BEGIN
    CREATE TABLE [dbo].{0}(
	    [ASSEMBLY] [nvarchar](260) NOT NULL,
	    [ID] [int] NOT NULL,
	    [DATEADDED] [datetime] NOT NULL,
	    [DBUSER] [nvarchar](255) NOT NULL,
	    [SYSUSER] [nvarchar](255) NOT NULL,
	    [WINUSER] [nvarchar](255) NOT NULL,
	    [REVTEXT] [nvarchar](max) NULL,
        [NEW] [bit] NOT NULL,
     CONSTRAINT {1} PRIMARY KEY CLUSTERED 
    (
	    [ASSEMBLY] ASC,
	    [ID] ASC
    )WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [DEFGROUP]
    ) ON [DEFGROUP] TEXTIMAGE_ON [DEFGROUP]

    ALTER TABLE [dbo].{0} ADD  CONSTRAINT {2}  DEFAULT (getdate()) FOR [DATEADDED]

    ALTER TABLE [dbo].{0} ADD  CONSTRAINT {3}  DEFAULT (user_name()) FOR [DBUSER]

    ALTER TABLE [dbo].{0} ADD  CONSTRAINT {4}  DEFAULT (suser_sname()) FOR [SYSUSER]

    ALTER TABLE [dbo].{0} ADD  CONSTRAINT {5}  DEFAULT (1) FOR [NEW]
END

GO

", tableName, indexName, dateAddedConstraint, dbUserConstraint, sysUserConstraint, newConstraint);

            return cmd;
        }
    }
}
