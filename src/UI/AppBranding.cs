using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using EasyHFP.Services;

namespace EasyHFP.UI
{
    public static class AppBranding
    {
        public const string EmbeddedIconResourceName = "EasyHFP.Resources.easyhfp.ico";
        public const string EmbeddedLogoResourceName = "EasyHFP.Resources.easyhfp_logo.png";

        public static byte[]? GetEmbeddedIconBytes()
        {
            try
            {
                var asm = typeof(AppBranding).Assembly;
                using Stream? stream = asm.GetManifestResourceStream(EmbeddedIconResourceName);
                if (stream == null) return null;

                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to read embedded icon bytes: {ex.Message}");
                return null;
            }
        }

        public static Icon CreateWindowIcon()
        {
            try
            {
                var asm = typeof(AppBranding).Assembly;
                using Stream? stream = asm.GetManifestResourceStream(EmbeddedIconResourceName);
                if (stream != null)
                {
                    return new Icon(stream);
                }

                string? exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    var assoc = Icon.ExtractAssociatedIcon(exePath);
                    if (assoc != null) return assoc;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load window icon from resources: {ex.Message}");
            }

            return (Icon)SystemIcons.Application.Clone();
        }

        public static Icon CreateIconForSize(int size)
        {
            try
            {
                var asm = typeof(AppBranding).Assembly;
                using Stream? stream = asm.GetManifestResourceStream(EmbeddedIconResourceName);
                if (stream != null)
                {
                    return new Icon(stream, new Size(size, size));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load {size}x{size} icon from resources: {ex.Message}");
            }

            return CreateWindowIcon();
        }

        public static Icon CreateTrayIcon()
        {
            Size smallSize = SystemInformation.SmallIconSize;
            int targetSize = smallSize.Width > 0 ? smallSize.Width : 16;
            return CreateIconForSize(targetSize);
        }

        public static Bitmap? CreateLogoBitmap()
        {
            try
            {
                var asm = typeof(AppBranding).Assembly;
                using Stream? stream = asm.GetManifestResourceStream(EmbeddedLogoResourceName);
                if (stream != null)
                {
                    using var temp = Image.FromStream(stream);
                    return new Bitmap(temp);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load embedded logo PNG: {ex.Message}");
            }

            return null;
        }
    }
}
