// SubScoutRunner.cs
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace SubScout
{
    public sealed record ScanReport(int ItemsVisited, int SubCandidates, int Matches, int CopiesPlanned);
    public sealed record SubScoutResult(int ItemsVisited, int SubCandidates, int Matches, int Writes);
    public interface ISubScoutRunner
    {
        /// <summary>
        /// Execute a one-shot scan using the supplied configuration.
        /// When dryRun = true, no file operations are performed.
        /// </summary>
        Task<ScanReport> RunOnceAsync(SubScoutConfiguration cfg, bool dryRun, CancellationToken ct);
        Task<SubScoutResult> RunAsync(bool dryRun, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Self-contained runner that enumerates media, finds local subtitles by template/heuristics,
    /// and optionally copies/moves next to media. Keeps logic isolated so controller & scheduled hook can reuse.
    /// </summary>
    public sealed class SubScoutRunner : ISubScoutRunner
    {
        private readonly ILibraryManager _library;
        private readonly ILogger<SubScoutRunner> _logger;

        public SubScoutRunner(ILibraryManager library, ILogger<SubScoutRunner> logger)
        {
            _library = library;
            _logger = logger;
        }

        public async Task<ScanReport> RunOnceAsync(SubScoutConfiguration cfg, bool dryRun, CancellationToken ct)
        {
            // normalize config
            var templates = (cfg.Templates?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>());
            if (templates.Count == 0)
            {
                templates = new List<string>
                {
                    "%fn%.%l%.%fe%",
                    "%fn%_%l%.%fe%",
                    "%fn%.%fe%",

                    "Subs/%fn%.%l%.%fe%",
                    "Subs/%fn%_%l%.%fe%",
                    "Subs/%fn%.%fe%",

                    "Subs/%fn%/%n%_%l%.%fe%",
                    "Subs/%fn%/%any%.%fe%",
                    "Subs/%any%/%any%.%fe%"
                };
            }

            var extensions = cfg.Extensions?.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToList()
                            ?? new List<string> { ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx" };

            var langGroups = BuildLanguageGroups(cfg.LanguageSynonyms);

            _logger.LogInformation("SubScout[Runner]: dryRun={DryRun} | templates={T} | ext={E} | allowDeep={D} | maxDepth={M} | ICU={Icu} | OnlyPath='{P}' | OnlyName='{N}' | copy={C} | move={Mv} | overwrite={O} | dest='{Dest}'",
                dryRun, templates.Count, extensions.Count, cfg.AllowDeepMatch, cfg.MaxDepth, cfg.UseCultureLanguageMap,
                cfg.OnlyPathContains, cfg.OnlyNameContains, cfg.CopyToMediaFolder, cfg.MoveInsteadOfCopy, cfg.OverwriteExisting, cfg.DestinationPattern ?? "%fn%.%l%.%fe%");

            var root = _library.RootFolder;
            // get all Video (episodes + movies), then apply optional filters
            var videos = root.GetRecursiveChildren()
                             .OfType<Video>()
                             .Where(v =>
                                 (string.IsNullOrWhiteSpace(cfg.OnlyPathContains) ||
                                     (v.Path?.IndexOf(cfg.OnlyPathContains, StringComparison.OrdinalIgnoreCase) >= 0)) &&
                                 (string.IsNullOrWhiteSpace(cfg.OnlyNameContains) ||
                                     (v.Name?.IndexOf(cfg.OnlyNameContains, StringComparison.OrdinalIgnoreCase) >= 0)))
                             .ToList();

            int visited = 0, candidates = 0, matches = 0, copiesPlanned = 0;

            foreach (var video in videos)
            {
                ct.ThrowIfCancellationRequested();
                visited++;

                if (string.IsNullOrEmpty(video.Path) || !File.Exists(video.Path))
                    continue;

                var vPath = video.Path!;
                var vDir = Path.GetDirectoryName(vPath)!;
                var vFile = Path.GetFileName(vPath);
                var vBase = Path.GetFileNameWithoutExtension(vPath);

                // Enumerate candidate subtitle files near this item
                // - Always include media folder
                // - Include "Subs" under media folder
                // - Include deeper paths if AllowDeepMatch (bounded by MaxDepth; 0 = unbounded)
                var searchRoots = new List<string> { vDir };
                var subsDir = Path.Combine(vDir, "Subs");
                if (Directory.Exists(subsDir)) searchRoots.Add(subsDir);

                var subFiles = new List<string>();

                foreach (var rootDir in searchRoots.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(rootDir)) continue;
                    if (cfg.AllowDeepMatch)
                    {
                        foreach (var f in EnumerateFilesSafe(rootDir, extensions, cfg.MaxDepth))
                            subFiles.Add(f);
                    }
                    else
                    {
                        foreach (var f in SafeEnumerateFiles(rootDir))
                        {
                            if (extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                                subFiles.Add(f);
                        }
                    }
                }

                subFiles = subFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                candidates += subFiles.Count;

                // Try to match by templates and heuristics
                foreach (var subPath in subFiles)
                {
                    var subName = Path.GetFileName(subPath);
                    var subExt = Path.GetExtension(subPath);                   // e.g. ".srt"
                    var fe = subExt.StartsWith(".") ? subExt.Substring(1) : subExt;

                var detectedLang = DetectLanguage3(subName, langGroups, cfg.UseCultureLanguageMap);
                var langGroup = GetMatchingLanguageGroup(subName, langGroups);
                var langPattern = BuildLangAlternation(langGroup, detectedLang);

                var looksRelated = LooksRelated(subName, vBase);

                var anyTemplateHit = templates.Any(t => TemplateCouldHit(
                    template: t,
                    vBase: vBase,
                    langPattern: langPattern,   // <— alternation, not single code
                    feNoDot: fe,
                    subName: subName,
                    subPath: subPath,
                    mediaDir: vDir,
                    logger: _logger));

                    if (!looksRelated && !anyTemplateHit)
                        continue;

                    matches++;

                    // compute destination path
                    var destFile = (cfg.DestinationPattern ?? "%fn%.%l%.%fe%")
                        .Replace("%fn%", vBase, StringComparison.Ordinal)
                        .Replace("%l%", detectedLang, StringComparison.Ordinal)
                        .Replace("%fe%", fe, StringComparison.Ordinal);

                    var destPath = Path.Combine(vDir, destFile);

                    _logger.LogInformation("SubScout[Runner]: match: media='{Media}' <- sub='{Sub}' lang={Lang} => dest='{Dest}'",
                        vFile, subName, detectedLang, destPath);

                    // apply copy/move only if live mode
                    if (!dryRun && cfg.CopyToMediaFolder)
                    {
                        // Skip if destination exists and not overwriting
                        if (File.Exists(destPath) && !cfg.OverwriteExisting)
                        {
                            _logger.LogInformation("SubScout[Runner]: skip write; destination exists: {Dest}", destPath);
                        }
                        else
                        {
                            try
                            {
                                // ensure directory exists
                                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                                if (cfg.MoveInsteadOfCopy)
                                {
                                    // If dest exists and overwrite is set, delete first.
                                    if (File.Exists(destPath) && cfg.OverwriteExisting)
                                        File.Delete(destPath);

                                    // Move with replace if possible; otherwise emulate
#if NET8_0_OR_GREATER
                                    File.Move(subPath, destPath, overwrite: cfg.OverwriteExisting);
#else
                                    if (File.Exists(destPath) && cfg.OverwriteExisting)
                                        File.Delete(destPath);
                                    File.Move(subPath, destPath);
#endif
                                    _logger.LogInformation("SubScout[Runner]: moved -> {Dest}", destPath);
                                }
                                else
                                {
#if NET8_0_OR_GREATER
                                    File.Copy(subPath, destPath, overwrite: cfg.OverwriteExisting);
#else
                                    if (File.Exists(destPath))
                                    {
                                        if (cfg.OverwriteExisting)
                                        {
                                            File.Delete(destPath);
                                            File.Copy(subPath, destPath);
                                        }
                                        else
                                        {
                                            // skip
                                        }
                                    }
                                    else
                                    {
                                        File.Copy(subPath, destPath);
                                    }
#endif
                                    _logger.LogInformation("SubScout[Runner]: copied -> {Dest}", destPath);
                                }

                                copiesPlanned++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "SubScout[Runner]: file operation failed from '{Src}' to '{Dest}'", subPath, destPath);
                            }
                        }
                    }
                }
            }

            _logger.LogInformation("SubScout[Runner]: complete. visited={Visited}, candidates={Cand}, matches={Matches}, writes={Writes}",
                visited, candidates, matches, copiesPlanned);

            // no real async work above; match signature
            await Task.CompletedTask;
            return new ScanReport(visited, candidates, matches, copiesPlanned);
        }

        public async Task<SubScoutResult> RunAsync(bool dryRun, CancellationToken cancellationToken)
        {
            // Use current plugin configuration if available, else a default
            var cfg = SubScoutPlugin.Instance?.Configuration ?? new SubScoutConfiguration();

            var report = await RunOnceAsync(cfg, dryRun, cancellationToken);
            return new SubScoutResult(
                ItemsVisited: report.ItemsVisited,
                SubCandidates: report.SubCandidates,
                Matches: report.Matches,
                Writes: report.CopiesPlanned
            );
        }



        private static IEnumerable<string> SafeEnumerateFiles(string dir)
        {
            try
            {
                return Directory.EnumerateFiles(dir);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> EnumerateFilesSafe(string root, List<string> exts, int maxDepth)
        {
            var stack = new Stack<(string path, int depth)>();
            stack.Push((root, 0));

            while (stack.Count > 0)
            {
                var (p, d) = stack.Pop();
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(p); }
                catch { files = Array.Empty<string>(); }

                foreach (var f in files)
                {
                    if (exts.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                        yield return f;
                }

                if (maxDepth > 0 && d >= maxDepth) continue;

                IEnumerable<string> dirs;
                try { dirs = Directory.EnumerateDirectories(p); }
                catch { dirs = Array.Empty<string>(); }

                foreach (var sub in dirs)
                    stack.Push((sub, d + 1));
            }
        }

        private static bool LooksRelated(string subtitleFileName, string videoBaseName)
        {
            // Quick & permissive check: video base tokens must intersect subtitle tokens
            var vt = SplitTokens(videoBaseName);
            var st = SplitTokens(subtitleFileName);
            return vt.Intersect(st, StringComparer.OrdinalIgnoreCase).Any();
        }

        private static string[] SplitTokens(string s)
            => Regex.Split(s ?? string.Empty, @"[^A-Za-z0-9]+").Where(t => t.Length > 0).ToArray();

        private static bool TemplateCouldHit(
            string template,
            string vBase,
            string langPattern,     // now a regex alternation like (?:en|eng|english)
            string feNoDot,
            string subName,
            string subPath,
            string mediaDir,
            ILogger<SubScoutRunner> logger)
        {
            try
            {
                // Compute relative path from media directory (so templates with "Subs/..." work)
                string relativePath;
                if (subPath.StartsWith(mediaDir, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = subPath.Substring(mediaDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                else
                {
                    relativePath = subName; // fallback to filename only
                }

                // Start from an escaped version and expand placeholders to regex fragments
                string pattern = "^" + Regex.Escape(template)
                    .Replace("%fn%", Regex.Escape(vBase))
                    .Replace("%l%", langPattern)                 // allow any synonym that matched in filename
                    .Replace("%fe%", Regex.Escape(feNoDot))
                    .Replace("%n%", @"[0-9]+")
                    .Replace("%any%", @"[^/\\]+")                // exclude both separators
                    .Replace("/", @"[/\\]") + "$";

                // Fix accidental escaping around the path separator class if present
                pattern = pattern.Replace(@"\[/\\]", @"[/\\]");

                var regex = new Regex(pattern, RegexOptions.IgnoreCase);

                // Normalize path slashes for the test
                var relNorm = relativePath.Replace('\\', '/');

                bool matchesRelative = regex.IsMatch(relNorm);
                bool matchesFileName = regex.IsMatch(subName);

                logger.LogInformation(
                    "Template test: '{Template}' -> '{Pattern}' vs relative='{Relative}' file='{File}' -> {Result}",
                    template, pattern, relativePath, subName, matchesRelative || matchesFileName
                );

                return matchesRelative || matchesFileName;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Template matching failed for template: {Template}", template);
                return false;
            }
        }


        private static List<HashSet<string>> BuildLanguageGroups(IList<string>? groups)
        {
            // groups like: "en|eng|english"
            var result = new List<HashSet<string>>();
            if (groups != null)
            {
                foreach (var line in groups)
                {
                    var g = (line ?? string.Empty)
                        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(x => x.Length > 0)
                        .Select(x => x.ToLowerInvariant())
                        .ToHashSet();
                    if (g.Count > 0) result.Add(g);
                }
            }
            // ensure at least english fallback
            if (result.All(g => !g.Contains("en")))
                result.Add(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en", "eng", "english" });

            return result;
        }

        private static string DetectLanguage3(string fileName, List<HashSet<string>> groups, bool useCultureMap)
        {
            var tokens = SplitTokens(Path.GetFileNameWithoutExtension(fileName))
                         .Select(t => t.ToLowerInvariant()).ToArray();

            foreach (var t in tokens)
            {
                foreach (var g in groups)
                {
                    if (g.Contains(t))
                    {
                        // choose any 3-letter token present in group; else use the first 2/word as ISO-ish code
                        var three = g.FirstOrDefault(x => x.Length == 3) ?? g.First();
                        return three;
                    }
                }
            }

            if (useCultureMap)
            {
                // very light ICU-ish mapping for common patterns
                if (tokens.Contains("english") || tokens.Contains("eng") || tokens.Contains("en")) return "eng";
                if (tokens.Contains("danish") || tokens.Contains("dan") || tokens.Contains("da")) return "dan";
                if (tokens.Contains("french") || tokens.Contains("fra") || tokens.Contains("fre") || tokens.Contains("fr")) return "fra";
                if (tokens.Contains("german") || tokens.Contains("ger") || tokens.Contains("deu") || tokens.Contains("de")) return "deu";
                if (tokens.Contains("spanish") || tokens.Contains("spa") || tokens.Contains("es")) return "spa";
            }

            return "und"; // undetermined
        }

        private static HashSet<string>? GetMatchingLanguageGroup(string fileName, List<HashSet<string>> groups)
        {
            var tokens = SplitTokens(Path.GetFileNameWithoutExtension(fileName))
                         .Select(t => t.ToLowerInvariant()).ToArray();

            foreach (var g in groups)
            {
                // if any token from filename is contained in this group, we consider it a match
                if (tokens.Any(t => g.Contains(t)))
                    return g;
            }

            return null;
        }

        private static string BuildLangAlternation(HashSet<string>? group, string canonical)
        {
            // If we found a group, match any of its entries; otherwise just match the canonical code
            if (group != null && group.Count > 0)
            {
                // Escape each synonym, keep original casing-insensitive match at regex level
                var alts = group.Select(s => Regex.Escape(s)).ToArray();
                return $"(?:{string.Join("|", alts)})";
            }

            return Regex.Escape(canonical);
        }

    }
}
