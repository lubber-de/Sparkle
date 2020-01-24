﻿Public Class FrmMain

    Dim CX, CY, CB As Integer
    Private ReadOnly UndoX(255) As Byte
    Private ReadOnly UndoY(255) As Byte
    Private ReadOnly UndoB(255) As Byte
    Private ReadOnly UndoCT(255) As Byte
    Private ReadOnly UndoCS(255) As Byte
    Private ReadOnly UndoT(255) As String
    Private Undo, UndoStep As Integer
    Private PartT() As Integer, PartS(), PartDiskLoc(), PartNo As Integer
    Private ReadOnly PETSCII As Image = My.Resources.PETSCII_BW
    Private ReadOnly BM As New Bitmap(256, 256)

	Private Sub FrmMain_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        On Error GoTo Err

        If DotNetVersion() = False Then
			MsgBox("Sparkle requires .NET Framework version 4.5 or later!", vbOKOnly, "Please install .NET Framework")
			End
		End If

		DoRegistryMagic()

		Dim T As Integer
		Dim CmdArg As String() = Environment.GetCommandLineArgs()

		ResetArrays()

        ReDim PartT(-1), PartS(-1), PartDiskLoc(-1)

        TabT = My.Resources.TabT
        TabS = My.Resources.TabS

        Packer = My.Settings.DefaultPacker

        Track(1) = 0
        For T = 1 To 34
            Select Case T
                Case 1 To 17
                    Track(T + 1) = Track(T) + (21 * 256)
                Case 18 To 24
                    Track(T + 1) = Track(T) + (19 * 256)
                Case 25 To 30
                    Track(T + 1) = Track(T) + (18 * 256)
                Case 31 To 35
                    Track(T + 1) = Track(T) + (17 * 256)
            End Select
        Next

        'CalcILTab()

        CmdLine = False
        For Each Path In CmdArg
            Select Case Strings.Right(Path, 4)
                Case ".sls"
                    CmdLine = True
                    Script = IO.File.ReadAllText(Path)          'open script...!!
                    SetScriptPath(Path)
                    If (InStr(LCase(Script), "file:") = 0) And (InStr(LCase(Script), "list:") = 0) And (InStr(LCase(Script), "script:") = 0) Then
                        MsgBox("This script does not contain any files!", vbOKOnly + vbExclamation, "Unable to build disk")
                        End
                    Else
                        MakeDisk(sender, e, True)
                        End                                    '...and exit app!!!
                    End If
                Case ".d64"
                    D64Name = Path
                    OpenFile()
            End Select
        Next

        txtSector.AllowDrop = True

		If D64Name = "" Then TsbNew_Click(sender, e)

		CX = 0 : CY = 0 : CB = 0
		CursorPos(0)

		Exit Sub
Err:
		MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

	End Sub

	Private Sub CalcILTab()
        On Error GoTo Err

        Dim SMax, IL As Integer
        Dim Disk(682) As Byte
        Dim TabT(663), TabS(663) As Byte
        Dim I As Integer = 0
        Dim SCnt As Integer
        Dim Tr(35) As Integer
        Dim S As Integer = 0

        Tr(1) = 0
        For T = 1 To 34
            Select Case T
                Case 1 To 17
                    Tr(T + 1) = Tr(T) + 21
                Case 18 To 24
                    Tr(T + 1) = Tr(T) + 19
                Case 25 To 30
                    Tr(T + 1) = Tr(T) + 18
                Case 31 To 35
                    Tr(T + 1) = Tr(T) + 17
            End Select
        Next

        For T As Integer = 35 To 1 Step -1
            If T = 18 Then
                T += 1
                S += 2
            End If
            SCnt = 0

            Select Case T
                Case 1 To 17
                    SMax = 21
                    IL = 4
                Case 18 To 24
                    SMax = 19
                    IL = 3
                Case 25 To 30
                    SMax = 18
                    IL = 3
                Case 31 To 35
                    SMax = 17
                    IL = 3
            End Select

