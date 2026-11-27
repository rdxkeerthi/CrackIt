using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StealthScreenAI
{
    public static class ScreenCaptureService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(
            IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
            IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        private const int SRCCOPY = 0x00CC0020;

        public static Rectangle GetActiveScreenBounds()
        {
            try
            {
                IntPtr fg = NativeWin32.GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    var screenFromHwnd = Screen.FromHandle(fg);
                    if (screenFromHwnd != null && screenFromHwnd.Bounds.Width > 0)
                    {
                        return screenFromHwnd.Bounds;
                    }
                }
                var screenFromPoint = Screen.FromPoint(Cursor.Position);
                if (screenFromPoint != null && screenFromPoint.Bounds.Width > 0)
                {
                    return screenFromPoint.Bounds;
                }
            }
            catch { }

            return Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        }

        public static string CaptureFullScreenBase64()
        {
            Rectangle bounds = GetActiveScreenBounds();
            return CaptureRegionBase64(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        public static string CaptureRegionBase64(int x, int y, int width, int height)
        {
            if (width <= 0) width = 100;
            if (height <= 0) height = 100;

            IntPtr hDeskDC = GetDC(IntPtr.Zero);
            if (hDeskDC == IntPtr.Zero)
            {
                // Fallback mock image if running in a headless/non-interactive terminal
                return GenerateFallbackBase64(width, height);
            }

            try
            {
                IntPtr hMemDC = CreateCompatibleDC(hDeskDC);
                if (hMemDC == IntPtr.Zero)
                {
                    return GenerateFallbackBase64(width, height);
                }

                try
                {
                    IntPtr hBitmap = CreateCompatibleBitmap(hDeskDC, width, height);
                    if (hBitmap == IntPtr.Zero)
                    {
                        return GenerateFallbackBase64(width, height);
                    }

                    try
                    {
                        IntPtr hOldBmp = SelectObject(hMemDC, hBitmap);
                        BitBlt(hMemDC, 0, 0, width, height, hDeskDC, x, y, SRCCOPY);
                        SelectObject(hMemDC, hOldBmp);

                        using var bmp = Image.FromHbitmap(hBitmap);
                        using var ms = new MemoryStream();
                        SaveHighQualityJpeg(bmp, ms, 92L);
                        return Convert.ToBase64String(ms.ToArray());
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
                finally
                {
                    DeleteDC(hMemDC);
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hDeskDC);
            }
        }

        private static void SaveHighQualityJpeg(Image image, Stream output, long quality)
        {
            ImageCodecInfo? jpegCodec = null;
            foreach (var codec in ImageCodecInfo.GetImageEncoders())
            {
                if (codec.FormatID == ImageFormat.Jpeg.Guid)
                {
                    jpegCodec = codec;
                    break;
                }
            }

            if (jpegCodec != null)
            {
                using var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                image.Save(output, jpegCodec, encoderParams);
            }
            else
            {
                image.Save(output, ImageFormat.Jpeg);
            }
        }

        private static string GenerateFallbackBase64(int width, int height)
        {
            using var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
            }
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Jpeg);
            return Convert.ToBase64String(ms.ToArray());
        }
    }
}
