using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;   // <— for GetRequiredService

namespace SubScout;

[ApiController]
[Route("SubScout")]
[Authorize]
public sealed class SubScoutController : ControllerBase
{
    private readonly ILogger<SubScoutController> _logger;
    private readonly IServiceProvider _sp;

    public SubScoutController(ILogger<SubScoutController> logger, IServiceProvider sp)
    {
        _logger = logger;
        _sp = sp;
    }

    private ISubScoutRunner GetRunner() => _sp.GetRequiredService<ISubScoutRunner>();

    private static Stream? GetResource(string manifest)
        => Assembly.GetExecutingAssembly().GetManifestResourceStream(manifest);

    // ----- Static assets (HTML/JS) -----

    [HttpGet("ConfigurationPage")]
    public IActionResult ConfigurationPage()
    {
        const string manifest = "SubScout.Web.SubScoutPage.html";
        var stream = GetResource(manifest);
        if (stream is null) return NotFound($"Manifest not found: {manifest}");
        return File(stream, "text/html; charset=utf-8");
    }

    [HttpGet("SubScoutScript.js")]
    public IActionResult Script()
    {
        const string manifest = "SubScout.Web.SubScoutScript.js";
        var stream = GetResource(manifest);
        if (stream is null) return NotFound($"Manifest not found: {manifest}");
        return File(stream, "application/javascript; charset=utf-8");
    }

    // Optional compat route the web layer can call
    [HttpGet("web/ConfigurationPage")]
    public ActionResult GetPage([FromQuery] string name)
    {
        var asm = typeof(SubScoutPlugin).Assembly;

        if (string.Equals(name, "subscout", StringComparison.OrdinalIgnoreCase))
        {
            using var s = asm.GetManifestResourceStream("SubScout.Web.SubScoutPage.html");
            if (s == null) return NotFound();
            return File(s, "text/html; charset=utf-8");
        }

        if (string.Equals(name, "SubScoutScript.js", StringComparison.Ordinal))
        {
            using var s = asm.GetManifestResourceStream("SubScout.Web.SubScoutScript.js");
            if (s == null) return NotFound();
            return File(s, "application/javascript; charset=utf-8");
        }

        return NotFound();
    }

    // ----- Configuration API -----

    [HttpGet("Configuration")]
    public ActionResult<SubScoutConfiguration> GetConfiguration()
    {
        var inst = SubScoutPlugin.Instance;
        if (inst is null)
            return StatusCode(500, "SubScout plugin instance not available.");

        var c = inst.Configuration ?? new SubScoutConfiguration();
        Normalize(c);

        // Return a copy (arrays, not lists) to avoid ref mutation in the web ui
        return Ok(new SubScoutConfiguration
        {
            Templates             = c.Templates             ?? Array.Empty<string>(),
            Extensions            = c.Extensions            ?? Array.Empty<string>(),
            LanguageSynonyms      = c.LanguageSynonyms      ?? Array.Empty<string>(),
            AllowDeepMatch        = c.AllowDeepMatch,
            MaxDepth              = c.MaxDepth,
            UseCultureLanguageMap = c.UseCultureLanguageMap,
            OnlyPathContains      = c.OnlyPathContains      ?? string.Empty,
            OnlyNameContains      = c.OnlyNameContains      ?? string.Empty,
            CopyToMediaFolder     = c.CopyToMediaFolder,
            MoveInsteadOfCopy     = c.MoveInsteadOfCopy,
            OverwriteExisting     = c.OverwriteExisting,
            DestinationPattern    = string.IsNullOrWhiteSpace(c.DestinationPattern) ? "%fn%.%l%.%fe%" : c.DestinationPattern
        });
    }

    [HttpPost("Configuration")]
    public IActionResult UpdateConfiguration([FromBody] SubScoutConfiguration incoming)
    {
        Normalize(incoming);

        var inst = SubScoutPlugin.Instance;
        if (inst is null)
            return StatusCode(500, "SubScout plugin instance not available.");

        var cfg = inst.Configuration ?? new SubScoutConfiguration();

        cfg.Templates             = incoming.Templates            ?? Array.Empty<string>();
        cfg.Extensions            = incoming.Extensions           ?? Array.Empty<string>();
        cfg.LanguageSynonyms      = incoming.LanguageSynonyms     ?? Array.Empty<string>();
        cfg.AllowDeepMatch        = incoming.AllowDeepMatch;
        cfg.MaxDepth              = incoming.MaxDepth;
        cfg.UseCultureLanguageMap = incoming.UseCultureLanguageMap;

        cfg.OnlyPathContains      = incoming.OnlyPathContains     ?? string.Empty;
        cfg.OnlyNameContains      = incoming.OnlyNameContains     ?? string.Empty;

        cfg.CopyToMediaFolder     = incoming.CopyToMediaFolder;
        cfg.MoveInsteadOfCopy     = incoming.MoveInsteadOfCopy;
        cfg.OverwriteExisting     = incoming.OverwriteExisting;
        cfg.DestinationPattern    = string.IsNullOrWhiteSpace(incoming.DestinationPattern)
            ? "%fn%.%l%.%fe%"
            : incoming.DestinationPattern.Trim();

        inst.UpdateConfiguration(cfg);
        return Ok(new { Saved = true });
    }

