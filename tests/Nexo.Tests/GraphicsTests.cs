using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexo.Core;

internal static class GraphicsTests
{
    private static readonly GpuInfo Low = GpuDetector.Select(new[] { new GpuAdapter("Intel UHD Graphics", 1073741824L) }, 2560, 1440);
    private static readonly GpuInfo Mid = GpuDetector.Select(new[] { new GpuAdapter("NVIDIA GTX 1650", 4294967296L) }, 2560, 1440);
    private static readonly GpuInfo High = GpuDetector.Select(new[] { new GpuAdapter("NVIDIA RTX 3070", 8L << 30) }, 2560, 1440);
    private static readonly GpuInfo Ultra = GpuDetector.Select(new[] { new GpuAdapter("NVIDIA GeForce RTX 4070 SUPER", 12878610432L) }, 3440, 1440);

    internal static void RunAll(Action<string, Action> run, string workspace)
    {
        GraphicsProfileTests.RunAll(run, workspace);
        void Test(string name, Action<string, NexoService> test) => run(name, () =>
        {
            string root = Path.Combine(workspace, ".nexo-test-graphics-" + Guid.NewGuid().ToString("N"));
            try { test(root, new NexoService(root)); }
            finally
            {
                if (!SafePaths.Within(root, workspace) || !Path.GetFileName(root).StartsWith(".nexo-test-graphics-")) throw new Exception("Limpieza fuera del proyecto.");
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        run("GPU: VRAM QWORD y binaria de 12 GB sin truncar; DWORD no fiable", () =>
        {
            Equal(12878610432L, GpuDetector.ReadMemory(12878610432L));
            Equal(12878610432L, GpuDetector.ReadMemory(BitConverter.GetBytes(12878610432L)));
            Equal(0L, GpuDetector.ReadMemory((uint)uint.MaxValue));
            Equal(0L, GpuDetector.ReadMemory(-1L)); Equal(0L, GpuDetector.ReadMemory(ulong.MaxValue));
            Equal(0L, GpuDetector.ReadMemory(null)); Equal(0L, GpuDetector.ReadMemory(new byte[4]));
        });
        run("GPU: selecciona más VRAM, conserva fabricante y píxeles físicos", () =>
        {
            var gpu = GpuDetector.Select(new[] { new GpuAdapter("Intel UHD", 1L << 30), new GpuAdapter("NVIDIA GeForce RTX 4070 SUPER", 12878610432L), new GpuAdapter("AMD Radeon RX 6600", 8L << 30) }, 3440, 1440);
            Equal("NVIDIA", gpu.Vendor); Equal("Ultra", gpu.Tier); True(!gpu.IsIntegrated);
            Equal(12878610432L, gpu.VramBytes); Equal(3440, gpu.MonitorWidth); Equal(1440, gpu.MonitorHeight);
        });
        run("GPU: integrada con reserva grande sigue en Bajo; sin GPU usa Bajo", () =>
        {
            foreach (string name in new[] { "Intel(R) UHD Graphics 770", "Intel Arc 140V GPU", "AMD Radeon(TM) Graphics", "AMD Radeon 780M" })
            { var gpu = GpuDetector.Select(new[] { new GpuAdapter(name, 8L << 30) }); Equal("Bajo", gpu.Tier); True(gpu.IsIntegrated); }
            Equal("Bajo", GpuDetector.Select(new GpuAdapter[0]).Tier);
            Equal("Bajo", GpuDetector.Select(new[] { new GpuAdapter("NVIDIA desconocida", null) }).Tier);
            Equal("Bajo", GpuDetector.Select(new[] { new GpuAdapter("Microsoft Basic Display Adapter", 8L << 30) }).Tier);
        });
        run("GPU: umbrales de 3/6 GB y Arc discreta", () =>
        {
            Equal("Bajo", Low.Tier); Equal("Medio", Mid.Tier); Equal("Alto", High.Tier); Equal("Ultra", Ultra.Tier);
            Equal("Bajo", GpuDetector.Select(new[] { new GpuAdapter("NVIDIA", 2999999999L) }).Tier);
            Equal("Medio", GpuDetector.Select(new[] { new GpuAdapter("NVIDIA", 3000000000L) }).Tier);
            Equal("Alto", GpuDetector.Select(new[] { new GpuAdapter("NVIDIA", 6000000000L) }).Tier);
            var arc = GpuDetector.Select(new[] { new GpuAdapter("Intel(R) Arc(TM) A770 Graphics", 16L << 30) });
            True(!arc.IsIntegrated); Equal("Alto", arc.Tier);
            Equal("AMD", GpuDetector.Select(new[] { new GpuAdapter("Unknown RX 7600", 8L << 30, "PCI\\VEN_1002") }).Vendor);
        });
        Test("Gráficos PS2: nuevo INI, bootstrap y controles compatibles en cualquier orden", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); True(Prepare(s).Changed);
            var ini = Config(s, "inis/PCSX2.ini");
            Equal("6", ini.ReadSection("EmuCore/GS")["upscale_multiplier"]);
            Equal("16", ini.ReadSection("EmuCore/GS")["MaxAnisotropy"]);
            Equal("true", ini.ReadSection("EmuCore")["EnableWideScreenPatches"]);
            Equal("1", ini.ReadSection("UI")["SettingsVersion"]);
            Equal("-1", ini.ReadSection("EmuCore/GS")["Renderer"]);
            True(ControllerSetup.Prepare(s, 0, new string[0]).Changed);
            True(Read(s, "inis/PCSX2.ini").Contains("Cross = XInput-0/A"));
            True(!Prepare(s).Changed);
            Equal("6", Config(s, "inis/PCSX2.ini").ReadSection("EmuCore/GS")["upscale_multiplier"]);
        });
        Test("Gráficos PS2: defaults mejoran sin cambiar renderer, BIOS, filtros ni mandos", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe");
            Write(s, "inis/PCSX2.ini", "[UI]\nSettingsVersion = 1\n[EmuCore/GS]\nupscale_multiplier = 1.000000\nMaxAnisotropy = 0\nRenderer = -1\nfilter = 2\nTriFilter = -1\n[EmuCore]\nEnableWideScreenPatches = false\nEnableNoInterlacingPatches = false\n[Filenames]\nBIOS = mia.bin\n[Pad1]\nCross = mi tecla\n");
            True(Prepare(s).Changed); var text = Read(s, "inis/PCSX2.ini");
            True(text.Contains("upscale_multiplier = 6") && text.Contains("EnableNoInterlacingPatches = true"));
            True(text.Contains("Renderer = -1\nfilter = 2\nTriFilter = -1"));
            True(text.Contains("BIOS = mia.bin\n[Pad1]\nCross = mi tecla"));
        });
        Test("Gráficos: personalizados conservan bytes y avisan, sin reescritura por formato", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); Write(s, "portable.ini", "");
            const string text = "[EmuCore/GS]\nupscale_multiplier=6\nMaxAnisotropy=8\n[EmuCore]\nEnableWideScreenPatches=true\nEnableNoInterlacingPatches=true";
            Write(s, "inis/PCSX2.ini", text);
            var result = Prepare(s); True(!result.Changed && result.Message.Contains("personalizados"));
            Equal(text, Read(s, "inis/PCSX2.ini"));
        });
        Test("Gráficos: respeta cada clave personalizada mientras mejora las demás", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); Write(s, "inis/PCSX2.ini", "[EmuCore/GS]\nupscale_multiplier = 6\nMaxAnisotropy = 0\n");
            var result = Prepare(s); True(result.Changed && result.Message.Contains("upscale_multiplier"));
            var values = Config(s, "inis/PCSX2.ini").ReadSection("EmuCore/GS"); Equal("6", values["upscale_multiplier"]); Equal("16", values["MaxAnisotropy"]);
        });
        Test("Gráficos: los cuatro niveles usan las escalas de cada formato real a 1440p", (root, service) =>
        {
            var profiles = new[] {
                new[] { "PS2", "pcsx2-qt.exe", "inis/PCSX2.ini", "EmuCore/GS", "upscale_multiplier", "3", "4", "4", "6" },
                new[] { "GameCube-Wii", "Dolphin.exe", "User/Config/GFX.ini", "Settings", "InternalResolution", "3", "3", "3", "5" },
                new[] { "PSP", "PPSSPPWindows64.exe", "memstick/PSP/SYSTEM/ppsspp.ini", "Graphics", "InternalResolution", "4", "6", "6", "8" },
                new[] { "Switch", "eden.exe", "user/config/qt-config.ini", "Renderer", "resolution_setup", "3", "6", "6", "7" }
            };
            foreach (var p in profiles)
            {
                var s = Emu(service, p[0], p[1]); int level = 0;
                foreach (var gpu in new[] { Low, Mid, High, Ultra })
                {
                    if (File.Exists(At(s, p[2]))) File.Delete(At(s, p[2]));
                    var result = Prepare(s, gpu); True(result.Changed); Equal(gpu.Tier, result.Tier);
                    Equal(p[5 + level++], Config(s, p[2]).ReadSection(p[3])[p[4]]);
                }
            }
            var ps3 = Emu(service, "PS3", "rpcs3.exe"); int index = 0;
            foreach (var gpu in new[] { Low, Mid, High, Ultra })
            { if (File.Exists(At(ps3, "config/config.yml"))) File.Delete(At(ps3, "config/config.yml")); True(Prepare(ps3, gpu).Changed); Equal(new[] { "150", "200", "200", "300" }[index++], Config(ps3, "config/config.yml").ReadYamlScalars("Video")["Resolution Scale"]); }
        });
        Test("Gráficos Dolphin: enum aniso -1→4, MSAA 4x y sin hack panorámico", (root, service) =>
        {
            var s = Emu(service, "GameCube-Wii", "Dolphin.exe");
            Write(s, "User/Config/GFX.ini", "[Settings]\nInternalResolution = 1\nMSAA = 1\nwideScreenHack = False\n[Enhancements]\nMaxAnisotropy = -1\n");
            True(Prepare(s).Changed); var file = Config(s, "User/Config/GFX.ini");
            Equal("4", file.ReadSection("Enhancements")["MaxAnisotropy"]); Equal("4", file.ReadSection("Settings")["MSAA"]);
            Equal("False", file.ReadSection("Settings")["wideScreenHack"]); True(File.Exists(At(s, "portable.txt")));
            Write(s, "User/Config/GFX.ini", "[Settings]\nSSAA = True\nMSAA = 1\n[Enhancements]\nMaxAnisotropy = 0\n");
            True(Prepare(s).Message.Contains("SSAA/MSAA"));
            Equal("1", Config(s, "User/Config/GFX.ini").ReadSection("Settings")["MSAA"]);
            Equal("0", Config(s, "User/Config/GFX.ini").ReadSection("Enhancements")["MaxAnisotropy"]);
        });
        Test("Gráficos RPCS3: YAML global conserva nested Vulkan y opciones de precisión", (root, service) =>
        {
            var s = Emu(service, "PS3", "rpcs3.exe");
            const string nested = "  Vulkan:\n    Adapter: NVIDIA\n    Custom: true\n";
            Write(s, "config.yml", "Core:\n  PPU Decoder: Recompiler (LLVM)\nVideo:\n  Renderer: Vulkan\n  Resolution Scale: 100\n  Anisotropic Filter Override: 0\n  Strict Rendering Mode: false\n" + nested + "Audio:\n  Renderer: Cubeb\n");
            True(Prepare(s).Changed); var text = Read(s, "config.yml");
            True(text.Contains("  Resolution Scale: 300\n") && text.Contains("  Anisotropic Filter Override: 16\n"));
            True(text.Contains(nested) && text.Contains("Strict Rendering Mode: false"));
            True(text.Contains("Audio:\n  Renderer: Cubeb")); True(!Prepare(s).Changed);
            Write(s, "config.yml", text.Replace("Resolution Scale: 300", "Resolution Scale: 250").Replace("Anisotropic Filter Override: 16", "Anisotropic Filter Override: 8"));
            var before = File.ReadAllBytes(At(s, "config.yml")); True(!Prepare(s).Changed); True(before.SequenceEqual(File.ReadAllBytes(At(s, "config.yml"))));
        });
        Test("RPCS3 real: config/config.yml tiene prioridad y migra stub demostrado", (root, service) =>
        {
            var s = Emu(service, "PS3", "rpcs3.exe"); OwnedStub(s);
            Write(s, "config/config.yml", "Video:\n  Resolution Scale: 100\n  Anisotropic Filter Override: 0\n  Vulkan:\n    Asynchronous Texture Streaming: true\n");
            Write(s, "GuiConfigs/config_database.dat", "database intacta");
            var result = Prepare(s); True(result.Changed && result.Message.Contains("retiró"));
            Equal("300", Config(s, "config/config.yml").ReadYamlScalars("Video")["Resolution Scale"]);
            True(Read(s, "config/config.yml").Contains("    Asynchronous Texture Streaming: true"));
            True(!File.Exists(At(s, "config.yml")) && !File.Exists(At(s, "config.yml.nexo-graphics.json")));
            Equal("database intacta", Read(s, "GuiConfigs/config_database.dat"));
            True(!Directory.Exists(At(s, "config/custom_configs"))); True(!Prepare(s).Changed);
        });
        Test("RPCS3: stub nuestro sin config moderna se recrea en ruta actual", (root, service) =>
        {
            var s = Emu(service, "PS3", "rpcs3.exe"); OwnedStub(s);
            True(Prepare(s).Changed); True(!File.Exists(At(s, "config.yml")));
            Equal("300", Config(s, "config/config.yml").ReadYamlScalars("Video")["Resolution Scale"]);
        });
        Test("RPCS3: migración respeta valores modernos personalizados y perfiles por juego", (root, service) =>
        {
            var s = Emu(service, "PS3", "rpcs3.exe"); OwnedStub(s);
            const string custom = "Video:\n  Resolution Scale: 125\n  Anisotropic Filter Override: 8\n";
            Write(s, "config/config.yml", custom); Write(s, "config/custom_configs/config_BLES00759.yml", custom);
            var result = Prepare(s); True(result.Changed && result.Message.Contains("personalizados"));
            Equal(custom, Read(s, "config/config.yml")); Equal(custom, Read(s, "config/custom_configs/config_BLES00759.yml"));
            True(!File.Exists(At(s, "config.yml")));
        });
        Test("RPCS3: raíz sin recibo, editada, con datos extra o recibo corrupto no se retira", (root, service) =>
        {
            var s = Emu(service, "PS3", "rpcs3.exe");
            Write(s, "config/config.yml", "Video:\n  Resolution Scale: 125\n  Anisotropic Filter Override: 8\n");
            for (int i = 0; i < 5; i++)
            {
                OwnedStub(s);
                if (i == 0) File.Delete(At(s, "config.yml.nexo-graphics.json"));
                if (i == 1) Write(s, "config.yml", Read(s, "config.yml").Replace("300", "250"));
                if (i == 2) Write(s, "config.yml", Read(s, "config.yml") + "Audio:\n  Renderer: Cubeb\n");
                if (i == 3) Write(s, "config.yml.nexo-graphics.json", "{roto");
                if (i == 4) Write(s, "config.yml", "# personalizado\n" + Read(s, "config.yml"));
                string before = Read(s, "config.yml"); True(!Prepare(s).Changed); Equal(before, Read(s, "config.yml"));
            }
        });
        Test("RPCS3: fallo al guardar destino conserva stub y recibo antiguos", (root, service) =>
        {
            var s = Emu(service, "PS3", "rpcs3.exe"); OwnedStub(s);
            Write(s, "config/config.yml", "Video:\n  Resolution Scale: 100\n  Anisotropic Filter Override: 0\n");
            using (File.Open(At(s, "config/config.yml"), FileMode.Open, FileAccess.Read, FileShare.Read))
            { var result = Prepare(s); True(!result.Changed && result.Message.Contains("continuar")); }
            True(File.Exists(At(s, "config.yml")) && File.Exists(At(s, "config.yml.nexo-graphics.json")));
        });
        Test("Gráficos PPSSPP: auto mejora a 8x; aniso 16x ya es fábrica; memoria redirigida intacta", (root, service) =>
        {
            var s = Emu(service, "PSP", "PPSSPPWindows64.exe");
            Write(s, "memstick/PSP/SYSTEM/ppsspp.ini", "[Graphics]\nInternalResolution = 0\nAnisotropyLevel = 4\nTextureScalingLevel = 1\nGPUBackend = 0\n");
            True(Prepare(s).Changed); var values = Config(s, "memstick/PSP/SYSTEM/ppsspp.ini").ReadSection("Graphics");
            Equal("8", values["InternalResolution"]); Equal("4", values["AnisotropyLevel"]); Equal("1", values["TextureScalingLevel"]); Equal("0", values["GPUBackend"]);
            Write(s, "installed.txt", "D:\\partidas"); var before = Read(s, "memstick/PSP/SYSTEM/ppsspp.ini");
            True(!Prepare(s).Changed); Equal(before, Read(s, "memstick/PSP/SYSTEM/ppsspp.ini"));
        });
        Test("Gráficos Eden: enum 3→7, aniso 0→5 y flags default desactivados", (root, service) =>
        {
            var s = Emu(service, "Switch", "eden.exe");
            Write(s, "user/config/qt-config.ini", "[Renderer]\nresolution_setup=3\nresolution_setup\\default=true\nmax_anisotropy=0\nmax_anisotropy\\default=true\nbackend=1\n[Controls]\nplayer_0_button_a=mi guid\n");
            True(Prepare(s).Changed); var d = Config(s, "user/config/qt-config.ini").ReadSection("Renderer");
            Equal("7", d["resolution_setup"]); Equal("5", d["max_anisotropy"]);
            Equal("false", d[@"resolution_setup\default"]); Equal("false", d[@"max_anisotropy\default"]);
            Equal("1", d["backend"]); True(Read(s, "user/config/qt-config.ini").Contains("player_0_button_a=mi guid"));
            True(!Prepare(s).Changed);
        });
        Test("Gráficos Eden: flags ambiguos, valores propios y otros forks se conservan", (root, service) =>
        {
            var s = Emu(service, "Switch", "eden.exe");
            foreach (string text in new[] { "[Renderer]\nresolution_setup=8\nresolution_setup\\default=false\nmax_anisotropy=3\n", "[Renderer]\nresolution_setup=7\nresolution_setup\\default=true\nmax_anisotropy=3\n", "[Renderer]\nresolution_setup=3\nresolution_setup\\default=desconocido\nmax_anisotropy=3\n" })
            { Write(s, "user/config/qt-config.ini", text); True(!Prepare(s).Changed); Equal(text, Read(s, "user/config/qt-config.ini")); }
            File.Move(s.ExecutablePath, At(s, "yuzu.exe")); s = service.GetStatus("Switch");
            True(Prepare(s).Message.Contains("Solo está verificado"));
        });
        Test("Gráficos RetroArch: integer limpio, default smooth falso y shaders intactos", (root, service) =>
        {
            var s = Emu(service, "Retro", "retroarch.exe");
            Write(s, "retroarch.cfg", "# mi config\nvideo_scale_integer = \"false\"\nvideo_smooth = \"false\"\nvideo_shader_enable = \"true\"\ninput_driver = \"sdl2\"\n");
            True(Prepare(s).Changed); var text = Read(s, "retroarch.cfg");
            True(text.Contains("video_scale_integer = \"true\"")); True(text.Contains("video_shader_enable = \"true\"")); True(!Prepare(s).Changed);
            Write(s, "retroarch.cfg", "video_scale_integer = \"true\"\nvideo_smooth = \"true\"\n");
            var r = Prepare(s); True(!r.Changed && r.Message.Contains("video_smooth"));
            Equal("video_scale_integer = \"true\"\nvideo_smooth = \"true\"\n", Read(s, "retroarch.cfg"));
            File.Delete(At(s, "retroarch.cfg")); True(Prepare(s).Changed);
        });
        Test("Gráficos: no-op idempotente; borrar configuración permite reaplicar", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); Prepare(s);
            File.SetLastWriteTimeUtc(At(s, "inis/PCSX2.ini"), new DateTime(2020, 1, 1));
            File.SetLastWriteTimeUtc(At(s, "portable.ini"), new DateTime(2020, 1, 1));
            True(!Prepare(s).Changed); Equal(2020, File.GetLastWriteTimeUtc(At(s, "inis/PCSX2.ini")).Year); Equal(2020, File.GetLastWriteTimeUtc(At(s, "portable.ini")).Year);
            File.Delete(At(s, "inis/PCSX2.ini")); True(Prepare(s).Changed);
        });
        Test("Gráficos: INI duplicado, YAML alias y CFG include se conservan sin temporales", (root, service) =>
        {
            foreach (var p in new[] {
                new[] { "PS2", "pcsx2-qt.exe", "inis/PCSX2.ini", "[EmuCore/GS]\nMaxAnisotropy=0\nMaxAnisotropy=8\n" },
                new[] { "PS3", "rpcs3.exe", "config.yml", "Video:\n  <<: *perfil\n  Resolution Scale: 100\n" },
                new[] { "PS3", "rpcs3.exe", "config.yml", "Video:\n  Resolution Scale: 100\nVideo:\n  Resolution Scale: 250\n" },
                new[] { "Retro", "retroarch.exe", "retroarch.cfg", "#include \"ajustes.cfg\"\nvideo_scale_integer = \"false\"\n" } })
            {
                var s = Emu(service, p[0], p[1]); Write(s, p[2], p[3]); var r = Prepare(s);
                True(!r.Changed && r.Message.Contains("continuar")); Equal(p[3], Read(s, p[2]));
                Equal(0, Directory.GetFiles(s.EmulatorFolder, "*.tmp", SearchOption.AllDirectories).Length);
            }
        });
        Test("Gráficos: PS4/PS5 no escriben nada; externos tampoco", (root, service) =>
        {
            foreach (var p in new[] { new[] { "PS4", "shadPS4.exe" }, new[] { "PS5", "kyty_emulator.exe" } })
            { var s = Emu(service, p[0], p[1]); int before = Directory.GetFileSystemEntries(Path.GetDirectoryName(s.ExecutablePath)).Length; True(!Prepare(s).Changed); Equal(before, Directory.GetFileSystemEntries(Path.GetDirectoryName(s.ExecutablePath)).Length); }
            string external = Path.Combine(root, "externo.exe"); File.WriteAllText(external, "fixture"); service.SetExecutable("PS2", external);
            True(!Prepare(service.GetStatus("PS2")).Changed); True(!File.Exists(Path.Combine(root, "portable.ini")));
        });
        Test("Gráficos: activar portable no oculta datos globales ni rutas redirigidas", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); string global = Path.Combine(root, "global"); Directory.CreateDirectory(global); File.WriteAllText(Path.Combine(global, "partida"), "save");
            var r = GraphicsSetup.Prepare(s, Ultra, new[] { global }); True(!r.Changed && r.Message.Contains("perfil de Windows")); True(!File.Exists(At(s, "inis/PCSX2.ini")));
            Write(s, "portable.txt", "D:\\miPerfil"); True(!Prepare(s).Changed);
            var retro = Emu(service, "Retro", "retroarch.exe"); True(!GraphicsSetup.Prepare(retro, Ultra, new[] { global }).Changed); True(!File.Exists(At(retro, "retroarch.cfg")));
        });
        Test("Gráficos: E/S fallida mantiene bytes y no deja temporales", (root, service) =>
        {
            var s = Emu(service, "PSP", "PPSSPPWindows64.exe"); Write(s, "memstick/PSP/SYSTEM/ppsspp.ini", "[Graphics]\nInternalResolution = 0\n");
            string before = Read(s, "memstick/PSP/SYSTEM/ppsspp.ini");
            using (File.Open(At(s, "memstick/PSP/SYSTEM/ppsspp.ini"), FileMode.Open, FileAccess.Read, FileShare.Read))
            { var r = Prepare(s); True(!r.Changed && r.Message.Contains("continuar")); }
            Equal(before, Read(s, "memstick/PSP/SYSTEM/ppsspp.ini")); Equal(0, Directory.GetFiles(s.EmulatorFolder, "*.tmp", SearchOption.AllDirectories).Length);
        });
        Test("Gráficos: ambos lanzamientos continúan tras fallo y exponen resultado", (root, unused) =>
        {
            int launches = 0, graphics = 0, detectors = 0;
            var service = new NexoService(root, s => new ControllerSetupResult(false, "fixture"), info => { launches++; return 123; },
                () => { detectors++; return Ultra; }, (s, g) => { graphics++; throw new IOException("Prueba gráfica de E/S"); });
            var s2 = Emu(service, "PS2", "pcsx2-qt.exe"); string game = Path.Combine(s2.GamesFolder, "Juego.iso"); File.WriteAllText(game, "fixture");
            Equal(123, service.LaunchEmulator("PS2")); Equal(123, service.LaunchGame(new GameEntry("PS2", "Juego", game)));
            Equal(2, launches); Equal(2, graphics); Equal(1, detectors); Equal("Ultra", service.LastGraphicsSetup.Tier);
            True(service.LastGraphicsSetup.Message.Contains("Prueba gráfica de E/S")); Equal(Ultra, service.GpuInfo);
        });
        Test("Gráficos: fallo real de escritura tampoco bloquea y detector fallido usa Bajo", (root, unused) =>
        {
            var service = new NexoService(root, s => new ControllerSetupResult(false, "fixture"), info => 99,
                () => throw new IOException("GPU"), (s, gpu) => Prepare(s, gpu));
            var s2 = Emu(service, "PS2", "pcsx2-qt.exe"); Write(s2, "inis", "impide crear carpeta");
            Equal(99, service.LaunchEmulator("PS2")); Equal("Bajo", service.GpuInfo.Tier);
            True(!service.LastGraphicsSetup.Changed && service.LastGraphicsSetup.Message.Contains("continuar"));
        });
        run("GPU Ultra: capacidad y clase conocidas, no solo 10 GB", () =>
        {
            Equal("Alto", GpuDetector.Select(new[] { new GpuAdapter("NVIDIA RTX 3080", 9999999999L) }).Tier);
            Equal("Ultra", GpuDetector.Select(new[] { new GpuAdapter("NVIDIA RTX 3080", 10000000000L) }).Tier);
            Equal("Ultra", GpuDetector.Select(new[] { new GpuAdapter("AMD Radeon RX 7800 XT", 16L << 30) }).Tier);
            foreach (string name in new[] { "NVIDIA GTX 1080 Ti", "NVIDIA RTX 3060", "NVIDIA RTX 4060 Ti", "Intel Arc A770", "AMD Radeon RX 6700 XT", "NVIDIA desconocida" })
                Equal("Alto", GpuDetector.Select(new[] { new GpuAdapter(name, 16L << 30) }).Tier);
        });
        run("Calidad: monitor 1080p, 1440p y 4K; límites y supersampling reales", () =>
        {
            var fullHd = new GraphicsQuality(GpuDetector.Select(new[] { new GpuAdapter(Ultra.Name, Ultra.VramBytes) }, 1920, 1080));
            Equal(5, fullHd.Ps2); Equal(4, fullHd.Dolphin); Equal(225, fullHd.RpcScale); Equal(6, fullHd.Psp);
            var qhd = new GraphicsQuality(Ultra); Equal(6, qhd.Ps2); Equal(5, qhd.Dolphin); Equal(300, qhd.RpcScale); Equal(8, qhd.Psp); Equal(3, qhd.EdenScale);
            var ultra4k = new GraphicsQuality(GpuDetector.Select(new[] { new GpuAdapter("NVIDIA RTX 4080", 16L << 30) }, 3840, 2160));
            Equal(9, ultra4k.Ps2); Equal(7, ultra4k.Dolphin); Equal(300, ultra4k.RpcScale); Equal(10, ultra4k.Psp); Equal(4, ultra4k.EdenScale);
            var vram16 = new GraphicsQuality(GpuDetector.Select(new[] { new GpuAdapter("NVIDIA RTX 4080", 16L << 30) }, 2560, 1440));
            Equal(3, vram16.EdenScale);
            var portrait = new GraphicsQuality(GpuDetector.Select(new[] { new GpuAdapter(Ultra.Name, Ultra.VramBytes) }, 1080, 1920));
            True(portrait.Ps2 < qhd.Ps2); // fit 16:9 into portrait, don't render 1920 lines of black bars
            var missing = new GraphicsQuality(GpuDetector.Select(new[] { new GpuAdapter(Ultra.Name, Ultra.VramBytes) }));
            Equal(fullHd.Ps2, missing.Ps2); Equal(fullHd.Psp, missing.Psp);
        });
        Test("Procedencia: subida Alto→Ultra tras reabrir, sin tocar mandos ni otro ajuste", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); True(Prepare(s, High).Changed);
            Equal("4", Config(s, "inis/PCSX2.ini").ReadSection("EmuCore/GS")["upscale_multiplier"]);
            string path = At(s, "inis/PCSX2.ini");
            // An emulator save can change spacing and numerics without changing a setting.
            File.WriteAllText(path, Read(s, "inis/PCSX2.ini").Replace("upscale_multiplier = 4", "upscale_multiplier=4.000000").Replace("MaxAnisotropy = 16", "MaxAnisotropy = 8"));
            var reopened = new NexoService(root); var r = Prepare(reopened.GetStatus("PS2"));
            True(r.Changed && r.Message.Contains("MaxAnisotropy"));
            var d = Config(s, "inis/PCSX2.ini").ReadSection("EmuCore/GS"); Equal("6", d["upscale_multiplier"]); Equal("8", d["MaxAnisotropy"]);
            Equal(ControllerProfiles.Ps2Defaults["Cross"], Config(s, "inis/PCSX2.ini").ReadSection("Pad1")["Cross"]);
            True(!Prepare(s).Changed);
        });
        Test("Procedencia: edición manual revoca gestión incluso al volver al antiguo valor", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); Prepare(s, High);
            Write(s, "inis/PCSX2.ini", Read(s, "inis/PCSX2.ini").Replace("upscale_multiplier = 4", "upscale_multiplier = 7"));
            True(!Prepare(s).Changed);
            Write(s, "inis/PCSX2.ini", Read(s, "inis/PCSX2.ini").Replace("upscale_multiplier = 7", "upscale_multiplier = 4"));
            True(!Prepare(s).Changed); Equal("4", Config(s, "inis/PCSX2.ini").ReadSection("EmuCore/GS")["upscale_multiplier"]);
            // Restoring factory explicitly re-enables automatic quality without deleting a marker.
            Write(s, "inis/PCSX2.ini", Read(s, "inis/PCSX2.ini").Replace("upscale_multiplier = 4", "upscale_multiplier = 1"));
            True(Prepare(s).Changed); Equal("6", Config(s, "inis/PCSX2.ini").ReadSection("EmuCore/GS")["upscale_multiplier"]);
        });
        Test("Procedencia: valores antiguos sin registro no se atribuyen a Nexo", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); Write(s, "portable.ini", "");
            const string legacy = "[EmuCore/GS]\nupscale_multiplier=4\nMaxAnisotropy=16\n[EmuCore]\nEnableWideScreenPatches=true\nEnableNoInterlacingPatches=true\n";
            Write(s, "inis/PCSX2.ini", legacy); var r = Prepare(s); True(!r.Changed && r.Message.Contains("sin registro")); Equal(legacy, Read(s, "inis/PCSX2.ini"));
        });
        Test("Procedencia: perder registro conserva valores; borrar configuración reaplica", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); Prepare(s, High);
            File.Delete(At(s, "inis/PCSX2.ini.nexo-graphics.json")); True(!Prepare(s).Changed);
            Equal("4", Config(s, "inis/PCSX2.ini").ReadSection("EmuCore/GS")["upscale_multiplier"]);
            File.Delete(At(s, "inis/PCSX2.ini")); True(Prepare(s).Changed);
            Equal("6", Config(s, "inis/PCSX2.ini").ReadSection("EmuCore/GS")["upscale_multiplier"]);
        });
        Test("Procedencia: registro corrupto se conserva y fallo de commit no da propiedad falsa", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); Prepare(s, High); string receipt = At(s, "inis/PCSX2.ini.nexo-graphics.json");
            var original = File.ReadAllText(receipt); string before = Read(s, "inis/PCSX2.ini");
            File.WriteAllText(receipt, "{roto"); var r = Prepare(s); True(!r.Changed && r.Message.Contains("continuar")); Equal(before, Read(s, "inis/PCSX2.ini")); Equal("{roto", File.ReadAllText(receipt));
            File.WriteAllText(receipt, original);
            using (File.Open(receipt, FileMode.Open, FileAccess.Read, FileShare.Read))
            { r = Prepare(s); True(r.Changed && r.Message.Contains("continuar")); }
            // Config committed but receipt didn't: don't claim the new value as Nexo's.
            Equal("6", Config(s, "inis/PCSX2.ini").ReadSection("EmuCore/GS")["upscale_multiplier"]);
            True(!Prepare(s, High).Changed); Equal("6", Config(s, "inis/PCSX2.ini").ReadSection("EmuCore/GS")["upscale_multiplier"]);
            Equal(0, Directory.GetFiles(s.EmulatorFolder, "*.tmp", SearchOption.AllDirectories).Length);
        });
        Test("Procedencia: enum de Eden y flags suben juntos; flag editado se respeta", (root, service) =>
        {
            var s = Emu(service, "Switch", "eden.exe"); Prepare(s, Mid);
            Equal("6", Config(s, "user/config/qt-config.ini").ReadSection("Renderer")["resolution_setup"]);
            True(Prepare(s).Changed); Equal("7", Config(s, "user/config/qt-config.ini").ReadSection("Renderer")["resolution_setup"]);
            Write(s, "user/config/qt-config.ini", Read(s, "user/config/qt-config.ini").Replace("resolution_setup\\default = false", "resolution_setup\\default = true"));
            var gpu16 = GpuDetector.Select(new[] { new GpuAdapter("NVIDIA RTX 4080", 16L << 30) }, 3840, 2160);
            True(!Prepare(s, gpu16).Changed); Equal("7", Config(s, "user/config/qt-config.ini").ReadSection("Renderer")["resolution_setup"]);
        });
        Test("Procedencia: reducir presupuesto adapta solo ajustes aún gestionados", (root, service) =>
        {
            var s = Emu(service, "GameCube-Wii", "Dolphin.exe"); Prepare(s);
            True(Prepare(s, Mid).Changed); var d = Config(s, "User/Config/GFX.ini").ReadSection("Settings");
            Equal("3", d["InternalResolution"]); Equal("2", d["MSAA"]);
        });
    }

    private static void OwnedStub(SystemStatus s)
    {
        Write(s, "config.yml", "Video:\n  Resolution Scale: 300\n  Anisotropic Filter Override: 16\n");
        Write(s, "config.yml.nexo-graphics.json", "{\"version\":1,\"values\":{\"Video\\nResolution Scale\":\"300\",\"Video\\nAnisotropic Filter Override\":\"16\"}}");
    }
    private static SystemStatus Emu(NexoService service, string id, string exe)
    { string path = Path.Combine(service.GetStatus(id).EmulatorFolder, "package", exe); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "fixture"); return service.GetStatus(id); }
    private static string At(SystemStatus s, string relative) => Path.Combine(Path.GetDirectoryName(s.ExecutablePath), relative.Replace('/', Path.DirectorySeparatorChar));
    private static void Write(SystemStatus s, string relative, string text) { string path = At(s, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text, new UTF8Encoding(false)); }
    private static string Read(SystemStatus s, string relative) => File.ReadAllText(At(s, relative));
    private static ControllerConfig Config(SystemStatus s, string relative) => new ControllerConfig(At(s, relative));
    private static GraphicsSetupResult Prepare(SystemStatus s, GpuInfo gpu = null) => GraphicsSetup.Prepare(s, gpu ?? Ultra, new string[0]);
    private static void True(bool condition) { if (!condition) throw new Exception("Condición falsa."); }
    private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception("Esperado " + expected + "; recibido " + actual); }
}