NextSector:
            If Disk(Tr(T) + S) = 0 Then
                Disk(Tr(T) + S) = 1
                TabT(I) = T
                TabS(I) = S
                I += 1
                SCnt += 1
                S += IL
                If S >= SMax Then
                    S -= SMax
                    If (T < 18) And (S > 0) Then S -= 1 'If track 1-17 then subtract one more if S>0
                End If
                If SCnt < SMax Then GoTo NextSector
            Else
                S += 1
                If SCnt < SMax Then GoTo NextSector
            End If
        Next

		IO.File.WriteAllBytes(UserFolder + "\OneDrive\C64\Coding\TabT.prg", TabT)
		IO.File.WriteAllBytes(UserFolder + "\OneDrive\C64\Coding\TabS.prg", TabS)

		Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbNew_Click(sender As Object, e As EventArgs) Handles tsbNew.Click
        On Error GoTo Err

        If FileChanged Then
            Select Case MsgBox("Save current D64 file first?", vbYesNoCancel, "Save?")
                Case vbYes
                    If D64Name = "" Then
                        TsbSaveAs_Click(sender, e)
                        If D64Name = "" Then Exit Sub
                    Else
                        TsbSave_Click(sender, e)
                    End If
                Case vbCancel
                    Exit Sub
            End Select
        End If

        DiskHeader = "demo disk " + Year(Now).ToString
        DiskID = "sprkl"
        DemoName = "demo"

        NewDisk()

        ScanDiskForParts()

        D64Name = ""
        FileChanged = False
        StatusFileName(D64Name)

        ShowSector()

        CX = 0 : CY = 0 : CB = 0

        CursorPos(0)

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbOpen_Click(sender As Object, e As EventArgs) Handles tsbOpen.Click
        On Error GoTo Err

        Dim OpenDLG As New OpenFileDialog

        If FileChanged Then
            Select Case MsgBox("Save current D64 file first?", vbYesNoCancel, "Save?")
                Case vbYes
                    If D64Name = "" Then
                        TsbSaveAs_Click(sender, e)
                        If D64Name = "" Then Exit Sub
                    Else
                        TsbSave_Click(sender, e)
                    End If
                Case vbCancel
                    Exit Sub
            End Select
        End If

        With OpenDLG
            .Filter = "D64 Files (*.d64)|*.d64"
            .Title = "Open D64 File"
            .RestoreDirectory = True

            DialogResult = OpenDLG.ShowDialog

            If DialogResult = Windows.Forms.DialogResult.OK Then
                D64Name = .FileName
                OpenFile()
            End If
        End With

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub OpenFile()
        On Error GoTo Err

        Disk = System.IO.File.ReadAllBytes(D64Name)

        ScanDiskForParts()

        StatusFileName(D64Name)

        'Start with Directory
        CT = 18     'Track  #18
        CS = 1      'Sector #01

        CP = Track(CT) + CS * 256
        ShowSector()

        ResetUndo()

        FileChanged = False

        Exit Sub
Err:
        MsgBox("Unable to open file" + vbNewLine + ErrorToString(), vbOKOnly + vbExclamation, "Error opening file")

    End Sub

    Private Sub TsbSaveAs_Click(sender As Object, e As EventArgs) Handles tsbSaveAs.Click
        On Error GoTo Err

        Dim SaveDLG As New SaveFileDialog With {
            .Filter = "D64 Files (*.d64)|*.d64",
            .Title = "Save D64 File As...",
            .FileName = D64Name,
            .RestoreDirectory = True
        }

        Dim R As DialogResult = SaveDLG.ShowDialog(Me)

        If R = Windows.Forms.DialogResult.OK Then
            D64Name = SaveDLG.FileName
            If Strings.Right(D64Name, 4) <> ".d64" Then
                D64Name += ".d64"
            End If
            SaveFile()
            StatusFileName(D64Name)
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbSave_Click(sender As Object, e As EventArgs) Handles tsbSave.Click
        On Error GoTo Err

        If D64Name = "" Then
            TsbSaveAs_Click(sender, e)
        Else
            SaveFile()
            FileChanged = False
            StatusFileName(D64Name)
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub SaveFile()
        On Error GoTo ErrSaveFile

        IO.File.WriteAllBytes(D64Name, Disk)

        Exit Sub

