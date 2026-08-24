Attribute VB_Name = "ExcelHiddenSheetCsvExporter"
Option Explicit

'============================================================
' ExcelHiddenSheetCsvExporter.bas
'
' 指定フォルダ配下（サブフォルダも再帰的に）にあるExcelブックを検索し、
' 可視シート・非表示シート・最上位非表示(VeryHidden)シートを問わず全シートを
' CSVとして書き出すマクロ。
'
' 【出力ファイル名の規則】
'   ・選択したフォルダの直下にあるExcelファイルのシート
'       → "{ブック名}_{シート名}.csv"
'   ・サブフォルダ（孫フォルダ以下含む）にあるExcelファイルのシート
'       → "{そのファイルが直接入っている親フォルダ名}_{ブック名}_{シート名}.csv"
'     （フォルダの深さに関わらず、直上の親フォルダ名のみを1つ付ける）
'
' 【出力先】
'   選択したフォルダの直下に作成される "CSV_output" フォルダ（自動作成）。
'
' 【対象】
'   .xlsx / .xlsm / .xlsb / .xls のみ。開いたままロックされている一時ファイル
'   （"~$"で始まるファイル）は除外。ワークシートのみが対象で、グラフシートは
'   セルデータを持たないため対象外。
'
' 【使い方】
'   1. Excelの「開発」タブ → Visual Basic → このファイルをインポート
'      （またはVBEでプロジェクトにモジュールを追加してこの中身を貼り付け）
'   2. Alt+F8 等から ExportAllSheetsToCsv を実行
'   3. フォルダ選択ダイアログで対象フォルダを選ぶ
'
' 【調整できる設定】
'   OUTPUT_FILE_FORMAT … 既定はxlCSV（Shift-JIS）。他ツールでUTF-8として読みたい
'                          場合はxlCSVUTF8に変更する。
'============================================================

Private Const OUTPUT_SUBFOLDER As String = "CSV_output"
Private Const OUTPUT_FILE_FORMAT As Long = xlCSV ' UTF-8で出力したい場合はxlCSVUTF8に変更

Private mFso As Object
Private mOutputDir As String
Private mSuccessCount As Long
Private mErrorLog As String

Public Sub ExportAllSheetsToCsv()
    Dim baseFolder As String
    baseFolder = PickFolder()
    If baseFolder = "" Then
        MsgBox "フォルダが選択されなかったため中止しました。", vbInformation
        Exit Sub
    End If

    Set mFso = CreateObject("Scripting.FileSystemObject")
    mOutputDir = mFso.BuildPath(baseFolder, OUTPUT_SUBFOLDER)
    If Not mFso.FolderExists(mOutputDir) Then mFso.CreateFolder mOutputDir

    mSuccessCount = 0
    mErrorLog = ""

    Dim originalScreenUpdating As Boolean
    Dim originalDisplayAlerts As Boolean
    originalScreenUpdating = Application.ScreenUpdating
    originalDisplayAlerts = Application.DisplayAlerts
    Application.ScreenUpdating = False
    Application.DisplayAlerts = False

    On Error GoTo CleanFail
    ProcessFolder baseFolder, baseFolder

    Application.ScreenUpdating = originalScreenUpdating
    Application.DisplayAlerts = originalDisplayAlerts

    Dim msg As String
    msg = "完了: " & mSuccessCount & "件のシートをCSVに出力しました。" & vbCrLf & _
          "出力先: " & mOutputDir
    If mErrorLog <> "" Then
        msg = msg & vbCrLf & vbCrLf & "以下でエラーが発生し、スキップしました:" & vbCrLf & mErrorLog
    End If
    MsgBox msg, vbInformation
    Exit Sub

CleanFail:
    Application.ScreenUpdating = originalScreenUpdating
    Application.DisplayAlerts = originalDisplayAlerts
    MsgBox "予期しないエラーで中断しました: " & Err.Description, vbCritical
End Sub

' フォルダ選択ダイアログを表示し、選ばれたフォルダのフルパスを返す（キャンセル時は空文字）
Private Function PickFolder() As String
    Dim fd As Object
    Set fd = Application.FileDialog(4) ' msoFileDialogFolderPicker（参照設定不要にするため数値指定）
    fd.Title = "CSVに変換したいExcelファイルが入っているフォルダを選択してください"
    If fd.Show = -1 Then
        PickFolder = fd.SelectedItems(1)
    Else
        PickFolder = ""
    End If
End Function

' currentFolder配下のExcelファイルを処理し、そのままサブフォルダへ再帰する
Private Sub ProcessFolder(ByVal currentFolder As String, ByVal baseFolder As String)
    ' 出力フォルダ自身はスキャン対象から除外する（自己参照・無駄な走査を避けるため）
    If mFso.GetAbsolutePathName(currentFolder) = mFso.GetAbsolutePathName(mOutputDir) Then Exit Sub

    Dim folder As Object
    Set folder = mFso.GetFolder(currentFolder)

    Dim file As Object
    For Each file In folder.Files
        If IsExcelFile(file.Name) Then
            ExportWorkbookSheets file.Path, currentFolder, baseFolder
        End If
    Next file

    Dim subFolder As Object
    For Each subFolder In folder.SubFolders
        ProcessFolder subFolder.Path, baseFolder
    Next subFolder
