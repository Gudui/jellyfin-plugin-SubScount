using MediaBrowser.Controller.Providers;

namespace SubScout;

/// <summary>Constant values.</summary>
public static class SubScoutConstants
{
    /// <summary>Plugin GUID.</summary>
    public const string PLUGINGUID = "7de4aa03-f418-4e1c-a8ba-08ccecba4ab5";

    /// <summary>Plugin developer name.</summary>
    public const string PLUGINNAME = "SubScout";

    /// <summary>Display name.</summary>
    public const string DISPLAYNAME = "SubScout";

    /// <summary>Description.</summary>
    public const string DESCRIPTION = "Subtitle provider for local subtitle files.";

    /// <summary>SubtitleInfo ID section separator.</summary>
    public const char IDSEPARATOR = '-';

    /// <summary>Supported media types.</summary>
    public static readonly VideoContentType[] MEDIATYPES = [VideoContentType.Episode, VideoContentType.Movie];
}