ErrSaveFile:

        MsgBox("Unable to save file" + vbNewLine + ErrorToString(), vbOKOnly + vbExclamation, "Error saving file")

    End Sub

    Private Sub TsbBuildDisk_ButtonClick(sender As Object, e As EventArgs) Handles tsbBuildDisk.ButtonClick
        On Error GoTo Err

        Dim OpenDLG As New OpenFileDialog

        With OpenDLG
            .Filter = "Sparkle Loader Script files (*.sls)|*.sls"
            .Title = "Open Sparkle Loader Script"
            .RestoreDirectory = True

            Dim R As DialogResult = .ShowDialog(Me)

            If R = Windows.Forms.DialogResult.OK Then
                SetScriptPath(.FileName)
                Script = IO.File.ReadAllText(ScriptName)
                If (InStr(LCase(Script), "file:") = 0) And (InStr(LCase(Script), "list:") = 0) And (InStr(LCase(Script), "script:") = 0) Then
                    MsgBox("This script does not contain any files!", vbOKOnly + vbExclamation, "Unable to build disk")
                Else

                    MakeDisk(sender, e)

                End If
            End If
        End With

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsmRebuildDisk_Click(sender As Object, e As EventArgs) Handles tsmRebuildDisk.Click
        On Error GoTo Err

        If Script = "" Then
            TsbBuildDisk_ButtonClick(sender, e)
            Exit Sub
        Else
            If InStr(Script, "File:") = 0 Then
                MsgBox("This script does not contain any files", vbOKOnly, "Unable to build disk")
            Else

                MakeDisk(sender, e)

            End If
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub MakeDisk(sender As Object, e As EventArgs, Optional OnTheFly As Boolean = False)  'Args needed for button Sub calls
        On Error GoTo Err

        Dim DiskOK As Boolean

        Dim Frm As New FrmDisk
        Frm.Show(Me)

        Cursor = Cursors.WaitCursor

        DiskOK = BuildDemoFromScript()

        If OnTheFly = False Then
            If DiskOK = False Then
                TsbNew_Click(sender, e)                     'If error during last disk building then reset and show empty disk
            End If
            CT = 18 : CS = 1                                'Otherwise, show last built disk
            ShowSector()
            FileChanged = True
            StatusFileName(D64Name)
            ScanDiskForParts()
        End If

        If DiskOK = False Then
            MsgBox("Disk could not be built!", vbOKOnly + vbCritical, "Sparkle could not build the disk")
        End If

        GoTo Done

Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")
Done:
        Cursor = Cursors.Default

        Frm.Close()

    End Sub

    Private Sub TsbBAM_Click(sender As Object, e As EventArgs) Handles tsbBAM.Click
        On Error GoTo Err

        CT = 18
        CS = 0

        ShowSector()

        CX = 0 : CY = 0 : CB = 0

        CursorPos(0)

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbDir_Click(sender As Object, e As EventArgs) Handles tsbDir.Click
        On Error GoTo Err

        CT = 18
        CS = 1

        ShowSector()

        CX = 0 : CY = 0 : CB = 0

        CursorPos(0)

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbFirstTrack_Click(sender As Object, e As EventArgs) Handles tsbFirstTrack.Click
        On Error GoTo Err

        CT = 1
        CS = 0
        ShowSector()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbPrevTrack_Click(sender As Object, e As EventArgs) Handles tsbPrevTrack.Click
        On Error GoTo Err

        If CT <> 1 Then
            CT -= 1
            CS = 0
            ShowSector()
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbNextTrack_Click(sender As Object, e As EventArgs) Handles tsbNextTrack.Click
        On Error GoTo Err

        If CT <> 35 Then
            CT += 1
            CS = 0
            ShowSector()
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbLastTrack_Click(sender As Object, e As EventArgs) Handles tsbLastTrack.Click
        On Error GoTo Err

        CT = 35
        CS = 0
        ShowSector()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbNextSector4_Click(sender As Object, e As EventArgs) Handles tsbNextSector4.Click
        On Error GoTo Err

        If CT = 18 Then
            If (Disk(Track(CT) + CS * 256) = 18) And (Disk(Track(CT) + CS * 256 + 1) <> 255) Then
                CS = Disk(Track(CT) + CS * 256 + 1)
            End If
        Else

            Dim I As Integer

            For I = 0 To 663
                If (TabT(I) = CT) And (TabS(I) = CS) Then Exit For
            Next

            If I >= 663 Then Exit Sub

            CS = TabS(I + 1)
            CT = TabT(I + 1)
        End If

        ShowSector()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbPrevSector4_Click(sender As Object, e As EventArgs) Handles tsbPrevSector4.Click
        On Error GoTo Err

        Dim I As Integer

        If CT = 18 Then
            For I = 0 To 18
                If (Disk(Track(CT) + I * 256) = CT) And (Disk(Track(CT) + I * 256 + 1) = CS) Then
                    CS = I
                End If
            Next
        Else
            For I = 0 To 663
                If (TabT(I) = CT) And (TabS(I) = CS) Then Exit For
            Next

            If I = 0 Then Exit Sub
            If I > 663 Then Exit Sub

            CS = TabS(I - 1)
            CT = TabT(I - 1)
        End If

        ShowSector()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbSector0_Click(sender As Object, e As EventArgs) Handles tsbSector0.Click
        On Error GoTo Err

        CS = 0
        ShowSector()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbPrevSector_Click(sender As Object, e As EventArgs) Handles tsbPrevSector.Click
        On Error GoTo Err

        If CS <> 0 Then
            CS -= 1
            ShowSector()
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbNextSector_Click(sender As Object, e As EventArgs) Handles tsbNextSector.Click
        On Error GoTo Err

        If CS <> MaxSector Then
            CS += 1
            ShowSector()
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbLastSector_Click(sender As Object, e As EventArgs) Handles tsbLastSector.Click
        On Error GoTo Err

        CS = MaxSector
        ShowSector()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TxtSector_KeyDown(sender As Object, e As KeyEventArgs) Handles txtSector.KeyDown
        On Error GoTo Err

        Select Case e.KeyCode
            Case 48 To 57, 65 To 70
                'Case Keys.D0 To Keys.D9, Keys.A To Keys.F
                If e.Control Then
                    If e.KeyCode = Keys.B Then          'BAM (Ctrl+B)
                        TsbBAM_Click(sender, e)
                    ElseIf e.KeyCode = Keys.D Then      'Directory (Ctrl+D)
                        TsbDir_Click(sender, e)
                    ElseIf e.KeyCode = Keys.E Then      'Script Editor (Ctrl+E)
                        TsbScriptEditor_Click(sender, e)
                    ElseIf e.KeyCode = Keys.A Then      'About (Ctrl+A)
                        TsbAbout_Click(sender, e)
                    End If
                ElseIf e.Shift Then
                ElseIf e.Alt Then
                Else
                    If e.KeyCode < 65 Then
                        UpdateByte(e.KeyCode - 48)
                    Else
                        UpdateByte(e.KeyCode - 55)
                    End If

                    CursorPos(1)
                    AddToUndo()

                    txtSector.SelectionColor = Color.Red
                    txtSector.SelectedText = Chr(e.KeyCode)
                    MoveCursorRight()
                    FileChanged = True
                    tsbUndo.Enabled = True

                    StatusFileName(D64Name + "*")
                End If
            Case Keys.N         'New D64 File (Ctrl+N)
                If e.Control Then TsbNew_Click(sender, e)
            Case Keys.O         'Open D64 File (Ctrl+O)
                If e.Control Then TsbOpen_Click(sender, e)
            Case Keys.S         'Save D64 File (Ctrl+S)
                If e.Control Then TsbSave_Click(sender, e)
            Case Keys.F12       'Save D64 File As... (F12)
                TsbSaveAs_Click(sender, e)
            Case Keys.Left      'Cursor Left
                MoveCursorLeft()
            Case Keys.Up        'Cursor Up
                MoveCursorUp()
            Case Keys.Right     'Cursor Right
                MoveCursorRight()
            Case Keys.Down      'Cursor Down
                MoveCursorDown()
            Case Keys.Oemplus   'Next Sector in Sequence (+ key)
                TsbNextSector4_Click(sender, e)
            Case Keys.OemMinus  'Prevous Sector in Sequence (- key)
                TsbPrevSector4_Click(sender, e)
            Case Keys.Home      'First Sector of First Part (Home key)
                If e.Control Then
                    TsbFirstTrack_Click(sender, e)
                ElseIf e.Shift Then
                    TsbSector0_Click(sender, e)
                Else
                    TsbFirstPart_Click(sender, e)
                End If
            Case Keys.End       'Last Sector of Last Part (End key)
                If e.Control Then
                    TsbLastTrack_Click(sender, e)
                ElseIf e.Shift Then
                    TsbLastSector_Click(sender, e)
                Else
                    TsbLastPart_Click(sender, e)
                End If
            Case Keys.PageUp    'First Sector of Previous Part (PgUp key)
                If e.Control Then
                    TsbPrevTrack_Click(sender, e)
                ElseIf e.Shift Then
                    TsbPrevSector_Click(sender, e)
                Else
                    TsbPrevPart_Click(sender, e)
                End If
            Case Keys.PageDown  'First Sector of Next PArt (PgDn key)
                If e.Control Then
                    TsbNextTrack_Click(sender, e)
                ElseIf e.Shift Then
                    TsbNextSector_Click(sender, e)
                Else
                    TsbNextPart_Click(sender, e)
                End If
            Case Keys.Z
                If e.Control Then TsbUndo_Click(sender, e)  'Undo (Ctrl+Z)
            Case Keys.F5
                If e.Shift Then
                    TsmRebuildDisk_Click(sender, e)     'Rebuild Disk (Shift+F5)
                Else
                    TsbBuildDisk_ButtonClick(sender, e) 'Build Disk (F5)
                End If
            Case Else
        End Select

        CursorPos(0)
        e.SuppressKeyPress = True

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TxtSector_MouseDown(sender As Object, e As MouseEventArgs) Handles txtSector.MouseDown
        On Error GoTo Err

        Dim MS As Integer = txtSector.SelectionStart

        CY = Int(MS / 49)
        CX = Int((MS Mod 49) / 3)
        CB = MS - (CY * 49) - (CX * 3)
        If CB > 1 Then CB = 1

        CursorPos(0)

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbUndo_Click(sender As Object, e As EventArgs) Handles tsbUndo.Click
        On Error GoTo Err

        Dim AsciiUndo As Boolean = True

        If UndoStep > 0 Then
            UndoStep -= 1

            If UndoStep = 0 Then
                tsbUndo.Enabled = False
                FileChanged = False
                StatusFileName(D64Name)
            End If

            Undo -= 1
            If Undo < 0 Then Undo += 256
            If CT <> UndoCT(Undo) Or UndoCS(Undo) <> CS Then
                CT = UndoCT(Undo)
                CS = UndoCS(Undo)
                ShowSector()
                Dim UT As Integer = Undo
                For I As Integer = 0 To UndoStep
                    Undo -= 1
                    If Undo < 0 Then Undo += 256
                    If (UndoCT(Undo) = CT) And (UndoCS(Undo) = CS) Then
                        CX = UndoX(Undo)
                        CY = UndoY(Undo)
                        CB = UndoB(Undo)
                        CursorPos(1)
                        txtSector.SelectionColor = Color.Red
                    End If
                Next
                Undo = UT
            End If
            CX = UndoX(Undo)
            CY = UndoY(Undo)
            CB = UndoB(Undo)

            CursorPos(1)
            txtSector.SelectionColor = Color.Black
            txtSector.SelectedText = UndoT(Undo)

            txtSector.Select((CY * 49) + (CX * 3) + 0, 1)
            If txtSector.SelectionColor = Color.Red Then AsciiUndo = False
            txtSector.Select((CY * 49) + (CX * 3) + 1, 1)
            If txtSector.SelectionColor = Color.Red Then AsciiUndo = False

            CursorPos(0)

            If Asc(UndoT(Undo)) < 65 Then
                UpdateByte(Asc(UndoT(Undo)) - 48, AsciiUndo)
            Else
                UpdateByte(Asc(UndoT(Undo)) - 55, AsciiUndo)
            End If
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub AddToUndo()
        On Error GoTo Err

        UndoCT(Undo) = CT
        UndoCS(Undo) = CS
        UndoX(Undo) = CX
        UndoB(Undo) = CB
        UndoY(Undo) = CY
        UndoT(Undo) = txtSector.SelectedText
        Undo += 1
        Undo = Undo Mod 256

        If UndoStep <> 255 Then
            UndoStep += 1
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub ResetUndo()
        On Error GoTo Err

        For Undo = 0 To 255
            UndoCT(Undo) = 0
            UndoCS(Undo) = 0
            UndoX(Undo) = 0
            UndoY(Undo) = 0
            UndoB(Undo) = 0
            UndoT(Undo) = ""
        Next

        Undo = 0
        UndoStep = 0

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub ShowSector()
        On Error GoTo Err

        Dim tmpSect As String = ""
        Dim Tmp As Byte
        Dim TA As String
        Dim C, R As Integer

        If CT = 1 And CS = 0 Then
            tsbPrevSector4.Enabled = False
            tsbNextSector4.Enabled = True
        ElseIf CT = 35 And CS = 12 Then
            tsbPrevSector4.Enabled = True
            tsbNextSector4.Enabled = False
        Else
            tsbPrevSector4.Enabled = True
            tsbNextSector4.Enabled = True
        End If

        CP = Track(CT) + CS * 256

        'txtASCII.Text = ""
        Dim L, T As Integer
        For R = 0 To 15
            'TA = ""
            For C = 0 To 15
                Tmp = Disk(CP + R * 16 + C)
                tmpSect += ByteToChar(Int(Tmp / 16)) + ByteToChar(Tmp And &HF) + " "
                L = (Tmp Mod 16) * 16
                T = (Int(Tmp / 16)) * 16
                Using Gr As Graphics = Graphics.FromImage(BM)
                    ' Define source and destination rectangles.
                    Dim src_rect As New Rectangle(L, T, 16, 16)
                    Dim dst_rect As New Rectangle((C * 16), (R * 16) + 2, 16, 16)

                    ' Copy that part of the image.
                    Gr.DrawImage(PETSCII, dst_rect, src_rect, GraphicsUnit.Pixel)
                End Using
            Next
            If R < 15 Then
                tmpSect += vbNewLine
            End If
        Next

        txtSector.Text = tmpSect

        Pbx.Image = BM
        Pbx.Refresh()

        CursorPos(0)

        txtCT.Text = CT.ToString
        txtCS.Text = CS.ToString

        SetLastSector()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Function ByteToChar(B As Byte) As String
        On Error GoTo Err
        If B > 9 Then
            B += 55
        Else
            B += 48
        End If

        ByteToChar = Chr(B)

        Exit Function
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Function

    Private Sub StatusFileName(FN As String)
        On Error GoTo Err

        If FN = "*" Or FN = "" Then
            FN = "(New)" + FN
        End If

        ssLabel.Text = "Current File: " + FN

        If FileChanged = True Then
            Text = "Sparkle (unsaved changes)"
        Else
            Text = "Sparkle"
            txtSector.ForeColor = Color.Black
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub UpdateByte(N As Byte, Optional Undo As Boolean = False)
        On Error GoTo Err

        Dim B As Byte

        B = Disk(Track(CT) + (CS * 256) + (CY * 16) + CX)

        If CB = 0 Then
            N *= 16
            B = (B And &HF) + N
        Else
            B = (B And &HF0) + N
        End If

        Disk(Track(CT) + CS * 256 + CY * 16 + CX) = B

        Dim Tmp As Integer
        Dim L, T As Integer
        L = (B Mod 16) * 16
        T = (Int(B / 16)) * 16
        Using Gr As Graphics = Graphics.FromImage(BM)
            ' Define source and destination rectangles.
            Dim src_rect As New Rectangle(L, T, 16, 16)
            Dim dst_rect As New Rectangle((CX * 16) + 2, (CY * 16) + 2, 16, 16)

            ' Copy that part of the image.
            Gr.DrawImage(PETSCII, dst_rect, src_rect, GraphicsUnit.Pixel)
        End Using

        Pbx.Image = BM
        Pbx.Refresh()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub CursorPos(SelLen As Integer)
        On Error GoTo Err

        txtSector.Select((CY * 49) + (CX * 3) + CB, SelLen)

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub MoveCursorRight()
        On Error GoTo Err

        CB += 1
        If CB > 1 Then
            CB = 0
            CX += 1
            If CX > 15 Then
                CX = 0
                CB = 0
                CY += 1
                If CY > 15 Then
                    CY = 0
                End If
            End If
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub MoveCursorLeft()
        On Error GoTo Err

        CB -= 1
        If CB < 0 Then
            CB = 1
            CX -= 1
            If CX < 0 Then
                CX = 15
                CB = 1
                CY -= 1
                If CY < 0 Then
                    CY = 15
                End If
            End If
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub MoveCursorUp()
        On Error GoTo Err

        CY -= 1
        If CY < 0 Then
            CY = 15
            CX -= 1
            If CX < 0 Then CX = 15
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub MoveCursorDown()
        On Error GoTo Err

        CY += 1
        If CY > 15 Then
            CY = 0
            CX += 1
            If CX > 15 Then CX = 0
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbScriptEditor_Click(sender As Object, e As EventArgs) Handles TsbScriptEditor.Click
        On Error GoTo Err

        Using A As New FrmSE
            A.ShowDialog(Me)
        End Using

        If bBuildDisk = True Then
            If InStr(Script, "File:") = 0 Then
                MsgBox("This script does not contain any files", vbOKOnly, "Unable to build disk")
            Else

                MakeDisk(sender, e)

            End If
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TxtCT_GotFocus(sender As Object, e As EventArgs) Handles txtCT.GotFocus
        On Error GoTo Err

        txtCT.SelectAll()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TxtCT_KeyDown(sender As Object, e As KeyEventArgs) Handles txtCT.KeyDown
        On Error GoTo Err

        Select Case e.KeyCode
            Case Keys.D0, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9
            Case Keys.Delete, Keys.Back, Keys.Left, Keys.Right, Keys.Up, Keys.Down
            Case Keys.Return
                TxtCT_LostFocus(sender, e)
                txtCT.SelectAll()
                GoTo SuppressKey
            Case Else
