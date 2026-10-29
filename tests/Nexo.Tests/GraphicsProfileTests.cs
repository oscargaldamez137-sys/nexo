using System;
using System.IO;
using System.Linq;
using Nexo.Core;

internal static class GraphicsProfileTests
{
    private const uint Vk13 = (1u << 22) | (3u << 12);
    private static GpuInfo Gpu(bool vulkan = true) => new GpuInfo("NVIDIA GeForce RTX 4070 SUPER", 12L << 30, "NVIDIA", false, 3440, 1440, vulkan ? Vk13 : 0, 180, @"\\.\DISPLAY1");
    internal static void RunAll(Action<string, Action> run, string workspace)
    {
        void Test(string title, Action<NexoService> test) => run(title, () => {
            string root = Path.Combine(workspace, ".nexo-test-graphics-profile-" + Guid.NewGuid().ToString("N"));
            try { test(new NexoService(root)); }
            finally { if (SafePaths.Within(root, workspace) && Directory.Exists(root)) Directory.Delete(root, true); }
        });
        run("Motor: Vulkan se confirma por GPU exacta y API, nunca por VRAM sola", () => {
            True(Gpu().SupportsVulkan); True(!Gpu(false).SupportsVulkan);
            var adapters = new[] { Tuple.Create("Intel UHD", Vk13), Tuple.Create(" NVIDIA GeForce RTX 4070 SUPER ", Vk13) };
            Equal(Vk13, VulkanCapabilities.Match(Gpu().Name, adapters));
            Equal(0u, VulkanCapabilities.Match("AMD Radeon RX 7800 XT", adapters));
            var old = new GpuInfo(Gpu().Name, 12L << 30, "NVIDIA", false, 3440, 1440, (1u << 22) | (2u << 12));
            True(!old.SupportsVulkan); Equal("Ultra", old.Tier);
        });
        run("Pantalla: refresco conserva GPU y usa píxeles actuales sin asumir 1440p ni 180 Hz", () => {
            var previous = Gpu();
            var changed = GpuDetector.WithDisplay(previous, @"\\.\DISPLAY2", 3840, 2160, 120);
            Equal(3840, changed.MonitorWidth); Equal(2160, changed.MonitorHeight); Equal(120, changed.MonitorRefreshRate);
            Equal(@"\\.\DISPLAY2", changed.MonitorDeviceName); Equal(previous.Name, changed.Name); True(changed.SupportsVulkan);
            Equal(previous, GpuDetector.WithDisplay(previous, "bad", 0, 0, 0));
            Equal(0, GpuDetector.WithDisplay(previous, "unknown-hz", 3440, 1440, 1).MonitorRefreshRate);
        });
        run("Resolución: solo Ultra supersampling, límite útil también Xbox y Xenia", () => {
            var high = new GpuInfo("NVIDIA RTX 3070", 8L << 30, "NVIDIA", false, 2560, 1440, Vk13);
            var q = new GraphicsQuality(high);
            Equal(4, q.Ps2); Equal(3, q.Dolphin); Equal(200, q.RpcScale); Equal(6, q.Psp); Equal(2, q.EdenScale); Equal(2, q.Xbox360);
            var small = new GraphicsQuality(GpuDetector.WithDisplay(Gpu(), "small", 1280, 720, 60));
            Equal(3, small.Ps2); Equal(3, small.Xbox); Equal(2, small.Xbox360);
            var wide = new GraphicsQuality(Gpu()); var standard = new GraphicsQuality(GpuDetector.WithDisplay(Gpu(), "normal", 2560, 1440, 260));
            Equal(wide.Ps2, standard.Ps2); Equal(wide.EdenScale, standard.EdenScale);
        });
        Test("Perfil PS2: Vulkan, mipmapping y filtros de consola; respeta motor y mipmapping propios", service => {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); True(Prepare(s).Changed);
            var gs = Read(s, "inis/PCSX2.ini").ReadSection("EmuCore/GS");
            Equal("14", gs["Renderer"]); Equal("6", gs["upscale_multiplier"]); Equal("16", gs["MaxAnisotropy"]);
            Equal("true", gs["hw_mipmap"]); Equal("true", gs["mipmap"]); Equal("2", gs["filter"]); Equal("-1", gs["TriFilter"]); Equal("false", gs["DisableShaderCache"]);
            Edit(s, "inis/PCSX2.ini", t => t.Replace("Renderer = 14", "Renderer = 15").Replace("hw_mipmap = true", "hw_mipmap = false"));
            string before = Text(s, "inis/PCSX2.ini"); var result = Prepare(s);
            True(!result.Changed && result.Message.Contains("hw_mipmap")); Equal(before, Text(s, "inis/PCSX2.ini"));
        });
        Test("Perfil antiguo: recibo real permite 8x→6x sin atribuir ajustes manuales", service => {
            var s = Emu(service, "PS2", "pcsx2-qt.exe");
            Put(s, "inis/PCSX2.ini", "[EmuCore/GS]\nupscale_multiplier=8.000000\nMaxAnisotropy=8\nRenderer=15\n");
            var history = new GraphicsHistory(At(s, "inis/PCSX2.ini"), true); history.Record("EmuCore/GS", "upscale_multiplier", "8"); history.Save();
            True(Prepare(s).Changed); var gs = Read(s, "inis/PCSX2.ini").ReadSection("EmuCore/GS");
            Equal("6", gs["upscale_multiplier"]); Equal("8", gs["MaxAnisotropy"]); Equal("15", gs["Renderer"]);
            True(!Prepare(s).Changed);
        });
        Test("Perfil Xbox: OpenGL de fábrica→Vulkan, comillas TOML y datos BIOS intactos", service => {
            var s = Emu(service, "Xbox", "xemu.exe");
            Put(s, "xemu.toml", "[display]\nrenderer = 'OPENGL' # default\n[display.quality]\nsurface_scale=1\n[sys.files]\nflashrom_path='bios/flash.bin'\n");
            True(Prepare(s).Changed); var cfg = Read(s, "xemu.toml", true);
            Equal("\"VULKAN\"", cfg.ReadSection("display")["renderer"]); Equal("4", cfg.ReadSection("display.quality")["surface_scale"]);
            True(Text(s, "xemu.toml").Contains("flashrom_path='bios/flash.bin'")); True(!Prepare(s).Changed);
            Edit(s, "xemu.toml", t => t.Replace("renderer = \"VULKAN\"", "renderer = 'NULL'"));
            True(!Prepare(s).Changed); Equal("NULL", Read(s, "xemu.toml", true).ReadSection("display")["renderer"]);
        });
        Test("Motor: pasar a GPU sin Vulkan revierte solo selección gestionada por Nexo", service => {
            var s = Emu(service, "Xbox", "xemu.exe"); Prepare(s);
            True(Prepare(s, false).Changed); Equal("\"OPENGL\"", Read(s, "xemu.toml", true).ReadSection("display")["renderer"]);
            Put(s, "xemu.toml", "[display]\nrenderer='VULKAN'\n[display.quality]\nsurface_scale=4\n");
            True(!Prepare(s, false).Changed); Equal("VULKAN", Read(s, "xemu.toml", true).ReadSection("display")["renderer"]);
            var ps2 = Emu(service, "PS2", "pcsx2-qt.exe"); Prepare(ps2); Prepare(ps2, false);
            Equal("-1", Read(ps2, "inis/PCSX2.ini").ReadSection("EmuCore/GS")["Renderer"]);
        });
        Test("Perfil Dolphin: backend en Dolphin.ini, híbridos y precarga en GFX.ini", service => {
            var s = Emu(service, "GameCube-Wii", "Dolphin.exe");
            Put(s, "User/Config/Dolphin.ini", "[Core]\nGFXBackend = D3D\nCPUThread = True\n");
            True(Prepare(s).Changed); Equal("Vulkan", Read(s, "User/Config/Dolphin.ini").ReadSection("Core")["GFXBackend"]);
            var gfx = Read(s, "User/Config/GFX.ini"); var settings = gfx.ReadSection("Settings");
            Equal("5", settings["InternalResolution"]); Equal("4", settings["MSAA"]); Equal("2", settings["ShaderCompilationMode"]);
            Equal("true", settings["WaitForShadersBeforeStarting"]); Equal("true", settings["ShaderCache"]);
            True(!Text(s, "User/Config/GFX.ini").Contains("GFXBackend")); True(!Prepare(s).Changed);
            True(Prepare(s, false).Changed); Equal("D3D", Read(s, "User/Config/Dolphin.ini").ReadSection("Core")["GFXBackend"]);
        });
        Test("Perfil Dolphin: backend personal y modo shaders editado sobreviven al perfil", service => {
            var s = Emu(service, "GameCube-Wii", "Dolphin.exe"); Prepare(s);
            Edit(s, "User/Config/Dolphin.ini", t => t.Replace("GFXBackend = Vulkan", "GFXBackend = OGL"));
            Edit(s, "User/Config/GFX.ini", t => t.Replace("ShaderCompilationMode = 2", "ShaderCompilationMode = 1"));
            var result = Prepare(s); True(!result.Changed && result.Message.Contains("GFXBackend") && result.Message.Contains("ShaderCompilationMode"));
        });
        Test("Perfil PPSSPP: clave GraphicsBackend real, resolución fábrica 1 y ubershaders", service => {
            var s = Emu(service, "PSP", "PPSSPPWindows64.exe"); Prepare(s);
            var graphics = Read(s, "memstick/PSP/SYSTEM/ppsspp.ini").ReadSection("Graphics");
            Equal("3 (VULKAN)", graphics["GraphicsBackend"]); True(!graphics.ContainsKey("GPUBackend"));
            Equal("true", graphics["ShaderCache"]); Equal("true", graphics["UberShaderVertex"]); Equal("true", graphics["UberShaderFragment"]);
            Put(s, "memstick/PSP/SYSTEM/ppsspp.ini", "[Graphics]\nInternalResolution=1\nGraphicsBackend=2 (DIRECT3D11)\nShaderCache=false\n");
            True(Prepare(s).Changed); graphics = Read(s, "memstick/PSP/SYSTEM/ppsspp.ini").ReadSection("Graphics");
            Equal("8", graphics["InternalResolution"]); Equal("2 (DIRECT3D11)", graphics["GraphicsBackend"]); Equal("false", graphics["ShaderCache"]);
        });
        Test("Perfil Eden: Vulkan, cachés y default flags; ningún hack asíncrono", service => {
            var s = Emu(service, "Switch", "eden.exe"); Prepare(s);
            var renderer = Read(s, "user/config/qt-config.ini").ReadSection("Renderer");
            Equal("1", renderer["backend"]); Equal("true", renderer["use_disk_shader_cache"]); Equal("true", renderer["use_vulkan_driver_pipeline_cache"]);
            True(!renderer.ContainsKey("use_asynchronous_shaders"));
            Edit(s, "user/config/qt-config.ini", t => t.Replace("backend = 1", "backend = 0"));
            True(!Prepare(s).Changed); Equal("0", Read(s, "user/config/qt-config.ini").ReadSection("Renderer")["backend"]);
        });
        Test("Perfil RPCS3: global con Vulkan e intérprete; nunca perfiles por juego", service => {
            var s = Emu(service, "PS3", "rpcs3.exe"); Prepare(s);
            var video = Read(s, "config/config.yml").ReadYamlScalars("Video");
            Equal("Vulkan", video["Renderer"]); Equal("Async Recompiler with Shader Interpreter", video["Shader Mode"]);
            Equal("false", video["Disable On-Disk Shader Cache"]); Equal("300", video["Resolution Scale"]);
            True(!Directory.Exists(At(s, "config/custom_configs")));
        });
        Test("Perfil: fallo del segundo INI de Dolphin conserva sus bytes y no bloquea", service => {
            var s = Emu(service, "GameCube-Wii", "Dolphin.exe"); Put(s, "User/Config/Dolphin.ini", "[Core]\nGFXBackend=D3D\n");
            string before = Text(s, "User/Config/Dolphin.ini");
            using (File.Open(At(s, "User/Config/Dolphin.ini"), FileMode.Open, FileAccess.Read, FileShare.Read)) {
                var result = Prepare(s); True(result.Changed && result.Message.Contains("continuar"));
            }
            Equal(before, Text(s, "User/Config/Dolphin.ini"));
            True(!File.Exists(At(s, "User/Config/Dolphin.ini.nexo-graphics.json"))); True(Prepare(s).Changed);
        });
    }
    private static GraphicsSetupResult Prepare(SystemStatus s, bool vulkan = true) => GraphicsSetup.Prepare(s, Gpu(vulkan), new string[0]);
    private static SystemStatus Emu(NexoService service, string id, string exe) {
        string path = Path.Combine(service.GetStatus(id).EmulatorFolder, "package", exe); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "fixture"); return service.GetStatus(id);
    }
    private static string At(SystemStatus s, string relative) => Path.Combine(Path.GetDirectoryName(s.ExecutablePath), relative.Replace('/', '\\'));
    private static void Put(SystemStatus s, string relative, string text) { string p = At(s, relative); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllText(p, text); }
    private static string Text(SystemStatus s, string relative) => File.ReadAllText(At(s, relative));
    private static ControllerConfig Read(SystemStatus s, string relative, bool toml = false) => new ControllerConfig(At(s, relative), toml);
    private static void Edit(SystemStatus s, string relative, Func<string, string> edit) => Put(s, relative, edit(Text(s, relative)));
    private static void True(bool value) { if (!value) throw new Exception("Condición falsa."); }
    private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception("Esperado " + expected + "; recibido " + actual); }
}
