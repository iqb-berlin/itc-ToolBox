﻿Imports Newtonsoft.Json
Imports DocumentFormat.OpenXml
Imports DocumentFormat.OpenXml.Spreadsheet
Imports DocumentFormat.OpenXml.Packaging
Imports iqb.lib.openxml
Imports System.ComponentModel

Public Class OutputResultPage
    Private WithEvents myBackgroundWorker As BackgroundWorker = Nothing

    Private Sub Me_Loaded() Handles Me.Loaded
        Me.MBUC.AddMessage("Bitte warten!")
        Me.BtnCancelClose.IsEnabled = True
        Me.BtnCancelClose.Content = "Abbrechen"

        myBackgroundWorker = New ComponentModel.BackgroundWorker With {.WorkerReportsProgress = True, .WorkerSupportsCancellation = True}
        myBackgroundWorker.RunWorkerAsync()
    End Sub
    Private Sub myBackgroundWorker_RunWorkerCompleted(ByVal sender As Object, ByVal e As RunWorkerCompletedEventArgs) Handles myBackgroundWorker.RunWorkerCompleted
        Me.BtnCancelClose.IsEnabled = True
        Me.APBUC.UpdateProgressState(0)
        Me.MBUC.AddMessage("i: Beendet.")
        BtnCancelClose.IsEnabled = True
        BtnCancelClose.Content = "Schließen"
    End Sub

    Private Sub BtnCancelClose_Click(sender As System.Object, e As System.Windows.RoutedEventArgs)
        If myBackgroundWorker Is Nothing Then
            Dim parentDlg As OutputDialog = Me.Parent
            parentDlg.DialogResult = True
        Else
            If myBackgroundWorker.WorkerSupportsCancellation AndAlso myBackgroundWorker.IsBusy Then
                myBackgroundWorker.CancelAsync()
                BtnCancelClose.IsEnabled = False
                BtnCancelClose.Content = "Bitte warten"
                Me.MBUC.AddMessage("w: Abbruch - bitte warten!")
            Else
                Dim parentDlg As OutputDialog = Me.Parent
                parentDlg.DialogResult = True
            End If
        End If
    End Sub

    Private Sub myBackgroundWorker_ProgressChanged(ByVal sender As Object, ByVal e As ProgressChangedEventArgs) Handles myBackgroundWorker.ProgressChanged
        Me.APBUC.UpdateProgressState(e.ProgressPercentage)
        If Not String.IsNullOrEmpty(e.UserState) Then Me.MBUC.AddMessage(e.UserState)
    End Sub

    Private Sub myBackgroundWorker_DoWork(ByVal sender As Object, ByVal e As DoWorkEventArgs) Handles myBackgroundWorker.DoWork
        Dim myworker As ComponentModel.BackgroundWorker = sender
        Dim parentDlg As OutputDialog = Me.Parent

        Dim targetXlsxFilename As String = My.Settings.lastfile_OutputTargetXlsx
        Dim myTemplate As Byte() = Nothing
        Try
            Dim TmpZielXLS As SpreadsheetDocument = SpreadsheetDocument.Create(targetXlsxFilename, SpreadsheetDocumentType.Workbook)
            Dim myWorkbookPart As WorkbookPart = TmpZielXLS.AddWorkbookPart()
            myWorkbookPart.Workbook = New Workbook()
            myWorkbookPart.Workbook.AppendChild(Of Sheets)(New Sheets())
            TmpZielXLS.Close()

            myTemplate = IO.File.ReadAllBytes(targetXlsxFilename)
        Catch ex As Exception
            myworker.ReportProgress(0.0#, "e: Konnte Datei '" + targetXlsxFilename + "' nicht schreiben (noch geöffnet?)" + vbNewLine + ex.Message)
        End Try

        If myTemplate IsNot Nothing Then
            Dim myTestPersonList As New TestPersonList
            Dim Events As New List(Of String)
            Dim AllData As New SortedDictionary(Of String, Dictionary(Of String, List(Of ResponseEntry))) 'id -> booklet -> entries
            Dim AllVariables As New List(Of String)
            Dim AllUnitsWithResponses As New List(Of String)
            Dim LogEntryCount As Long = 0

            'Dim LogData As New Dictionary(Of String, Dictionary(Of String, Long))
            Dim SearchDir As New IO.DirectoryInfo(My.Settings.lastdir_OutputSource)
            For Each fi As IO.FileInfo In SearchDir.GetFiles("*.csv", IO.SearchOption.AllDirectories)
                If myworker.CancellationPending Then
                    e.Cancel = True
                    Exit For
                End If

                Dim allLines As String()
                Try
                    allLines = IO.File.ReadAllLines(fi.FullName)
                Catch ex As Exception
                    allLines = Nothing
                    myworker.ReportProgress(0.0#, "e:Fehler mein Lesen von " + fi.Name + "; noch geöffnet?")
                End Try
                If allLines IsNot Nothing Then
                    myworker.ReportProgress(0.0#, "Lese " + fi.Name)
                    If allLines.First = OutputDialog.LogFileFirstLine Then
                        '#########################
                        Dim isFirstLine As Boolean = True
                        For Each line As String In allLines
                            If isFirstLine Then
                                isFirstLine = False
                            Else
                                Dim lineSplits As String() = line.Split({""";"}, StringSplitOptions.RemoveEmptyEntries)
                                If lineSplits.Count = 7 Then
                                    LogEntryCount += 1
                                    Dim group As String = lineSplits(0).Substring(1)
                                    Dim login As String = lineSplits(1).Substring(1)
                                    Dim code As String = lineSplits(2).Substring(1)
                                    Dim booklet As String = lineSplits(3).Substring(1).ToUpper()
                                    Dim unit As String = lineSplits(4)
                                    If unit.Length < 2 Then
                                        unit = ""
                                    Else
                                        unit = unit.Substring(1)
                                    End If
                                    Dim timestampStr As String = lineSplits(5).Substring(1)
                                    Dim timestampInt As Long = Long.Parse(timestampStr)
                                    Dim entry As String = lineSplits(6)
                                    Dim key As String = entry
                                    Dim parameter As String = ""
                                    If key.IndexOf(" : ") > 0 Then
                                        parameter = key.Substring(key.IndexOf(" : ") + 3)
                                        If parameter.IndexOf("""") = 0 AndAlso parameter.LastIndexOf("""") = parameter.Length - 1 Then
                                            parameter = parameter.Substring(1, parameter.Length - 2)
                                            parameter = parameter.Replace("""""", """")
                                            parameter = parameter.Replace("\\", "\")
                                        End If
                                        key = key.Substring(0, key.IndexOf(" : "))
                                    ElseIf key.IndexOf(" = ") > 0 Then
                                        parameter = key.Substring(key.IndexOf(" = ") + 3)
                                        key = key.Substring(0, key.IndexOf(" = "))
                                    End If

                                    Select Case key
                                        Case "LOADCOMPLETE"
                                            Dim sysdata As Dictionary(Of String, String) = Nothing
                                            Try
                                                sysdata = JsonConvert.DeserializeObject(parameter, GetType(Dictionary(Of String, String)))
                                            Catch ex As Exception
                                                sysdata = Nothing
                                                Debug.Print("sysdata json convert failed: " + ex.Message)
                                            End Try
                                            myTestPersonList.SetSysdata(group, login, code, booklet, sysdata)
                                            myTestPersonList.AddLogEvent(group, login, code, booklet, timestampInt, "#BOOKLET#", key, parameter)

                                        Case "BOOKLETLOADSTART"
                                            Dim parameterClean As String = parameter.Replace("""""", """")
                                            parameterClean = parameterClean.Replace("\\", "\")
                                            Dim sysdata As Dictionary(Of String, String) = Nothing
                                            Try
                                                sysdata = JsonConvert.DeserializeObject(parameterClean, GetType(Dictionary(Of String, String)))
                                            Catch ex As Exception
                                                sysdata = Nothing
                                                Debug.Print("sysdata json convert failed: " + ex.Message)
                                            End Try
                                            myTestPersonList.SetSysdata(group, login, code, booklet, sysdata)
                                            myTestPersonList.AddLogEvent(group, login, code, booklet, timestampInt, "#BOOKLET#", key, parameter)
                                        Case "RESPONSESCOMPLETE", "PRESENTATIONCOMPLETE"
                                            myTestPersonList.AddLogEvent(group, login, code, booklet, timestampInt, unit, key, parameter)
                                        Case "UNITENTER"
                                            myTestPersonList.SetFirstUnitEnter(group, login, code, booklet, timestampInt)
                                            myTestPersonList.AddLogEvent(group, login, code, booklet, timestampInt, unit, key, parameter)
                                        Case Else
                                            myTestPersonList.AddLogEvent(group, login, code, booklet, timestampInt, unit, key, parameter)
                                    End Select
                                End If
                            End If
                        Next
                    ElseIf allLines.First = OutputDialog.ResponsesFileFirstLine Then
                        '#########################
                        Dim lineCount As Integer = 1
                        Dim isFirstLine As Boolean = True
                        For Each line As String In allLines
                            If isFirstLine Then
                                isFirstLine = False
                            Else
                                lineCount += 1
                                For Each entry As ResponseEntry In ResponseEntry.getResponseEntriesFromLine(line, "file '" + fi.Name + "', line " + lineCount.ToString(), parentDlg.outputConfig.variables)
                                    If entry.data.Count > 0 AndAlso (parentDlg.outputConfig.omitUnits Is Nothing OrElse Not parentDlg.outputConfig.omitUnits.Contains(entry.unit)) Then
                                        For Each d As KeyValuePair(Of String, String) In entry.data
                                            If Not AllUnitsWithResponses.Contains(entry.unit) Then AllUnitsWithResponses.Add(entry.unit)
                                            If Not AllVariables.Contains(entry.unit + "##" + d.Key) Then AllVariables.Add(entry.unit + "##" + d.Key)
                                        Next

                                        If Not AllData.ContainsKey(entry.Key) Then AllData.Add(entry.Key, New Dictionary(Of String, List(Of ResponseEntry)))
                                        Dim myPerson As Dictionary(Of String, List(Of ResponseEntry)) = AllData.Item(entry.Key)
                                        If Not myPerson.ContainsKey(entry.booklet) Then myPerson.Add(entry.booklet, New List(Of ResponseEntry))
                                        myPerson.Item(entry.booklet).Add(entry)
                                    End If
                                Next
                            End If
                        Next
                    End If
                End If
            Next

            myworker.ReportProgress(0.0#, "Daten für " + AllData.Count.ToString("#,##0") + " Testpersonen und " + AllVariables.Count.ToString("#,##0") + " Variablen gelesen.")
            myworker.ReportProgress(0.0#, LogEntryCount.ToString("#,##0") + " Log-Einträge gelesen.")


            If Not myworker.CancellationPending Then

                Using MemStream As New IO.MemoryStream()
                    MemStream.Write(myTemplate, 0, myTemplate.Length)
                    Using ZielXLS As SpreadsheetDocument = SpreadsheetDocument.Open(MemStream, True)
                        Dim myStyles As ExcelStyleDefs = xlsxFactory.AddIQBStandardStyles(ZielXLS.WorkbookPart)
                        '########################################################
                        'Responses
                        '########################################################
                        Dim TableResponses As WorksheetPart = xlsxFactory.InsertWorksheet(ZielXLS.WorkbookPart, "Responses")
                        myworker.ReportProgress(0.0#, "Schreibe Daten")

                        Dim myRow As Integer = 1
                        xlsxFactory.SetCellValueString("A", myRow, TableResponses, "ID", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("A", TableResponses, 20)
                        xlsxFactory.SetCellValueString("B", myRow, TableResponses, "Group", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("B", TableResponses, 10)
                        xlsxFactory.SetCellValueString("C", myRow, TableResponses, "Login+Code", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("C", TableResponses, 10)
                        xlsxFactory.SetCellValueString("D", myRow, TableResponses, "Booklet", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("D", TableResponses, 10)
                        Dim myColumn As String = "E"
                        Dim Columns As New Dictionary(Of String, String)

                        Dim progressMax As Integer = AllVariables.Count
                        Dim progressCount As Integer = 1
                        Dim stepMax As Integer = 5
                        Dim stepCount As Integer = 1
                        Dim progressValue As Double = 0.0#

                        For Each s As String In From v As String In AllVariables Order By v Select v
                            progressValue = progressCount * (100 / stepMax) / progressMax + (100 / stepMax) * (stepCount - 1)
                            myworker.ReportProgress(progressValue, "")
                            progressCount += 1
                            xlsxFactory.SetCellValueString(myColumn, myRow, TableResponses, s, CellFormatting.RowHeader2, myStyles)
                            xlsxFactory.SetColumnWidth(myColumn, TableResponses, 10)
                            Columns.Add(s, myColumn)
                            myColumn = xlsxFactory.GetNextColumn(myColumn)
                        Next

                        Dim BookletUnits As New Dictionary(Of String, List(Of String)) 'für unten TechTable

                        progressMax = AllData.Count
                        progressCount = 1
                        stepCount += 1
                        For Each persondata As KeyValuePair(Of String, Dictionary(Of String, List(Of ResponseEntry))) In AllData
                            If myworker.CancellationPending Then
                                e.Cancel = True
                                Exit For
                            End If
                            progressValue = progressCount * (100 / stepMax) / progressMax + (100 / stepMax) * (stepCount - 1)
                            myworker.ReportProgress(progressValue, "")
                            progressCount += 1
                            For Each bookletdata As KeyValuePair(Of String, List(Of ResponseEntry)) In persondata.Value
                                Dim resp As ResponseEntry = bookletdata.Value.FirstOrDefault
                                If resp IsNot Nothing Then
                                    myRow += 1
                                    Dim myRowData As New List(Of RowData)
                                    myRowData.Add(New RowData With {.Column = "A", .Value = persondata.Key + bookletdata.Key, .CellType = CellTypes.str})
                                    myRowData.Add(New RowData With {.Column = "B", .Value = resp.group, .CellType = CellTypes.str})
                                    myRowData.Add(New RowData With {.Column = "C", .Value = resp.login + resp.code, .CellType = CellTypes.str})
                                    myRowData.Add(New RowData With {.Column = "D", .Value = bookletdata.Key, .CellType = CellTypes.str})
                                    For Each u As ResponseEntry In bookletdata.Value
                                        If Not BookletUnits.ContainsKey(bookletdata.Key) Then BookletUnits.Add(bookletdata.Key, New List(Of String))
                                        If Not BookletUnits.Item(bookletdata.Key).Contains(u.unit) Then BookletUnits.Item(bookletdata.Key).Add(u.unit)
                                        For Each d As KeyValuePair(Of String, String) In u.data
                                            myRowData.Add(New RowData With {.Column = Columns.Item(u.unit + "##" + d.Key), .Value = d.Value, .CellType = CellTypes.str})
                                        Next
                                    Next
                                    xlsxFactory.AppendRow(myRow, myRowData, TableResponses)
                                End If
                            Next
                        Next


                        '########################################################
                        'TimeOnPage
                        '########################################################
                        progressMax = myTestPersonList.Count
                        progressCount = 1
                        stepCount += 1
                        Dim TableTimeOnPage As WorksheetPart = xlsxFactory.InsertWorksheet(ZielXLS.WorkbookPart, "TimeOnPage")
                        myRow = 1
                        xlsxFactory.SetCellValueString("A", myRow, TableTimeOnPage, "ID", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("A", TableTimeOnPage, 20)

                        Dim AllTimeVariables As New List(Of String)
                        Dim AllTimeOnPage As New Dictionary(Of String, List(Of TimeOnPage))
                        Dim BookletMaxVisitedPagesCount As New Dictionary(Of String, Integer) 'für unten TechTable
                        Dim TesteeBookletVisitedPagesCount As New Dictionary(Of String, Integer) 'für unten TechTable
                        Dim TesteeBookletRespondedUnitsCount As New Dictionary(Of String, Integer) 'für unten TechTable
                        For Each tc As KeyValuePair(Of String, TestPerson) In myTestPersonList
                            If myworker.CancellationPending Then
                                e.Cancel = True
                                Exit For
                            End If
                            progressValue = progressCount * (100 / stepMax) / progressMax + (100 / stepMax) * (stepCount - 1)
                            myworker.ReportProgress(progressValue, "")
                            progressCount += 1
                            If Not AllTimeOnPage.ContainsKey(tc.Key) Then
                                Dim myTimeOnPageList As List(Of TimeOnPage) = tc.Value.GetTimeOnPageList(AllUnitsWithResponses)
                                AllTimeOnPage.Add(tc.Key, myTimeOnPageList)

                                TesteeBookletVisitedPagesCount.Add(tc.Key, myTimeOnPageList.Count)
                                If BookletMaxVisitedPagesCount.ContainsKey(tc.Value.booklet) Then
                                    If BookletMaxVisitedPagesCount.Item(tc.Value.booklet) < myTimeOnPageList.Count Then BookletMaxVisitedPagesCount.Item(tc.Value.booklet) = myTimeOnPageList.Count
                                Else
                                    BookletMaxVisitedPagesCount.Add(tc.Value.booklet, myTimeOnPageList.Count)
                                End If

                                TesteeBookletRespondedUnitsCount.Add(tc.Key, tc.Value.GetResponsesCompleteAllUnitCount(AllUnitsWithResponses))

                                For Each p As TimeOnPage In myTimeOnPageList
                                    If Not AllTimeVariables.Contains(p.page) Then AllTimeVariables.Add(p.page)
                                Next
                            End If
                        Next

                        myColumn = "B"
                        Columns.Clear()
                        For Each s As String In From v As String In AllTimeVariables Order By v
                            xlsxFactory.SetCellValueString(myColumn, myRow, TableTimeOnPage, s + "##topTotal", CellFormatting.RowHeader2, myStyles)
                            xlsxFactory.SetColumnWidth(myColumn, TableTimeOnPage, 10)
                            Columns.Add(s, myColumn)
                            myColumn = xlsxFactory.GetNextColumn(myColumn)
                            xlsxFactory.SetCellValueString(myColumn, myRow, TableTimeOnPage, s + "##topCount", CellFormatting.RowHeader2, myStyles)
                            xlsxFactory.SetColumnWidth(myColumn, TableTimeOnPage, 10)
                            myColumn = xlsxFactory.GetNextColumn(myColumn)
                        Next

                        progressMax = AllTimeOnPage.Count
                        progressCount = 1
                        stepCount += 1
                        For Each topList As KeyValuePair(Of String, List(Of TimeOnPage)) In From top As KeyValuePair(Of String, List(Of TimeOnPage)) In AllTimeOnPage Order By top.Key
                            If myworker.CancellationPending Then
                                e.Cancel = True
                                Exit For
                            End If
                            progressValue = progressCount * (100 / stepMax) / progressMax + (100 / stepMax) * (stepCount - 1)
                            myworker.ReportProgress(progressValue, "")
                            progressCount += 1

                            myRow += 1
                            Dim myRowData As New List(Of RowData)
                            myRowData.Add(New RowData With {.Column = "A", .Value = topList.Key, .CellType = CellTypes.str})
                            For Each top As TimeOnPage In topList.Value
                                myRowData.Add(New RowData With {.Column = Columns.Item(top.page), .Value = top.millisec, .CellType = CellTypes.int})
                                myRowData.Add(New RowData With {.Column = xlsxFactory.GetNextColumn(Columns.Item(top.page)), .Value = top.count, .CellType = CellTypes.int})
                            Next
                            xlsxFactory.AppendRow(myRow, myRowData, TableTimeOnPage)
                        Next



                        '########################################################
                        'TechData
                        '########################################################
                        Dim TableTechData As WorksheetPart = xlsxFactory.InsertWorksheet(ZielXLS.WorkbookPart, "TechData")
                        Dim currentUser As System.Security.Principal.WindowsIdentity = System.Security.Principal.WindowsIdentity.GetCurrent
                        Dim currentUserName As String = currentUser.Name.Substring(currentUser.Name.IndexOf("\") + 1)

                        xlsxFactory.SetCellValueString("A", 1, TableTechData, "Antworten und Log-Daten IQB-Testcenter", CellFormatting.Null, myStyles)
                        xlsxFactory.SetCellValueString("A", 2, TableTechData, "konvertiert mit " + My.Application.Info.AssemblyName + " V" +
                                                       My.Application.Info.Version.Major.ToString + "." + My.Application.Info.Version.Minor.ToString + "." +
                                                       My.Application.Info.Version.Build.ToString + " am " + DateTime.Now.ToShortDateString + " " + DateTime.Now.ToShortTimeString +
                                                       " (" + currentUserName + ")", CellFormatting.Null, myStyles)

                        myRow = 4

                        xlsxFactory.SetCellValueString("A", myRow, TableTechData, "ID", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("A", TableTechData, 30)
                        xlsxFactory.SetCellValueString("B", myRow, TableTechData, "Start at", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("B", TableTechData, 20)
                        xlsxFactory.SetCellValueString("C", myRow, TableTechData, "loadcomplete after", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("C", TableTechData, 20)
                        xlsxFactory.SetCellValueString("D", myRow, TableTechData, "loadspeed", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("D", TableTechData, 20)
                        xlsxFactory.SetCellValueString("E", myRow, TableTechData, "firstUnitEnter after", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("E", TableTechData, 20)
                        xlsxFactory.SetCellValueString("F", myRow, TableTechData, "os", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("F", TableTechData, 20)
                        xlsxFactory.SetCellValueString("G", myRow, TableTechData, "browser", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("G", TableTechData, 20)
                        xlsxFactory.SetCellValueString("H", myRow, TableTechData, "screen", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("H", TableTechData, 20)
                        xlsxFactory.SetCellValueString("I", myRow, TableTechData, "pages visited ratio", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("I", TableTechData, 20)
                        xlsxFactory.SetCellValueString("J", myRow, TableTechData, "units fully responded ratio", CellFormatting.RowHeader2, myStyles)
                        xlsxFactory.SetColumnWidth("J", TableTechData, 20)


                        progressMax = myTestPersonList.Count
                        progressCount = 1
                        stepCount += 1
                        For Each tc As KeyValuePair(Of String, TestPerson) In myTestPersonList
                            If myworker.CancellationPending Then
                                e.Cancel = True
                                Exit For
                            End If
                            progressValue = progressCount * (100 / stepMax) / progressMax + (100 / stepMax) * (stepCount - 1)
                            myworker.ReportProgress(progressValue, "")
                            progressCount += 1

                            myRow += 1
                            Dim myRowData As New List(Of RowData)
                            myRowData.Add(New RowData With {.Column = "A", .Value = tc.Key, .CellType = CellTypes.str})
                            myRowData.Add(New RowData With {.Column = "B", .Value = 0, .CellType = CellTypes.int})
                            myRowData.Add(New RowData With {.Column = "C", .Value = tc.Value.loadtime, .CellType = CellTypes.int})
                            myRowData.Add(New RowData With {.Column = "D", .Value = tc.Value.loadspeed(parentDlg.outputConfig.bookletSizes).ToString(), .CellType = CellTypes.dec})
                            myRowData.Add(New RowData With {.Column = "E", .Value = 0, .CellType = CellTypes.int})
                            myRowData.Add(New RowData With {.Column = "F", .Value = tc.Value.os, .CellType = CellTypes.str})
                            myRowData.Add(New RowData With {.Column = "G", .Value = tc.Value.browser, .CellType = CellTypes.str})
                            myRowData.Add(New RowData With {.Column = "H", .Value = tc.Value.screen, .CellType = CellTypes.str})

                            Dim myRatio As Double = 0.0#
                            If TesteeBookletVisitedPagesCount.ContainsKey(tc.Key) AndAlso BookletMaxVisitedPagesCount.ContainsKey(tc.Value.booklet) Then
                                Dim bmvpc As Integer = BookletMaxVisitedPagesCount.Item(tc.Value.booklet)
                                If bmvpc > 0 Then myRatio = TesteeBookletVisitedPagesCount.Item(tc.Key) * 100 / bmvpc
                            End If
                            myRowData.Add(New RowData With {.Column = "I", .Value = myRatio.ToString(), .CellType = CellTypes.dec})

                            myRatio = 0.0#
                            If TesteeBookletRespondedUnitsCount.ContainsKey(tc.Key) AndAlso BookletUnits.ContainsKey(tc.Value.booklet) Then
                                Dim buc As Integer = BookletUnits.Item(tc.Value.booklet).Count
                                If buc > 0 Then myRatio = TesteeBookletRespondedUnitsCount.Item(tc.Key) * 100 / buc
                            End If
                            myRowData.Add(New RowData With {.Column = "J", .Value = myRatio.ToString(), .CellType = CellTypes.dec})

                            xlsxFactory.AppendRow(myRow, myRowData, TableTechData)
                        Next


                    End Using
                    myworker.ReportProgress(100.0#, "Speichern Datei")
                    Try
                        Using fs As New IO.FileStream(targetXlsxFilename, IO.FileMode.Create)
                            MemStream.WriteTo(fs)
                        End Using
                    Catch ex As Exception
                        myworker.ReportProgress(100.0#, "e: Konnte Datei nicht schreiben: " + ex.Message)
                    End Try
                End Using
            End If
        End If
    End Sub

End Class
