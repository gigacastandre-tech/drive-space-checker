$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;

public static class IconBuilder
{
    static byte[] Png(int size)
    {
        using (var bmp = new Bitmap(size, size))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var rect = new Rectangle(0, 0, size, size);
                using (var brush = new LinearGradientBrush(rect, Color.FromArgb(255, 37, 99, 235), Color.FromArgb(255, 23, 48, 133), 45f))
                    g.FillRectangle(brush, rect);

                float stroke = Math.Max(1.4f, size * 0.105f);
                float pad = size * 0.235f;
                float d = size - 2 * pad;
                using (var pen = new Pen(Color.White, stroke))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawEllipse(pen, pad, pad, d, d);
                }
                using (var pen = new Pen(Color.FromArgb(255, 74, 222, 128), stroke))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, pad, pad, d, d, -58, 116);
                }
                float hole = size * 0.115f;
                using (var b = new SolidBrush(Color.White))
                    g.FillEllipse(b, (size - hole) / 2f, (size - hole) / 2f, hole, hole);
            }

            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }

    public static void Build(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (var s in sizes) pngs.Add(Png(s));

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)pngs[i].Length);
                bw.Write((uint)offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) bw.Write(p);
        }
    }
}
'@

$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets\app.ico'
[IconBuilder]::Build($target)
Write-Output ("Created {0} ({1:N0} bytes)" -f $target, (Get-Item $target).Length)
