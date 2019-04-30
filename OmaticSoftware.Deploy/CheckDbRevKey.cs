using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Data.SqlClient;
using System.IO;
using System.Linq;

namespace OmaticSoftware.Deploy
{
  public class CheckDbRevKey : Task
  {

    #region "Properties"

    [Required()]
    public string DeployPath { get; set; }

    [Required()]
    public string SqlConnectString { get; set; }

    [Output()]
    public bool DBRevisionsNeeded { get; set; }

    #endregion


    public override bool Execute()
    {
      bool successfulRun = true;

      if (String.IsNullOrEmpty(SqlConnectString))
      {
        throw new ArgumentNullException("SqlConnectString is required");
      }

      if (String.IsNullOrEmpty(DeployPath))
      {
        throw new ArgumentNullException("DeployPath is required");
      }

      DBRevisionsNeeded = false;

      try
      {
        DBRevisionsNeeded = DbRevisionsOutOfDate(DeployPath, SqlConnectString);
      }
      catch (Exception ex)
      {
        Log.LogErrorFromException(ex);
        successfulRun = false;
      }

      return successfulRun;
    }

    private bool DbRevisionsOutOfDate(string deployPath, string connString)
    {
      bool revisionsNeeded = false;

      string filePath = System.IO.Path.Combine(deployPath, "bin", "dbrevkey.txt");
      Log.LogMessage("Opening {0}", filePath);

      string fileVersion = "0.0";
      string dbVersion = "0.0";

      using (StreamReader sr = new StreamReader(filePath))
      {
        string line = sr.ReadLine();
        if (line != null)
        {
          fileVersion = line;
        }
      }

      using (SqlConnection sqlConn = new SqlConnection(connString))
      {
        sqlConn.Open();
        string sqlCommandText = "SELECT value FROM sys.extended_properties WHERE class = 0 AND name = 'DBREVKEY'";
        using (SqlCommand sqlCmd = new SqlCommand(sqlCommandText, sqlConn))
        {
          Log.LogMessage("Executing SQL: {0}", sqlCommandText);
          Object resultObj = sqlCmd.ExecuteScalar();
          if (resultObj != null)
          {
            dbVersion = resultObj as string;
          }
        }
      }

      int compareResult = CompareRevKeys(fileVersion, dbVersion);
      if (compareResult > 0)
      {
        Log.LogMessage("Application greater than DB, revisions needed.");
        revisionsNeeded = true;
      }
      else if (compareResult == 0)
      {
        Log.LogMessage("Application and DB version the same, no revisions needed.");
        revisionsNeeded = false;
      }
      else if (compareResult < 0)
      {
        Log.LogWarning("DB version greater than application version, application needs to be updated.");
        revisionsNeeded = false;
      }

      return revisionsNeeded;
    }

    private int CompareRevKeys(string appRevKey, string dbRevKey)
    {
      Log.LogMessage("Application revision: '{0}', DB Revision: '{1}'", appRevKey, dbRevKey);

      int appVersionMajor = 0;
      int appVersionMinor = 0;
      int dbVersionMajor = 0;
      int dbVersionMinor = 0;

      string[] appVersionSplit = appRevKey.Split(new char[] { '.' });
      string[] dbVersionSplit = dbRevKey.Split(new char[] { '.' });

      if (appVersionSplit.Count() != 2 || dbVersionSplit.Count() != 2)
      {
        throw new Exception("Couldn't parse version.");
      }

      appVersionMajor = Int32.Parse(appVersionSplit[0]);
      appVersionMinor = Int32.Parse(appVersionSplit[1]);
      dbVersionMajor = Int32.Parse(dbVersionSplit[0]);
      dbVersionMinor = Int32.Parse(dbVersionSplit[1]);

      if (appVersionMajor > dbVersionMajor)
      {
        return 1;
      }
      else if (appVersionMajor < dbVersionMajor)
      {
        return -1;
      }
      else
      {
        // must be equal, look at minor version
        if (appVersionMinor > dbVersionMinor)
        {
          return 1;
        }
        else if (appVersionMinor < dbVersionMinor)
        {
          return -1;
        }
      }

      // If we got here, everything must have been equal
      return 0;
    }


  }
}