End Sub

Private Function IsExcelFile(ByVal fileName As String) As Boolean
    If Left$(fileName, 2) = "~$" Then
        IsExcelFile = False ' Excelが開いている間に作る一時ロックファイルを除外
        Exit Function
    End If
    Dim ext As String
    ext = LCase$(mFso.GetExtensionName(fileName))
    IsExcelFile = (ext = "xlsx" Or ext = "xlsm" Or ext = "xlsb" Or ext = "xls")
End Function

' 1つのExcelファイルを開き、全ワークシートをCSVとして書き出す
Private Sub ExportWorkbookSheets(ByVal filePath As String, ByVal fileFolder As String, ByVal baseFolder As String)
    Dim isNested As Boolean
    isNested = (mFso.GetAbsolutePathName(fileFolder) <> mFso.GetAbsolutePathName(baseFolder))

    Dim folderPrefix As String
    If isNested Then
        folderPrefix = SanitizeFileNamePart(mFso.GetFolder(fileFolder).Name) & "_"
    Else
        folderPrefix = ""
    End If

    Dim bookNameNoExt As String
    bookNameNoExt = mFso.GetBaseName(filePath)

    Dim wb As Workbook
    On Error GoTo FileFail
    Set wb = Workbooks.Open(filePath, ReadOnly:=True, UpdateLinks:=False, IgnoreReadOnlyRecommended:=True)

    Dim ws As Worksheet
    For Each ws In wb.Worksheets
        ExportSheetToCsv ws, folderPrefix & bookNameNoExt & "_" & SanitizeFileNamePart(ws.Name) & ".csv"
    Next ws

    wb.Close SaveChanges:=False
    Exit Sub

FileFail:
    mErrorLog = mErrorLog & "・" & filePath & " : " & Err.Description & vbCrLf
    On Error Resume Next
    If Not wb Is Nothing Then wb.Close SaveChanges:=False
    On Error GoTo 0
End Sub

' 1枚のシートを、新規ブックにコピーしてCSVとして保存する
' （Copyメソッドは非表示/最上位非表示のシートでも可視性を変えずに実行できるため、
'   元ブックのシート表示状態には一切触れずに済む）
Private Sub ExportSheetToCsv(ByVal ws As Worksheet, ByVal outputFileName As String)
    Dim tmpWb As Workbook
    Dim outputPath As String
    outputPath = mFso.BuildPath(mOutputDir, MakeUniqueFileName(outputFileName))

    On Error GoTo SheetFail
    ws.Copy ' 引数なしのCopyは「新規ブックの先頭シート」としてコピーする
    Set tmpWb = ActiveWorkbook
    ' コピー先の新規ブック（保存後に破棄する使い捨てブック）側でだけ可視化する。
    ' 唯一のシートが非表示のままだとCSV保存時にアクティブシートを認識できないため。
    tmpWb.Worksheets(1).Visible = xlSheetVisible
    tmpWb.SaveAs Filename:=outputPath, FileFormat:=OUTPUT_FILE_FORMAT, CreateBackup:=False
    tmpWb.Close SaveChanges:=False
    mSuccessCount = mSuccessCount + 1
    Exit Sub

SheetFail:
    mErrorLog = mErrorLog & "・" & ws.Parent.FullName & " [" & ws.Name & "] : " & Err.Description & vbCrLf
    On Error Resume Next
    If Not tmpWb Is Nothing Then tmpWb.Close SaveChanges:=False
    On Error GoTo 0
End Sub

' シート名をファイル名の一部として使うための簡易サニタイズ
' （Excelのシート名は元々\/:*?"<>|を含められないため通常は素通りするが、念のため用意）
Private Function SanitizeFileNamePart(ByVal s As String) As String
    Dim invalidChars As Variant
    invalidChars = Array("\", "/", ":", "*", "?", """", "<", ">", "|")
    Dim result As String
    result = s
    Dim i As Long
    For i = LBound(invalidChars) To UBound(invalidChars)
        result = Replace(result, invalidChars(i), "_")
    Next i
    SanitizeFileNamePart = result
End Function

' 出力先に同名ファイルが既にあれば "_(2)" のように連番を付けて重複上書きを避ける
Private Function MakeUniqueFileName(ByVal fileName As String) As String
    Dim baseName As String
    Dim ext As String
    baseName = mFso.GetBaseName(fileName)
    ext = mFso.GetExtensionName(fileName)

    Dim candidate As String
    candidate = fileName
    Dim n As Long
    n = 1
    Do While mFso.FileExists(mFso.BuildPath(mOutputDir, candidate))
        n = n + 1
        candidate = baseName & "_(" & n & ")." & ext
    Loop
    MakeUniqueFileName = candidate
End Function
