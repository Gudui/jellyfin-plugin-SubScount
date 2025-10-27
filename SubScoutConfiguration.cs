using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using MediaBrowser.Model.Plugins;

namespace SubScout;

/// <summary>Plugin configuration.</summary>
[Serializable]
public sealed class SubScoutConfiguration : BasePluginConfiguration
{
    // Keep your list-backed Templates implementation
    private readonly List<string> _templates = new();

    // SubScoutConfiguration.cs  (add inside the class)
    public SubScoutConfiguration()
    {
        // Only seed if not already set (e.g., when Jellyfin deserializes an existing file)
        if (_templates.Count == 0)
        {
            _templates.AddRange(new[]
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
            });
        }

        // Arrays already have sensible defaults in your file; enforce the exact set requested:
        Extensions = new[] { ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx" };

        LanguageSynonyms = new[]
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

        AllowDeepMatch        = true;
        MaxDepth              = 0;
        UseCultureLanguageMap = true;
        OnlyPathContains      = string.Empty;
        OnlyNameContains      = string.Empty;
        CopyToMediaFolder     = true;
        MoveInsteadOfCopy     = false;
        OverwriteExisting     = false;
        DestinationPattern    = "%fn%.%l%.%fe%";
    }


    /// <summary>Gets or sets template strings.</summary>
    [SuppressMessage("StyleCop.CSharp.SpacingRules", "CA1819", Justification = "Backed by a list object.")]
    public string[] Templates
    {
        get => _templates.ToArray();
        set
        {
            _templates.Clear();
            if (value == null) return;
            foreach (var t in value)
            {
                if (!string.IsNullOrWhiteSpace(t))
                    _templates.Add(t.Trim());
            }
        }
    }

    /// <summary>Add a template if not present.</summary>
    public void AddTemplate(string template)
    {
        if (!string.IsNullOrWhiteSpace(template) && !_templates.Contains(template))
            _templates.Add(template);
    }

    public void RemoveTemplate(string template) => _templates.Remove(template);
    public void ClearTemplates() => _templates.Clear();

    /// <summary>Reset templates to safe defaults.</summary>
    public void ResetTemplates()
    {
        _templates.Clear();
        // Same placeholders your provider recognizes: %f%, %fn%, %fe%, %l%, %n%, %any%
        _templates.Add(Path.Join("Subs", "%fn%", "%n%_%l%.srt"));
        _templates.Add(Path.Join("Subs", "%fn%.%l%.srt"));
        _templates.Add(Path.Join("Subs", "%n%_%l%.srt"));
        _templates.Add(Path.Join("Subs", "%l%.srt"));
        _templates.Add(Path.Join("Subs", "%fn%.srt"));
    }

    // ===== NEW FIELDS (used by controller/UI) =====

    /// <summary>Allowed subtitle file extensions (with dot), one per line in UI.</summary>
    public string[] Extensions { get; set; } =
    {
        ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx"
    };

    /// <summary>Language synonym groups (pipe-separated tokens per line in UI).</summary>
    public string[] LanguageSynonyms { get; set; } =
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

    /// <summary>Allow descending into subfolders specified via templates.</summary>
    public bool AllowDeepMatch { get; set; } = true;

    /// <summary>Max recursion depth (0 = unbounded).</summary>
    public int MaxDepth { get; set; } = 0;

    /// <summary>Use CultureInfo/ICU to expand language names (en → English).</summary>
    public bool UseCultureLanguageMap { get; set; } = true;

    /// <summary>Optional: only process items whose full path contains this substring.</summary>
    public string? OnlyPathContains { get; set; } = string.Empty;

    /// <summary>Optional: only process items whose display name contains this substring.</summary>
    public string? OnlyNameContains { get; set; } = string.Empty;

    /// <summary>Copy matched subtitles next to the media (if false, discovery-only).</summary>
    public bool CopyToMediaFolder { get; set; } = true;

    /// <summary>Move instead of copy when placing subtitles next to media.</summary>
    public bool MoveInsteadOfCopy { get; set; } = false;

    /// <summary>Overwrite an existing destination subtitle file.</summary>
    public bool OverwriteExisting { get; set; } = false;

    /// <summary>Destination filename pattern when copying/moving.</summary>
    public string DestinationPattern { get; set; } = "%fn%.%l%.%fe%";
}
