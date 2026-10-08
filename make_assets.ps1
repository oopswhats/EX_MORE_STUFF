# Rebuilds EX More Stuff's images from the originals in art\: res\background.jpg (EXMORESTUFF.png, JPEG q90),
# res\logo.png (EXMORESTUFF_ICON.png at 128 px), src\app.ico (the icon, 16-256 px, PNG entries) and res\wordmark.png
# (EXMORESTUFF_WORDS.png: the lettering lifted off its black background, cropped, 160 px tall).
param([string]$Background = "$PSScriptRoot\art\EXMORESTUFF.png", [string]$Icon = "$PSScriptRoot\art\EXMORESTUFF_ICON.png",
      [string]$Words = "$PSScriptRoot\art\EXMORESTUFF_WORDS.png")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @"
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class OffBlack {
    // Lettering drawn on black, as the same lettering on transparency: alpha from the brightest channel (above a
    // small noise floor), colour un-blended from black; cropped to the lettering plus `pad` pixels.
    public static string Lift(string input, string outputFile, int floor, int pad, int height) {
        var source = new Bitmap(input);
        int w = source.Width, h = source.Height;
        var rect = new Rectangle(0, 0, w, h);
        BitmapData d = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * w * 4, w * 4);
        source.UnlockBits(d);
        int left = w, top = h, right = 0, bottom = 0;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) {
            int p = (y * w + x) * 4, m = Math.Max(px[p], Math.Max(px[p + 1], px[p + 2]));
            if (m > floor * 3) { left = Math.Min(left, x); right = Math.Max(right, x + 1); top = Math.Min(top, y); bottom = Math.Max(bottom, y + 1); }
        }
        left = Math.Max(0, left - pad); top = Math.Max(0, top - pad); right = Math.Min(w, right + pad); bottom = Math.Min(h, bottom + pad);
        int ow = right - left, oh = bottom - top;
        var output = new byte[ow * oh * 4];
        for (int y = 0; y < oh; y++) for (int x = 0; x < ow; x++) {
            int p = ((y + top) * w + x + left) * 4, q = (y * ow + x) * 4;
            int m = Math.Max(px[p], Math.Max(px[p + 1], px[p + 2]));
            int a = Math.Max(0, Math.Min(255, (m - floor) * 255 / (255 - floor)));
            if (a == 0 || m == 0) continue;
            for (int c = 0; c < 3; c++) output[q + c] = (byte)Math.Min(255, px[p + c] * 255 / m);
            output[q + 3] = (byte)a;
        }
        var result = new Bitmap(ow, oh, PixelFormat.Format32bppArgb);
        BitmapData r = result.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < oh; y++) Marshal.Copy(output, y * ow * 4, r.Scan0 + y * r.Stride, ow * 4);
        result.UnlockBits(r);
        source.Dispose();
        int width = ow * height / oh;
        using (var mark = new Bitmap(width, height, PixelFormat.Format32bppArgb)) {
            using (Graphics g = Graphics.FromImage(mark)) {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(result, 0, 0, width, height);
            }
            result.Dispose();
            mark.Save(outputFile, ImageFormat.Png);
        }
        return width + "x" + height;
    }
}
"@
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$res = Join-Path $root "res"
New-Item -ItemType Directory -Force $res | Out-Null

$bg = [Drawing.Image]::FromFile($Background)
$codec = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq "image/jpeg" }
$ep = New-Object Drawing.Imaging.EncoderParameters 1
$ep.Param[0] = New-Object Drawing.Imaging.EncoderParameter ([Drawing.Imaging.Encoder]::Quality, [long]90)
$bg.Save((Join-Path $res "background.jpg"), $codec, $ep)
$bg.Dispose()

$src = [Drawing.Image]::FromFile($Icon)
function Scaled($size) {
    $b = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($b)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'HighQuality'
    $g.DrawImage($src, 0, 0, $size, $size); $g.Dispose()
    $ms = New-Object IO.MemoryStream; $b.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
    return ,$ms.ToArray()
}
[IO.File]::WriteAllBytes((Join-Path $res "logo.png"), (Scaled 128))
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = $sizes | ForEach-Object { ,(Scaled $_) }
$ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i] % 256
    $w.Write([byte]$s); $w.Write([byte]$s); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $root "src\app.ico"), $ms.ToArray())
$src.Dispose()

$size = [OffBlack]::Lift($Words, (Join-Path $res "wordmark.png"), 8, 4, 160)
"background.jpg, logo.png, app.ico, wordmark.png ($size) written"
