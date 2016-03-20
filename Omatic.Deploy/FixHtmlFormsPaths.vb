Imports Microsoft.Build.Framework
Imports Microsoft.Build.Utilities
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions

Namespace Omatic.Deploy
    Public Class FixHtmlFormsPaths
        Inherits Task
        Private _fixedPaths As List(Of ITaskItem)

        <Required()>
        Public Property Paths() As ITaskItem()
            Get
                Return m_Paths
            End Get
            Set(value As ITaskItem())
                m_Paths = value
            End Set
        End Property
        Private m_Paths As ITaskItem()

        <Required()>
        Public Property DestinationFolder() As String
            Get
                Return m_DestinationFolder
            End Get
            Set(value As String)
                m_DestinationFolder = value
            End Set
        End Property
        Private m_DestinationFolder As String

        <Output()>
        Public ReadOnly Property FixedPaths() As ITaskItem()
            Get
                Return _fixedPaths.ToArray()
            End Get
        End Property

        Public Overrides Function Execute() As Boolean
            _fixedPaths = New List(Of ITaskItem)()

            Log.LogMessage("Fixing HTML forms file paths")

            Dim remove As New Regex("^.*\\htmlforms\\", RegexOptions.IgnoreCase Or RegexOptions.Compiled)
            For Each path As String In Paths.[Select](Function(x) x.ItemSpec)
                Dim newPath As String = System.IO.Path.Combine(DestinationFolder, remove.Replace(path, ""))
                Log.LogMessage(MessageImportance.Low, "Old path: ""{0}""" & vbTab & "New path: ""{1}""", path, newPath)
                _fixedPaths.Add(New TaskItem(newPath))
            Next
            Return True
        End Function
    End Class
End Namespace
