Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports Microsoft.Build.Utilities
Imports Microsoft.Build.Framework
Imports System.IO
Imports System.Data.SqlClient

Namespace Omatic.Deploy
    Public Class CheckDbRevKey
        Inherits Task

#Region "Properties"

        <Required()>
        Public Property DeployPath() As String
            Get
                Return m_DeployPath
            End Get
            Set(value As String)
                m_DeployPath = value
            End Set
        End Property
        Private m_DeployPath As String

        <Required()>
        Public Property SqlConnectString() As String
            Get
                Return m_SqlConnectString
            End Get
            Set(value As String)
                m_SqlConnectString = value
            End Set
        End Property
        Private m_SqlConnectString As String

        <Output()>
        Public Property DBRevisionsNeeded() As Boolean
            Get
                Return m_DBRevisionsNeeded
            End Get
            Private Set(value As Boolean)
                m_DBRevisionsNeeded = value
            End Set
        End Property
        Private m_DBRevisionsNeeded As Boolean

#End Region


        Public Overrides Function Execute() As Boolean
            Dim successfulRun As Boolean = True

            If [String].IsNullOrEmpty(SqlConnectString) Then
                Throw New ArgumentNullException("SqlConnectString is required")
            End If

            If [String].IsNullOrEmpty(DeployPath) Then
                Throw New ArgumentNullException("DeployPath is required")
            End If

            DBRevisionsNeeded = False

            Try
                DBRevisionsNeeded = DbRevisionsOutOfDate(DeployPath, SqlConnectString)
            Catch ex As Exception
                Log.LogErrorFromException(ex)
                successfulRun = False
            End Try

            Return successfulRun
        End Function

        Private Function DbRevisionsOutOfDate(deployPath As String, connString As String) As Boolean
            Dim revisionsNeeded As Boolean = False

            Dim filePath As String = System.IO.Path.Combine(deployPath, "bin", "dbrevkey.txt")
            Log.LogMessage("Opening {0}", filePath)

            Dim fileVersion As String = "0.0"
            Dim dbVersion As String = "0.0"

            Using sr As New StreamReader(filePath)
                Dim line As String = sr.ReadLine()
                If line IsNot Nothing Then
                    fileVersion = line
                End If
            End Using

            Using sqlConn As New SqlConnection(connString)
                sqlConn.Open()
                Dim sqlCommandText As String = "SELECT value FROM sys.extended_properties WHERE class = 0 AND name = 'DBREVKEY'"
                Using sqlCmd As New SqlCommand(sqlCommandText, sqlConn)
                    Log.LogMessage("Executing SQL: {0}", sqlCommandText)
                    Dim resultObj As [Object] = sqlCmd.ExecuteScalar()
                    If resultObj IsNot Nothing Then
                        dbVersion = TryCast(resultObj, String)
                    End If
                End Using
            End Using

            Dim compareResult As Integer = CompareRevKeys(fileVersion, dbVersion)
            If compareResult > 0 Then
                Log.LogMessage("Application greater than DB, revisions needed.")
                revisionsNeeded = True
            ElseIf compareResult = 0 Then
                Log.LogMessage("Application and DB version the same, no revisions needed.")
                revisionsNeeded = False
            ElseIf compareResult < 0 Then
                Log.LogWarning("DB version greater than application version, application needs to be updated.")
                revisionsNeeded = False
            End If

            Return revisionsNeeded
        End Function

        Private Function CompareRevKeys(appRevKey As String, dbRevKey As String) As Integer
            Log.LogMessage("Application revision: '{0}', DB Revision: '{1}'", appRevKey, dbRevKey)

            Dim appVersionMajor As Integer = 0
            Dim appVersionMinor As Integer = 0
            Dim dbVersionMajor As Integer = 0
            Dim dbVersionMinor As Integer = 0

            Dim appVersionSplit As String() = appRevKey.Split(New Char() {"."c})
            Dim dbVersionSplit As String() = dbRevKey.Split(New Char() {"."c})

            If appVersionSplit.Count() <> 2 OrElse dbVersionSplit.Count() <> 2 Then
                Throw New Exception("Couldn't parse version.")
            End If

            appVersionMajor = Int32.Parse(appVersionSplit(0))
            appVersionMinor = Int32.Parse(appVersionSplit(1))
            dbVersionMajor = Int32.Parse(dbVersionSplit(0))
            dbVersionMinor = Int32.Parse(dbVersionSplit(1))

            If appVersionMajor > dbVersionMajor Then
                Return 1
            ElseIf appVersionMajor < dbVersionMajor Then
                Return -1
            Else
                ' must be equal, look at minor version
                If appVersionMinor > dbVersionMinor Then
                    Return 1
                ElseIf appVersionMinor < dbVersionMinor Then
                    Return -1
                End If
            End If

            ' If we got here, everything must have been equal
            Return 0
        End Function


    End Class
End Namespace
