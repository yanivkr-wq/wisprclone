using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Serilog;

namespace WisprClone.App.Ui.Branding;

/// <summary>
/// Hand-built multi-resolution app icon for WisprClone: a stylised microphone
/// in white over a blue gradient circle. Rendered procedurally at 256×256
/// and downsampled with bicubic interpolation for each target size, then
/// packaged into a proper PNG-payload .ico file with entries for 16/32/48/64/256.
/// </summary>
public static class IconFactory
{
    private static readonly int[] DefaultSizes = { 16, 32, 48, 64, 256 };

    // Brand colours.
    private static readonly Color BrandBlueTop    = Color.FromArgb(0xFF, 0x3D, 0x7B, 0xEA);
    private static readonly Color BrandBlueBottom = Color.FromArgb(0xFF, 0x1D, 0x4E, 0xC7);

    public static void SaveAsIco(string path) => SaveAsIco(path, DefaultSizes);

    public static void SaveAsIco(string path, int[] sizes)
    {
        using var master = RenderMaster(256);

        var pngs = new List<byte[]>(sizes.Length);
        foreach (var size in sizes)
        {
            pngs.Add(RenderSizeFromMaster(master, size));
        }

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        // ICONDIR (6 bytes)
        w.Write((short)0);                // reserved
        w.Write((short)1);                // type 1 = ICO
        w.Write((short)sizes.Length);     // number of images

        // ICONDIRENTRY blocks (16 bytes each)
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            // bWidth/bHeight: 0 means 256 (the byte can only hold up to 255).
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);             // palette count (0 for 32bpp)
            w.Write((byte)0);             // reserved
            w.Write((short)1);            // color planes
            w.Write((short)32);           // bits per pixel
            w.Write(pngs[i].Length);      // image size (DWORD)
            w.Write(offset);              // offset to image data (DWORD)
            offset += pngs[i].Length;
        }

        // Image data
        foreach (var png in pngs)
        {
            w.Write(png);
        }

        Log.Information("Generated multi-res icon at {Path} (sizes: {Sizes}, total: {Bytes} bytes)",
            path, string.Join(",", sizes), new FileInfo(path).Length);
    }

    /// <summary>Renders the master 256×256 design that all sizes are derived from.</summary>
    private static Bitmap RenderMaster(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);

        DrawBackground(g, size);
        DrawMicrophone(g, size);

        return bmp;
    }

    private static byte[] RenderSizeFromMaster(Bitmap master, int size)
    {
        using var resized = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);

            // For tiny sizes (16, 24) bicubic downsampling from 256 produces a
            // slightly muddy result. Re-rendering directly at the target size
            // would be sharper but the micro-proportions get awkward.
            // Bicubic is a reasonable middle ground.
            g.DrawImage(master, new Rectangle(0, 0, size, size));
        }

        using var ms = new MemoryStream();
        resized.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static void DrawBackground(Graphics g, int size)
    {
        var rect = new Rectangle(0, 0, size, size);
        using var brush = new LinearGradientBrush(
            new Point(0, 0),
            new Point(0, size),
            BrandBlueTop,
            BrandBlueBottom);
        g.FillEllipse(brush, rect);

        // Subtle inner highlight at the top for depth.
        using var highlight = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
        g.FillEllipse(highlight,
            size * 0.18f, size * 0.10f, size * 0.64f, size * 0.30f);
    }

    private static void DrawMicrophone(Graphics g, int size)
    {
        var midX = size / 2f;
        var white = Color.White;
        var strokeWidth = size * 0.07f;

        using var fill = new SolidBrush(white);
        using var stroke = new Pen(white, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        // -- Mic capsule (vertical rounded rectangle) --
        var capW = size * 0.28f;
        var capH = size * 0.46f;
        var capX = midX - capW / 2f;
        var capY = size * 0.16f;
        using (var path = RoundedRect(capX, capY, capW, capH, capW / 2f))
        {
            g.FillPath(fill, path);
        }

        // -- U-shape cradle below the capsule --
        var cradleW = size * 0.52f;
        var cradleH = size * 0.22f;
        var cradleX = midX - cradleW / 2f;
        var cradleY = size * 0.52f;
        g.DrawArc(stroke, cradleX, cradleY, cradleW, cradleH, 0, 180);

        // -- Vertical stem from cradle bottom to base --
        var stemTopY = cradleY + cradleH / 2f;
        var stemBottomY = size * 0.84f;
        g.DrawLine(stroke, midX, stemTopY, midX, stemBottomY);

        // -- Horizontal base --
        var baseHalfW = size * 0.16f;
        g.DrawLine(stroke, midX - baseHalfW, stemBottomY, midX + baseHalfW, stemBottomY);
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        var d = r * 2f;
        path.AddArc(x,        y,        d, d, 180, 90);  // top-left
        path.AddArc(x + w - d, y,        d, d, 270, 90); // top-right
        path.AddArc(x + w - d, y + h - d, d, d, 0,   90); // bottom-right
        path.AddArc(x,        y + h - d, d, d, 90,  90); // bottom-left
        path.CloseFigure();
        return path;
    }
}