    // ----- One-shot execution -----


    [HttpPost("Test")]
    public async Task<ActionResult> Test([FromBody] SubScoutConfiguration? cfg, CancellationToken ct)
    {
        if (cfg is null) return BadRequest(new { ok = false, message = "No configuration payload." });
        Normalize(cfg);
        var problems = Validate(cfg);
        if (problems.Length > 0) return BadRequest(new { ok = false, message = "SubScout test failed validation.", problems });

        var report = await GetRunner().RunOnceAsync(cfg, dryRun: false, ct).ConfigureAwait(false);
        return Ok(new { ok = true, message = "SubScout test execute. See server logs for matches.", report });
    }

    [HttpPost("Run")]
    public async Task<ActionResult> Run([FromBody] SubScoutConfiguration? cfg, CancellationToken ct)
    {
        if (cfg is null) return BadRequest(new { ok = false, message = "No configuration payload." });
        Normalize(cfg);
        var problems = Validate(cfg);
        if (problems.Length > 0) return BadRequest(new { ok = false, message = "SubScout run failed validation.", problems });

        _logger.LogInformation("SubScout[Run]: starting LIVE scan.");
        var report = await GetRunner().RunOnceAsync(cfg, dryRun: false, ct).ConfigureAwait(false);
        return Ok(new { ok = true, message = "SubScout live run completed. See server logs for details.", report });
    }

    // ----- helpers -----

private static void Normalize(SubScoutConfiguration cfg)
{
    // Trim arrays, de-dupe, drop empties
    static string[] Clean(string[]? arr) =>
        (arr ?? Array.Empty<string>())
            .Select(s => (s ?? string.Empty).Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    cfg.Templates        = Clean(cfg.Templates);
    cfg.Extensions       = Clean(cfg.Extensions);
    cfg.LanguageSynonyms = Clean(cfg.LanguageSynonyms);

    // Auto-demux common user mistakes
    var misfiledLang = cfg.Extensions.Where(x => x.Contains('|', StringComparison.Ordinal)).ToArray();
    if (misfiledLang.Length > 0)
    {
        cfg.LanguageSynonyms = cfg.LanguageSynonyms.Concat(misfiledLang).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        cfg.Extensions = cfg.Extensions.Where(x => !x.Contains('|', StringComparison.Ordinal)).ToArray();
    }
    var misfiledExt = cfg.LanguageSynonyms.Where(x => x.StartsWith(".", StringComparison.Ordinal)).ToArray();
    if (misfiledExt.Length > 0)
    {
        cfg.Extensions = cfg.Extensions.Concat(misfiledExt).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        cfg.LanguageSynonyms = cfg.LanguageSynonyms.Where(x => !x.StartsWith(".", StringComparison.Ordinal)).ToArray();
    }

    // ---- Seed defaults if empty ----
    if (cfg.Templates.Length == 0)
    {
        cfg.Templates = new[]
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

    if (cfg.Extensions.Length == 0)
    {
        cfg.Extensions = new[] { ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx" };
    }

    if (cfg.LanguageSynonyms.Length == 0)
    {
        cfg.LanguageSynonyms = new[]
        {
            "en|eng|english",
            "fr|fra|fre|french",
            "de|ger|deu|german",
            "es|spa|spanish",
            "it|ita|italian",
            "pt|por|portuguese",
            "sv|swe|swedish",
            "da|dan|dansk|danish",
            "nl|dut|nld|dutch",
            "pl|pol|polish",
            "ru|rus|russian",
            "zh|chi|zho|chinese|chs|cht",
            "ja|jpn|japanese",
            "ko|kor|korean"
        };
    }

    // Destination pattern fallback
    if (string.IsNullOrWhiteSpace(cfg.DestinationPattern))
        cfg.DestinationPattern = "%fn%.%l%.%fe%";
}


    private static string[] Validate(SubScoutConfiguration? cfg)
    {
        if (cfg is null) return new[] { "No configuration payload received." };

        var issues = new List<string>();

        if (cfg.Templates == null || cfg.Templates.Length == 0)
            issues.Add("Templates are empty.");
        if (cfg.Extensions == null || cfg.Extensions.Length == 0)
            issues.Add("Extensions are empty.");

        var badTemplates = (cfg.Templates ?? Array.Empty<string>())
            .Where(t => string.IsNullOrWhiteSpace(t)
                        || t.IndexOfAny(Path.GetInvalidPathChars()) >= 0
                        || t.Contains("..\\", StringComparison.Ordinal)
                        || t.Contains("../", StringComparison.Ordinal))
            .ToArray();
        if (badTemplates.Length > 0)
            issues.Add($"Invalid templates: {string.Join(", ", badTemplates.Take(3))}{(badTemplates.Length > 3 ? "…" : "")}");

        var badExt = (cfg.Extensions ?? Array.Empty<string>())
            .Where(e => !e.StartsWith(".", StringComparison.Ordinal))
            .ToArray();
        if (badExt.Length > 0)
            issues.Add($"Extensions must start with a dot: {string.Join(", ", badExt.Take(3))}{(badExt.Length > 3 ? "…" : "")}");

        return issues.ToArray();
    }
}
