#requires -Version 7.0
# Original geometric icon artwork; MIT, same license as the project.
param([string]$OutputPath=(Join-Path $PSScriptRoot '..\Tangerine\Assets\tangerine.ico'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$images=@()
foreach($size in @(16,24,32,48,64,128,256)){
    $bitmap=[Drawing.Bitmap]::new($size,$size)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.ScaleTransform($size/64.0,$size/64.0)
    $fill=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,89,0))
    $pen=[Drawing.Pen]::new([Drawing.Color]::White,2.6)
    $pen.StartCap=[Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap=[Drawing.Drawing2D.LineCap]::Round
    $graphics.FillEllipse($fill,4,4,56,56)
    for($i=0;$i -lt 8;$i++){
        $angle=$i*[Math]::PI/4
        $graphics.DrawLine($pen,[single](32+[Math]::Cos($angle)*8),[single](32+[Math]::Sin($angle)*8),[single](32+[Math]::Cos($angle)*21),[single](32+[Math]::Sin($angle)*21))
    }
    $stream=[IO.MemoryStream]::new()
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $images+=@{Size=$size;Bytes=$stream.ToArray()}
    $stream.Dispose();$pen.Dispose();$fill.Dispose();$graphics.Dispose();$bitmap.Dispose()
}
$OutputPath=[IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath)|Out-Null
$writer=[IO.BinaryWriter]::new([IO.File]::Create($OutputPath))
try{
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$images.Count)
    $offset=6+16*$images.Count
    foreach($item in $images){$dimension=if($item.Size -eq 256){0}else{$item.Size};$writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$item.Bytes.Length);$writer.Write([uint32]$offset);$offset+=$item.Bytes.Length}
    foreach($item in $images){$writer.Write([byte[]]$item.Bytes)}
}finally{$writer.Dispose()}
Write-Output "Generated original icon: $OutputPath"