SuppressKey:
                e.SuppressKeyPress = True
                e.Handled = True
        End Select

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TxtCT_LostFocus(sender As Object, e As EventArgs) Handles txtCT.LostFocus
        On Error GoTo Err

        If txtCT.Text = "" Then
            txtCT.Text = CT.ToString
        Else
            Dim I As Integer = txtCT.Text
            If I < 1 Then I = 1
            If I > 35 Then I = 35
            CT = I
            ShowSector()
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TxtCT_MouseDown(sender As Object, e As MouseEventArgs) Handles txtCT.MouseDown
        On Error GoTo Err

        txtCT.SelectAll()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TxtCS_GotFocus(sender As Object, e As EventArgs) Handles txtCS.GotFocus
        On Error GoTo Err

        txtCS.SelectAll()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbFirstPart_Click(sender As Object, e As EventArgs) Handles TsbFirstPart.Click
        On Error GoTo Err

        If PartT.Count > 0 Then
            CT = PartT(0)
            CS = PartS(0)
            ShowSector()
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbLastPart_Click(sender As Object, e As EventArgs) Handles TsbLastPart.Click
        On Error GoTo Err

        If PartT.Count > 0 Then
            CT = PartT(PartT.Count - 1)
            CS = PartS(PartS.Count - 1)
            ShowSector()
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbNextPart_Click(sender As Object, e As EventArgs) Handles TsbNextPart.Click
        On Error GoTo Err

        If PartT.Count > 0 Then

            Dim I, J As Integer

            For I = 0 To 663
                If (TabT(I) = CT) And (TabS(I) = CS) Then Exit For
            Next

            For J = 0 To PartNo
                If PartDiskLoc(J) > I Then Exit For
            Next

            If J > PartNo Then J = PartNo

            CT = PartT(J)
            CS = PartS(J)

            ShowSector()

        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbPrevPart_Click(sender As Object, e As EventArgs) Handles TsbPrevPart.Click
        On Error GoTo Err

        If PartT.Count > 0 Then

            Dim I, J As Integer

            For I = 0 To 663
                If (TabT(I) = CT) And (TabS(I) = CS) Then Exit For
            Next

            For J = PartNo To 0 Step -1
                If PartDiskLoc(J) < I Then Exit For
            Next

            If J < 0 Then J = 0

            CT = PartT(J)
            CS = PartS(J)

            ShowSector()

        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsbAbout_Click(sender As Object, e As EventArgs) Handles TsbAbout.Click
        On Error GoTo Err

        Dim A As New FrmAbout
        A.Show(Me)

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsmTestDisk_Click(sender As Object, e As EventArgs) Handles TsmTestDisk.Click
        On Error GoTo Err

		MakeTestDisk()

		ScanDiskForParts()

        CT = 18
        CS = 1

        ShowSector()
		If IO.Directory.Exists(UserDeskTop) Then
			D64Name = UserDeskTop + "\Loader Test.d64"
		Else
			D64Name = "C:\"
        End If

        CurrentDisk = -1    'This is needed if we have a script loaded when a test disk is being built

        TsbSaveAs_Click(sender, e)

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TxtCS_KeyDown(sender As Object, e As KeyEventArgs) Handles txtCS.KeyDown
        On Error GoTo Err

        Select Case e.KeyCode
            Case Keys.D0, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9
            Case Keys.Delete, Keys.Back, Keys.Left, Keys.Right, Keys.Up, Keys.Down
            Case Keys.Return
                TxtCS_LostFocus(sender, e)
                txtCS.SelectAll()
                GoTo SuppressKey
            Case Else
