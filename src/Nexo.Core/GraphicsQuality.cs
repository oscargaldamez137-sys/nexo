using System;

namespace Nexo.Core
{
    // Quality ceilings, not a benchmark or a guaranteed frame rate. Render an image
    // fitted inside the monitor (never stretch 4:3/16:9 to ultrawide), then supersample
    // up to the tier's budget. Native heights are nominal; PS2 games vary considerably.
    internal sealed class GraphicsQuality
    {
        internal int Ps2, Dolphin, RpcScale, Psp, EdenScale, Msaa, Xbox, Xbox360;
        internal GraphicsQuality(GpuInfo gpu)
        {
            int level = gpu.Tier == "Ultra" ? 3 : gpu.Tier == "Alto" ? 2 : gpu.Tier == "Medio" ? 1 : 0;
            double sample = level == 3 ? 1.5 : 1.0;
            // PCSX2 2.8.2's UI labels 4x ~1440p / 8x ~2880p (nominal 360p).
            Ps2 = Scale(gpu, 360, 16.0 / 9, sample, new[] { 3, 4, 6, 10 }[level]);
            // Dolphin's EFB is 640x528, before game-specific cropping/aspect correction.
            Dolphin = Scale(gpu, 528, 16.0 / 9, sample, new[] { 3, 4, 5, 8 }[level]);
            RpcScale = Math.Min(new[] { 150, 200, 250, 300 }[level], Math.Max(100,
                (int)Math.Ceiling(FittedHeight(gpu, 16.0 / 9) * sample / 720 / 0.25) * 25));
            Psp = Scale(gpu, 272, 480.0 / 272, sample, new[] { 4, 6, 8, 10 }[level]);
            // 4x is sixteen times the native pixel area; reserve it for Ultra >=16 GB.
            bool eden4 = level == 3 && gpu.VramBytes >= 16000000000L;
            EdenScale = Scale(gpu, 720, 16.0 / 9, sample, new[] { 1, 2, 3, eden4 ? 4 : 3 }[level]);
            Xbox = Scale(gpu, 480, 4.0 / 3, sample, new[] { 1, 2, 3, 4 }[level]);
            Xbox360 = Scale(gpu, 720, 16.0 / 9, sample, new[] { 1, 2, 3, 3 }[level]);
            Msaa = new[] { 1, 2, 4, 4 }[level];
        }
        private static int Scale(GpuInfo gpu, double nativeHeight, double aspect, double sample, int cap)
            => Math.Max(1, Math.Min(cap, (int)Math.Ceiling(FittedHeight(gpu, aspect) * sample / nativeHeight)));
        private static double FittedHeight(GpuInfo gpu, double aspect)
        {
            // Missing display data uses 1080p, bounded by the same GPU budgets.
            double height = gpu.MonitorHeight > 0 ? Math.Min(gpu.MonitorHeight, 32768) : 1080;
            return gpu.MonitorWidth > 0 ? Math.Min(height, gpu.MonitorWidth / aspect) : height;
        }
    }
}
