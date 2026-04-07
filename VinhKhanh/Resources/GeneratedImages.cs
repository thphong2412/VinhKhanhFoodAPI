using System;
using System.IO;
using Microsoft.Maui.Controls;

namespace VinhKhanh.Resources
{
    // Small generated placeholders as base64 PNGs. Replace with real images later.
    internal static class GeneratedImages
    {
        // 1x1 transparent png placeholder
        public const string TransparentPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=";

        // Use same placeholder for now; replace content with actual base64 images if available
        public static ImageSource GetDirections() => ImageSource.FromStream(() => new MemoryStream(Convert.FromBase64String(TransparentPng)));
        public static ImageSource GetNarration() => ImageSource.FromStream(() => new MemoryStream(Convert.FromBase64String(TransparentPng)));
        public static ImageSource GetShare() => ImageSource.FromStream(() => new MemoryStream(Convert.FromBase64String(TransparentPng)));
        public static ImageSource GetSave() => ImageSource.FromStream(() => new MemoryStream(Convert.FromBase64String(TransparentPng)));
    }
}
