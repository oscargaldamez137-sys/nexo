using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Nexo.Tests")]

namespace Nexo.Core
{
    public enum MediaKind { Cartridge, DvdCase, BluRayCase, Disc, Umd, MiniDisc, GameCard }
    public enum ArchiveState { Ready, Incomplete, Unreadable }

    public sealed class ArchiveEntry
    {
        public string SystemId { get; }
        public string Path { get; }
        public string Title { get; }
        public long Size { get; }
        public long? ExpectedSize { get; }
        public ArchiveState State { get; }
        public string Message { get; }
        public string TitleId { get; }
        public GameCompatibility Compatibility { get; }
        public ArchiveEntry(string systemId, string path, string title, long size, long? expectedSize,
            ArchiveState state, string message, GameCompatibility compatibility = null, string titleId = null)
        { SystemId = systemId; Path = path; Title = title; Size = size; ExpectedSize = expectedSize; State = state; Message = message;
          Compatibility = compatibility ?? GameCompatibility.Unknown; TitleId = titleId; }
    }

    public sealed class GameSystem
    {
        public string Id { get; }
        public string Name { get; }
        public string EmulatorName { get; }
        public string Maker { get; }
        public int? Year { get; }
        public string Description { get; }
        public string GamesHint { get; internal set; } = "";
        public string Website { get; }
        public string Primary { get; }
        public string Secondary { get; }
        public string Accent { get; }
        public MediaKind Media { get; }
        public bool Experimental { get; }

        public GameSystem(string id, string name, string emulatorName, string maker, int? year,
            string description, string website, string primary, string secondary, string accent,
            MediaKind media, bool experimental = false)
        {
            Id = id; Name = name; EmulatorName = emulatorName; Maker = maker; Year = year;
            Description = description; Website = website; Primary = primary; Secondary = secondary;
            Accent = accent; Media = media; Experimental = experimental;
        }
    }

    public sealed class GameCompatibility
    {
        public string Status { get; }
        public string Date { get; }
        public string RecommendedUpdate { get; }
        public string Explanation { get; }
        public string UpdateHint { get; }
        public string EmulatorVersion { get; }
        public string OperatingSystem { get; }
        public string ReportUrl { get; }
        public GameCompatibility(string status, string date, string recommendedUpdate, string explanation, string updateHint,
            string emulatorVersion = null, string operatingSystem = null, string reportUrl = null)
        { Status = status; Date = date; RecommendedUpdate = recommendedUpdate; Explanation = explanation; UpdateHint = updateHint;
          EmulatorVersion = emulatorVersion; OperatingSystem = operatingSystem; ReportUrl = reportUrl; }
        internal static GameCompatibility Unknown { get; } = new GameCompatibility("Desconocido", null, null,
            "No hay datos locales de compatibilidad para este juego. Esto no significa que no funcione.", null);
    }

    public sealed class GameEntry
    {
        public string SystemId { get; }
        public string Title { get; }
        public string Path { get; }
        public string CoverPath { get; }
        public string BackgroundPath { get; }
        public string Platform { get; }
        public GameCompatibility Compatibility { get; }
        public GameEntry(string systemId, string title, string path, string coverPath = null)
            : this(systemId, title, path, coverPath, null) { }
        public GameEntry(string systemId, string title, string path, string coverPath, GameCompatibility compatibility)
            : this(systemId, title, path, coverPath, compatibility, null, null) { }
        public GameEntry(string systemId, string title, string path, string coverPath, GameCompatibility compatibility,
            string backgroundPath, string platform)
        { SystemId = systemId; Title = title; Path = path; CoverPath = coverPath; Compatibility = compatibility ?? GameCompatibility.Unknown;
            BackgroundPath = backgroundPath; Platform = platform; }
    }

    public sealed class SystemStatus
    {
        public GameSystem System { get; }
        public string ExecutablePath { get; }
        public bool Installed { get; }
        public string EmulatorFolder { get; }
        public string GamesFolder { get; }
        public IReadOnlyList<string> SetupMessages { get; }
        public SystemStatus(GameSystem system, string executablePath, bool installed, string emulatorFolder, string gamesFolder)
        { System = system; ExecutablePath = executablePath; Installed = installed; EmulatorFolder = emulatorFolder; GamesFolder = gamesFolder;
          SetupMessages = SystemFiles.Inspect(this); }
    }

    public sealed class GameScanResult
    {
        public IReadOnlyList<GameEntry> Games { get; }
        public IReadOnlyList<ArchiveEntry> Archives { get; }
        /// <summary>Recognized Retro ROMs excluded because no compatible installed core was found.</summary>
        public int IgnoredCount { get; }
        public string Reason { get; }
        internal GameScanResult(IReadOnlyList<GameEntry> games, int ignoredCount, string reason, IReadOnlyList<ArchiveEntry> archives = null)
        { Games = games; IgnoredCount = ignoredCount; Reason = reason; Archives = archives ?? Array.Empty<ArchiveEntry>(); }
    }

    /// <summary>Resultado de cambiar una carpeta raíz. Nexo nunca mueve ni borra lo que había en la anterior.</summary>
    public sealed class FolderChangeResult
    {
        public bool Changed { get; }
        public string PreviousFolder { get; }
        public string Folder { get; }
        /// <summary>La carpeta anterior contiene archivos del usuario (además de los LEEME de Nexo).</summary>
        public bool PreviousHasContent { get; }
        public string Message { get; }
        public FolderChangeResult(bool changed, string previousFolder, string folder, bool previousHasContent, string message)
        { Changed = changed; PreviousFolder = previousFolder; Folder = folder; PreviousHasContent = previousHasContent; Message = message; }
    }

    public sealed class NexoException : Exception
    {
        public NexoException(string message) : base(message) { }
        public NexoException(string message, Exception inner) : base(message, inner) { }
    }

    internal sealed class SystemProfile
    {
        internal GameSystem Info { get; }
        internal string[] Executables { get; }
        internal string[] Extensions { get; }
        internal string Instructions { get; }
        internal SystemProfile(GameSystem info, string[] executables, string[] extensions, string instructions)
        { Info = info; Info.GamesHint = instructions; Executables = executables; Extensions = extensions; Instructions = instructions; }
    }
}
