# Renders every *.pdf in -Dir to <name>-p<N>.png beside it, through Windows.Data.Pdf (no poppler needed), and prints
# each page's size in DIPs (1/96 inch: A4 is 793 x 1123, an 80 mm roll is 302.33 wide). Phase 79; the "look at the
# rendered PDF" rule is phases 73-74's. Run with Windows PowerShell 5.1:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/pdf/pdf2png.ps1 -Dir <folder>
param([string]$Dir, [double]$Scale = 1.6)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]

$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1
$asActionTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction'
} | Select-Object -First 1

function Await($op, [Type]$t) {
    $task = $asTask.MakeGenericMethod($t).Invoke($null, @($op)); $task.Wait(); $task.Result
}

Get-ChildItem -Path $Dir -Filter *.pdf | ForEach-Object {
    $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($_.FullName)) ([Windows.Storage.StorageFile])
    $doc = Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
    for ($i = 0; $i -lt $doc.PageCount; $i++) {
        $page = $doc.GetPage($i)
        $opts = New-Object Windows.Data.Pdf.PdfPageRenderOptions
        $opts.DestinationWidth = [uint32]($page.Size.Width * $Scale)
        $opts.DestinationHeight = [uint32]($page.Size.Height * $Scale)
        $stream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
        $t = $asActionTask.Invoke($null, @($page.RenderToStreamAsync($stream, $opts))); $t.Wait()
        $net = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream.GetInputStreamAt(0))
        $out = Join-Path $Dir ("{0}-p{1}.png" -f $_.BaseName, ($i + 1))
        $fs = [System.IO.File]::Create($out); $net.CopyTo($fs); $fs.Close()
        "{0} page {1}: {2} x {3} pt" -f $_.Name, ($i + 1), $page.Size.Width, $page.Size.Height
    }
}