SuppressKey:
                e.SuppressKeyPress = True
                e.Handled = True
        End Select

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TxtCS_LostFocus(sender As Object, e As EventArgs) Handles txtCS.LostFocus
        On Error GoTo Err

        If txtCS.Text = "" Then
            txtCS.Text = CS.ToString
        Else
            Dim I As Integer = txtCS.Text
            Select Case CT
                Case 1 To 17
                    If I > 20 Then I = 20
                Case 18 To 24
                    If I > 18 Then I = 18
                Case 25 To 30
                    If I > 18 Then I = 17
                Case 31 To 35
                    If I > 18 Then I = 16
            End Select
            CS = I
            ShowSector()
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsmAssociate_Click(sender As Object, e As EventArgs) Handles TsmAssociate.Click
        On Error GoTo Err

        AssociateSLS()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TsmDeleteAssociation_Click(sender As Object, e As EventArgs) Handles TsmDeleteAssociation.Click
        On Error GoTo Err

        DeleteAssociation()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TxtCS_MouseDown(sender As Object, e As MouseEventArgs) Handles txtCS.MouseDown
        On Error GoTo Err

        txtCS.SelectAll()

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub FrmMain_DragDrop(sender As Object, e As DragEventArgs) Handles Me.DragDrop
        On Error GoTo Err

        Dim DropFiles() As String = e.Data.GetData(DataFormats.FileDrop)
        For Each Path In DropFiles
            Select Case Strings.Right(Path, 4)
                Case ".sls"
                    SetScriptPath(Path)
                    Script = IO.File.ReadAllText(Path)     'open script...
                    MakeDisk(sender, e)
                Case ".d64"
                    D64Name = Path
                    OpenFile()
            End Select
        Next

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub FrmMain_DragEnter(sender As Object, e As DragEventArgs) Handles Me.DragEnter
        On Error GoTo Err

        If e.Data.GetDataPresent(DataFormats.FileDrop) Then
            e.Effect = DragDropEffects.Copy
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TxtSector_DragDrop(sender As Object, e As DragEventArgs) Handles txtSector.DragDrop
        On Error GoTo Err

        FrmMain_DragDrop(sender, e)

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub TxtSector_DragEnter(sender As Object, e As DragEventArgs) Handles txtSector.DragEnter
        On Error GoTo Err

        FrmMain_DragEnter(sender, e)

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

    Private Sub ScanDiskForParts()
        On Error GoTo Err

        Dim BlockNo As Integer = 0

        PartNo = -1

        ReDim PartT(PartNo), PartS(PartNo), PartDiskLoc(PartNo)

        If Disk(255) = 0 Then Exit Sub

NextPart:
        PartNo += 1

        ReDim Preserve PartT(PartNo), PartS(PartNo), PartDiskLoc(PartNo)

        PartT(PartNo) = TabT(BlockNo)  'First Track of Part
        PartS(PartNo) = TabS(BlockNo)  'First Sector of Part
        PartDiskLoc(PartNo) = BlockNo

        BlockNo += Disk(Track(TabT(BlockNo)) + (TabS(BlockNo) * 256) + 255)

        If BlockNo < 664 Then
            If Disk(Track(TabT(BlockNo)) + (TabS(BlockNo) * 256) + 255) <> 0 Then GoTo NextPart
        End If

        Exit Sub
Err:
        MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

    End Sub

End Class
