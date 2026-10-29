using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Nexo.Core
{
    public sealed class GraphicsSetupResult
    {
        public bool Changed { get; }
        public string Message { get; }
        public string Tier { get; }
        public GraphicsSetupResult(bool changed, string message, string tier)
        { Changed = changed; Message = message; Tier = tier; }
    }

    internal static class GraphicsSetup
    {
        // Defaults and enum encodings are version-specific; references are in LEEME.md.
        internal static GraphicsSetupResult Prepare(SystemStatus status, GpuInfo gpu, IEnumerable<string> externalFolders = null)
        {
            bool changed = false;
            string tier = gpu.Tier;
            GraphicsSetupResult Result(string text) => new GraphicsSetupResult(changed, text, tier);
            lock (ControllerSetup.Gate)
            {
                try
                {
                    if (!status.Installed) return Result("Primero copia el emulador en su carpeta.");
                    string id = status.System.Id;
                    if (id == "PS4") return Result("shadPS4 ya utiliza Vulkan. La resolución interna la decide cada juego; cambiar internal_screen_width/height no equivale a escalar su renderizado. Se conservan sus ajustes de compatibilidad.");
                    if (id == "PS5") return Result("Nova5 utiliza Vulkan y aplica sus propios ajustes recomendados; se conservan.");
                    string folder = Path.GetDirectoryName(status.ExecutablePath);
                    if (!SafePaths.Within(status.ExecutablePath, status.EmulatorFolder) || SafePaths.HasReparsePoint(folder))
                        return Result("Se conserva el emulador externo; los gráficos automáticos requieren su carpeta Emuladores de Nexo.");
                    if (id == "Switch" && !string.Equals(Path.GetFileName(status.ExecutablePath), "eden.exe", StringComparison.OrdinalIgnoreCase))
                        return Result("Solo está verificado el formato gráfico de Eden 0.2.1; se conserva este emulador de Switch.");

                    string marker = id == "PS2" || id == "GBC" || id == "GBA" ? "portable.ini" : id == "GameCube-Wii" || id == "Xbox360" ? "portable.txt" : id == "Xbox" ? "xemu.toml" : null;
                    string data = id == "Switch" ? "user" : id == "PSP" ? "memstick" : null;
                    bool portable = marker != null && File.Exists(Path.Combine(folder, marker)) || data != null && Directory.Exists(Path.Combine(folder, data));
                    if (id == "PS2" && File.Exists(Path.Combine(folder, "portable.txt")))
                    {
                        string redirect = Path.Combine(folder, "portable.txt");
                        if (SafePaths.HasReparsePoint(redirect) || new FileInfo(redirect).Length > 4096 || !string.IsNullOrWhiteSpace(File.ReadAllText(redirect)))
                            return Result("PCSX2 usa una ruta portable personalizada; se conserva sin cambios.");
                        portable = true;
                    }
                    if (id == "PSP" && File.Exists(Path.Combine(folder, "installed.txt")))
                        return Result("Se conserva la memoria indicada por installed.txt de PPSSPP; trasládala a memstick antes de preparar sus gráficos.");
                    if ((marker != null || data != null) && !portable && (externalFolders ?? ControllerSetup.ExternalFolders(id)).Any(HasData))
                        return Result("Hay datos del emulador en el perfil de Windows. Se conservan; trasládalos a la carpeta portable antes de preparar los gráficos.");
                    if (id == "Retro" && !File.Exists(Path.Combine(folder, "retroarch.cfg")) &&
                        (externalFolders ?? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroArch", "retroarch.cfg") }).Any(HasData))
                        return Result("Se conserva la configuración global de RetroArch; cópiala junto a retroarch.exe para preparar sus gráficos.");

                    string relative;
                    switch (id)
                    {
                        case "GBC": case "GBA": relative = "config.ini"; break;
                        case "Xbox": relative = "xemu.toml"; break;
                        case "Xbox360": relative = "xenia-canary.config.toml"; break;
                        case "PS2": relative = @"inis\PCSX2.ini"; break;
                        case "GameCube-Wii": relative = @"User\Config\GFX.ini"; break;
                        case "PS3": relative = @"config\config.yml"; break;
                        case "PSP": relative = @"memstick\PSP\SYSTEM\ppsspp.ini"; break;
                        case "Switch": relative = @"user\config\qt-config.ini"; break;
                        case "Retro": relative = "retroarch.cfg"; break;
                        default: return Result("No hay preparación gráfica automática para este emulador.");
                    }
                    // Validate portable paths before any configuration write.
                    foreach (var part in new[] { marker, data }.Where(p => p != null))
                        if (SafePaths.HasReparsePoint(Path.Combine(folder, part))) throw new IOException("La ruta portable es un enlace.");
                    var migration = id == "PS3" ? new RpcGraphicsMigration(folder) : null;
                    string configPath = migration?.ConfigPath ?? Path.Combine(folder, relative);
                    var file = new ControllerConfig(configPath, id == "Xbox" || id == "Xbox360");
                    var history = new GraphicsHistory(configPath, file.Exists);
                    if (id == "PS2")
                    {
                        var ui = file.ReadSection("UI");
                        if (ui != null && ui.TryGetValue("SettingsVersion", out var version) && version != "1")
                            return Result("Se conserva una versión de configuración de PCSX2 no reconocida.");
                    }
                    var quality = new GraphicsQuality(gpu);
                    int edits = 0;
                    var preserved = new List<string>();

                    void Set(string section, string key, string factory, string desired, bool yaml = false, bool qt = false, bool quoted = false, string alternateFactory = null)
                    {
                        var values = yaml ? file.ReadYamlScalars(section) : file.ReadSection(section);
                        string current = null;
                        if (values != null) values.TryGetValue(key, out current);
                        if ((id == "GBA" || id == "GBC") && current == null)
                        {
                            string global = file.ReadPreamble(key);
                            if (global != null && !Equivalent(global, factory)) { preserved.Add(key); return; }
                        }
                        string defaultFlag = null;
                        if (qt && values != null) values.TryGetValue(key + @"\default", out defaultFlag);
                        bool owned = history.Owns(section, key, current) && (!qt || history.Owns(section, key + @"\default", defaultFlag));
                        if (!owned)
                        {
                            history.Forget(section, key);
                            if (qt) history.Forget(section, key + @"\default");
                        }
                        if (defaultFlag != null && !Equivalent(defaultFlag, "true") && !Equivalent(defaultFlag, "false"))
                        { preserved.Add(key); return; }
                        // Preserve even stale non-default values paired with Qt's default=true.
                        // They may be deliberate edits; don't silently reinterpret user intent.
                        if (current != null && !Equivalent(current, factory) && (alternateFactory == null || !Equivalent(current, alternateFactory)) && !owned)
                        {
                            preserved.Add(key);
                            return;
                        }
                        if (current != null && Equivalent(current, desired)) return;
                        // Existing files already inherit missing factory defaults. Avoid adding
                        // redundant keys or claiming ownership of a value we haven't changed.
                        if (current == null && file.Exists && Equivalent(factory, desired)) return;
                        var patch = new Dictionary<string, string> { [key] = quoted ? "\"" + desired + "\"" : desired };
                        if (qt) patch[key + @"\default"] = "false";
                        if (yaml) file.SetYamlScalars(section, patch); else file.SetSection(section, patch);
                        history.Record(section, key, desired);
                        if (qt) history.Record(section, key + @"\default", "false");
                        edits++;
                    }

                    switch (id)
                    {
                        case "GBC": case "GBA":
                            Set("ports.qt", "lockIntegerScaling", "0", "1");
                            Set("ports.qt", "resampleVideo", "0", "0");
                            break;
                        case "Xbox":
                            Set("display", "renderer", "OPENGL", gpu.SupportsVulkan ? "VULKAN" : "OPENGL", quoted: true);
                            Set("display.quality", "surface_scale", "1", quality.Xbox.ToString(CultureInfo.InvariantCulture));
                            Set("perf", "cache_shaders", "true", "true");
                            break;
                        case "Xbox360":
                            Set("GPU", "gpu", "any", "d3d12", quoted: true);
                            Set("GPU", "draw_resolution_scale_x", "1", quality.Xbox360.ToString(CultureInfo.InvariantCulture));
                            Set("GPU", "draw_resolution_scale_y", "1", quality.Xbox360.ToString(CultureInfo.InvariantCulture));
                            break;
                        case "PS2":
                            Set("EmuCore/GS", "Renderer", "-1", gpu.SupportsVulkan ? "14" : "-1");
                            Set("EmuCore/GS", "upscale_multiplier", "1", quality.Ps2.ToString(CultureInfo.InvariantCulture));
                            Set("EmuCore/GS", "MaxAnisotropy", "0", "16");
                            Set("EmuCore", "EnableWideScreenPatches", "false", "true");
                            Set("EmuCore", "EnableNoInterlacingPatches", "false", "true");
                            Set("EmuCore/GS", "hw_mipmap", "true", "true");
                            Set("EmuCore/GS", "mipmap", "true", "true");
                            Set("EmuCore/GS", "filter", "2", "2"); // PS2's intended sampling, not forced blur on sprites.
                            Set("EmuCore/GS", "TriFilter", "-1", "-1");
                            Set("EmuCore/GS", "DisableShaderCache", "false", "false");
                            if (!file.Exists)
                            {
                                file.SetSection("UI", new Dictionary<string, string> { ["SettingsVersion"] = "1", ["SetupWizardIncomplete"] = "true" });
                                // PCSX2's bootstrap version is required to retain settings. Include its
                                // factory keyboard so a later controller preparation can recognize it.
                                file.SetSection("Pad1", ControllerProfiles.Ps2Defaults);
                            }
                            break;
                        case "GameCube-Wii":
                            Set("Settings", "InternalResolution", "1", quality.Dolphin.ToString(CultureInfo.InvariantCulture));
                            Set("Enhancements", "MaxAnisotropy", "-1", "4"); // log2: 4 = 16x, -1 = game default
                            var dolphin = file.ReadSection("Settings");
                            if (dolphin != null && dolphin.TryGetValue("SSAA", out var ssaa) && !Equivalent(ssaa, "false"))
                            { preserved.Add("SSAA/MSAA"); history.Forget("Settings", "MSAA"); }
                            else Set("Settings", "MSAA", "1", quality.Msaa.ToString(CultureInfo.InvariantCulture));
                            Set("Settings", "ShaderCompilationMode", "0", tier == "Bajo" ? "0" : "2"); // hybrid ubershaders, NEVER skip rendering (3)
                            Set("Settings", "WaitForShadersBeforeStarting", "false", "true");
                            Set("Settings", "ShaderCache", "true", "true");
                            Set("Settings", "BackendMultithreading", "true", "true");
                            break;
                        case "PS3":
                            Set("Video", "Resolution Scale", "100", quality.RpcScale.ToString(CultureInfo.InvariantCulture), yaml: true);
                            Set("Video", "Anisotropic Filter Override", "0", "16", yaml: true);
                            // Already the Windows defaults, also observed in build 20393's config.
                            Set("Video", "Renderer", "Vulkan", "Vulkan", yaml: true);
                            Set("Video", "Shader Mode", "Async Recompiler with Shader Interpreter", "Async Recompiler with Shader Interpreter", yaml: true);
                            Set("Video", "Disable On-Disk Shader Cache", "false", "false", yaml: true);
                            break;
                        case "PSP":
                            Set("Graphics", "InternalResolution", "1", quality.Psp.ToString(CultureInfo.InvariantCulture), alternateFactory: "0");
                            Set("Graphics", "AnisotropyLevel", "4", "4"); // PPSSPP already defaults to 16x.
                            Set("Graphics", "GraphicsBackend", "3 (VULKAN)", "3 (VULKAN)", alternateFactory: "3");
                            Set("Graphics", "ShaderCache", "true", "true");
                            Set("Graphics", "UberShaderVertex", "true", "true");
                            Set("Graphics", "UberShaderFragment", "true", "true");
                            break;
                        case "Switch":
                            Set("Renderer", "resolution_setup", "3", new[] { "3", "6", "7", "8" }[quality.EdenScale - 1], qt: true);
                            Set("Renderer", "max_anisotropy", "0", "5", qt: true);
                            Set("Renderer", "backend", "1", "1", qt: true);
                            Set("Renderer", "use_disk_shader_cache", "true", "true", qt: true);
                            Set("Renderer", "use_vulkan_driver_pipeline_cache", "true", "true", qt: true);
                            break;
                        case "Retro":
                            Set("", "video_scale_integer", "false", "true", quoted: true);
                            Set("", "video_smooth", "false", "false", quoted: true); // Windows factory default is false.
                            break;
                    }
                    if (edits > 0) changed |= file.Save();
                    // Config commits first. If receipt commit fails, new values remain unowned
                    // on retry, rather than claiming user data after a partial failure.
                    history.Save();
                    if (id == "GameCube-Wii")
                    {
                        // Backend belongs to Dolphin.ini, not GFX.ini. Independent atomic
                        // commit/receipt; no per-game INI is ever created or overwritten.
                        configPath = Path.Combine(folder, @"User\Config\Dolphin.ini");
                        file = new ControllerConfig(configPath);
                        history = new GraphicsHistory(configPath, file.Exists);
                        edits = 0;
                        Set("Core", "GFXBackend", "D3D", gpu.SupportsVulkan ? "Vulkan" : "D3D", alternateFactory: "");
                        if (edits > 0) changed |= file.Save();
                        history.Save();
                    }
                    bool retired = migration != null && migration.Retire();
                    changed |= retired;
                    if (data != null && !Directory.Exists(Path.Combine(folder, data)))
                    { Directory.CreateDirectory(Path.Combine(folder, data)); changed = true; }
                    if (marker != null && !File.Exists(Path.Combine(folder, marker)))
                    {
                        using (new FileStream(Path.Combine(folder, marker), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
                        changed = true;
                    }
                    string message = (changed ? "Gráficos preparados" : "Gráficos comprobados") + " para " + status.System.EmulatorName + ": nivel " + tier + " (" + gpu.Name + ").";
                    if (preserved.Count > 0) message += " Se conservan los ajustes personalizados o sin registro de Nexo: " + string.Join(", ", preserved) + ".";
                    if (retired) message += " Se retiró el archivo gráfico antiguo de Nexo de la raíz de RPCS3; se usa config\\config.yml.";
                    if (gpu.MonitorWidth > 0 && gpu.MonitorHeight > 0)
                        message += " Pantalla principal: " + gpu.MonitorWidth + "×" + gpu.MonitorHeight + (gpu.MonitorRefreshRate > 0 ? " a " + gpu.MonitorRefreshRate + " Hz" : "") + "; se conserva la proporción del juego.";
                    if (!gpu.SupportsVulkan && (id == "PS2" || id == "GameCube-Wii" || id == "Xbox"))
                        message += " No se confirmó Vulkan 1.3 en esta GPU; se conserva el motor compatible o automático.";
                    return Result(message);
                }
                catch (Exception ex)
                { return Result("No se pudieron completar los ajustes gráficos; el lanzamiento puede continuar. " + ex.Message); }
            }
        }

        private static bool HasData(string path) => File.Exists(path) || Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
        internal static bool Equivalent(string actual, string expected)
        {
            if (actual == null) return false;
            actual = actual.Trim();
            if (actual.Length >= 2 && actual[0] == '"' && actual[actual.Length - 1] == '"') actual = actual.Substring(1, actual.Length - 2);
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
            // Only number-to-number equivalence; 0/1 are not substituted for INI boolean values.
            return decimal.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
                decimal.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && a == b;
        }
    }
}
