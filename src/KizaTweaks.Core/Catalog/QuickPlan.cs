namespace KizaTweaks.Core.Catalog;

/// <summary>
/// Optimización rápida: solo cosas que no cambian cómo funcionan tus programas ni juegos.
/// Apps: únicamente relleno de Microsoft y juegos patrocinados (nada que la gente instale a propósito).
/// </summary>
public static class QuickPlan
{
    public static readonly IReadOnlySet<string> AppIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Clipchamp.Clipchamp", "Microsoft.3DBuilder", "Microsoft.549981C3F5F10", "Microsoft.BingFinance",
        "Microsoft.BingFoodAndDrink", "Microsoft.BingHealthAndFitness", "Microsoft.BingNews", "Microsoft.BingSports",
        "Microsoft.BingTravel", "Microsoft.Getstarted", "Microsoft.Messaging", "Microsoft.Microsoft3DViewer",
        "Microsoft.MicrosoftOfficeHub", "Microsoft.MicrosoftPowerBIForWindows", "Microsoft.MicrosoftSolitaireCollection",
        "Microsoft.MixedReality.Portal", "Microsoft.Office.Sway", "Microsoft.OneConnect", "Microsoft.Print3D",
        "Microsoft.PowerAutomateDesktop", "Microsoft.WindowsFeedbackHub", "Microsoft.Windows.DevHome", "MicrosoftTeams",
        "king.com.CandyCrushSaga", "king.com.CandyCrushSodaSaga", "king.com.BubbleWitch3Saga", "FarmVille2CountryEscape",
        "CaesarsSlotsFreeCasino", "MarchofEmpires", "HiddenCity", "flaregamesGmbH.RoyalRevolt", "Asphalt8Airborne",
        "DisneyMagicKingdoms", "COOKINGFEVER",
        // Apps de terceros que Windows instala o sugiere solo después de formatear
        "AppleInc.iTunes", "AppleInc.AppleMusicWin", "AppleInc.AppleTVWin", "AppleInc.AppleDevices", "AppleInc.iCloud",
        "5319275A.WhatsAppDesktop", "FACEBOOK.317180B0BB486", "FACEBOOK.FACEBOOK", "Facebook.Instagram",
        "BytedancePte.Ltd.TikTok", "4DF9E0F8.Netflix", "Disney.37853FC22B2CE", "AmazonVideo.PrimeVideo", "Amazon.com.Amazon",
        "HULULLC.HULUPLUS", "SpotifyAB.SpotifyMusic", "LinkedInforWindows", "PicsArt-PhotoStudio", "Duolingo-LearnLanguagesforFree",
        "Flipboard", "iHeartRadio", "PandoraMediaInc", "TuneInRadio", "SlingTV", "NYTCrossword", "PhototasticCollage",
        "PolarrPhotoEditorAcademicEdition", "AutodeskSketchBook", "DrawboardPDF", "CyberLinkMediaSuiteEssentials",
        "ACGMediaPlayer", "ActiproSoftwareLLC", "WinZipUniversal", "Sidia.LiveWallpaper", "OneCalendar",
        "AdobeSystemsIncorporated.AdobePhotoshopExpress", "ROBLOXCORPORATION.ROBLOX", "Microsoft.Copilot",
        "Microsoft.Edge.GameAssist", "Microsoft.BingWeather", "Microsoft.WindowsMaps",
        "Microsoft.ZuneVideo", "Microsoft.SkypeApp", "Microsoft.People", "Microsoft.Windows.AIHub", "Microsoft.PCManager",
    };

    /// <summary>Anuncios, sugerencias y rastreo de Windows: apagarlos no cambia ninguna función que uses.</summary>
    public static readonly IReadOnlySet<string> PrivacyTweaks = new HashSet<string>
    {
        "q.DisableTelemetry", "kiza.telemetryServices", "q.DisableSuggestions", "q.DisableLockscreenTips",
        "q.DisableEdgeAds", "q.DisableSettings365Ads", "kiza.consumerFeatures", "q.DisableDeviceAutoAppDownload",
        "q.DisableCopilot", "q.DisableRecall", "q.DisableClickToDo",
    };

    /// <summary>Tareas de limpieza: borran solo temporales, reportes y cachés que Windows recrea.</summary>
    public static readonly IReadOnlyList<string> CleanupTasks = new[] { "temp", "wer", "do-cache", "dns", "trim-on", "retrim" };

    /// <summary>Menos input delay sin riesgo: el perfil de entrada sin el plan de energía.</summary>
    public static HashSet<string> DelayTweaks(TweakCatalog c) =>
        ProfileResolver.Resolve(Profiles.Input, c, null).Ids.Where(id => c.Find(id) is { Risk: "safe" }).ToHashSet();

    public static IEnumerable<AppDefinition> Apps(AppCatalog apps) => apps.Apps.Where(a => a.Ids.Any(AppIds.Contains));
}
