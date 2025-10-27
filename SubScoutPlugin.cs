using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace SubScout;

/// <summary>
/// SubScout plugin entry + admin page registration.
/// </summary>
public sealed class SubScoutPlugin : BasePlugin<SubScoutConfiguration>, IHasWebPages
{
    // Jellyfin expects THIS constructor signature.
    public SubScoutPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        // SubScoutPlugin.cs  (inside the constructor after Instance = this;)
        try
        {
            var cfg = Configuration ?? new SubScoutConfiguration();
            bool changed = false;

            // Templates
            if (cfg.Templates == null || cfg.Templates.Length == 0)
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
                changed = true;
            }

            // Extensions
            if (cfg.Extensions == null || cfg.Extensions.Length == 0)
            {
                cfg.Extensions = new[] { ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx" };
                changed = true;
            }

            // LanguageSynonyms
            if (cfg.LanguageSynonyms == null || cfg.LanguageSynonyms.Length == 0)
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
                changed = true;
            }

            // Scalars (only set if null/blank or obviously defaulted wrong)
            if (string.IsNullOrWhiteSpace(cfg.DestinationPattern)) { cfg.DestinationPattern = "%fn%.%l%.%fe%"; changed = true; }

            // The rest already default correctly from the config ctor:
            // AllowDeepMatch=true, MaxDepth=0, UseCultureLanguageMap=true,
            // OnlyPathContains="", OnlyNameContains="", CopyToMediaFolder=true,
            // MoveInsteadOfCopy=false, OverwriteExisting=false

            if (changed)
                UpdateConfiguration(cfg);
        }
        catch
        {
            // Swallow—plugin must remain loadable even if config is corrupt.
        }

    }

    public static SubScoutPlugin? Instance { get; private set; }

    public override string Name => SubScoutConstants.DISPLAYNAME;
    public override string Description => SubScoutConstants.DESCRIPTION;
    public override Guid Id => Guid.Parse(SubScoutConstants.PLUGINGUID);

    public IEnumerable<PluginPageInfo> GetPages()
    {
        // EXACT manifest names present in your DLL:
        //   SubScout.Web.SubScoutPage.html
        //   SubScout.Web.SubScoutScript.js
        const string htmlManifest = "SubScout.Web.SubScoutPage.html";
        const string jsManifest   = "SubScout.Web.SubScoutScript.js";

        // New route
        yield return new PluginPageInfo
        {
            Name = "subscout",
            EmbeddedResourcePath = htmlManifest
        };

        // Back-compat: the dashboard still links to LocalSubsPage for you
        yield return new PluginPageInfo
        {
            Name = "LocalSubsPage",
            EmbeddedResourcePath = htmlManifest
        };

        // Serve the controller script at __plugin/SubScoutScript.js
        yield return new PluginPageInfo
        {
            Name = "SubScoutScript.js",
            EmbeddedResourcePath = jsManifest
        };
    }
}
