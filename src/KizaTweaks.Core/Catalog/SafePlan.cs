using KizaTweaks.Core.Hardware;

namespace KizaTweaks.Core.Catalog;

public sealed record SafeSection(string Id, string Title, string Description, string Glyph, IReadOnlyList<string> TweakIds);

/// <summary>
/// Optimización segura: solo cosas que no cambian cómo funcionan tus programas (Photoshop, Illustrator,
/// juegos, anticheats). Lo que puede dar problemas en algunos PCs vive aparte, en «Avanzado».
/// </summary>
public static class SafePlan
{
    /// <summary>Solo en escritorio: en portátil gastan batería o quitan la hibernación.</summary>
    public static readonly IReadOnlySet<string> DesktopOnly = new HashSet<string> { "kiza.powerHigh", "kiza.hibernate" };

    private static readonly SafeSection[] All =
    {
        new("junk", "Quitar lo inservible",
            "Widgets, Copilot, Recall, Bing en el menú Inicio y las apps que Windows instala solo. Nada que uses para trabajar o jugar.",
            "",
            new[]
            {
                "kiza.widgets", "q.DisableCopilot", "q.DisableRecall", "q.DisableClickToDo", "q.DisableAISvcAutoStart",
                "q.DisableBing", "q.DisableStartRecommended", "q.DisableStartPhoneLink", "kiza.consumerFeatures",
                "q.DisableDeviceAutoAppDownload",
            }),
        new("input", "Menos input lag",
            "El mouse y el teclado responden antes y los puertos no se duermen. No cambia cómo corren tus juegos ni tus apps.",
            "",
            new[]
            {
                "q.DisableMouseAcceleration", "kiza.keyboardRepeat", "q.DisableStickyKeys", "kiza.accessibilityShortcuts",
                "kiza.usbSuspend", "kiza.pcieLspm", "q.DisableDVR", "kiza.gameMode",
            }),
        new("perf", "Rendimiento seguro",
            "Energía en alto rendimiento, arranque sin esperas y sin reinicios sorpresa mientras trabajas.",
            "",
            new[]
            {
                "kiza.powerHigh", "kiza.startupDelay", "q.DisableFastStartup", "kiza.hibernate",
                "q.PreventUpdateAutoReboot", "q.DisableDeliveryOptimization", "q.EnableEndTask",
            }),
        new("privacy", "Sin anuncios ni rastreo",
            "Telemetría, sugerencias y anuncios de Windows, Edge y Configuración. Ninguna función que uses deja de servir.",
            "",
            new[]
            {
                "q.DisableTelemetry", "kiza.telemetryServices", "q.DisableSuggestions", "q.DisableLockscreenTips",
                "q.DisableEdgeAds", "q.DisableSettings365Ads",
            }),
    };

    /// <summary>Secciones con los ajustes que aplican a este equipo (sin los de escritorio en portátiles).</summary>
    public static IReadOnlyList<SafeSection> Sections(TweakCatalog c, HardwareReport? hw = null)
    {
        bool laptop = hw?.IsLaptop == true;
        return All.Select(s => s with
        {
            TweakIds = s.TweakIds.Where(id => c.Find(id) is { Advanced: false } && !(laptop && DesktopOnly.Contains(id))).ToList(),
        }).ToList();
    }

    public static HashSet<string> AllTweakIds(TweakCatalog c, HardwareReport? hw = null) =>
        Sections(c, hw).SelectMany(s => s.TweakIds).ToHashSet();

    /// <summary>Apps que Windows instala o sugiere solo. Nunca Store, Xbox, Fotos, Paint, Calculadora ni drivers.</summary>
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

    /// <summary>Apps que suelen usarse: salen en la lista pero desmarcadas.</summary>
    public static readonly IReadOnlySet<string> AppsUncheckedByDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "5319275A.WhatsAppDesktop", "SpotifyAB.SpotifyMusic", "4DF9E0F8.Netflix", "Disney.37853FC22B2CE",
        "AmazonVideo.PrimeVideo", "ROBLOXCORPORATION.ROBLOX", "FACEBOOK.317180B0BB486", "AppleInc.iCloud",
        "AppleInc.AppleDevices", "AppleInc.AppleMusicWin",
    };

    public static IEnumerable<AppDefinition> Apps(AppCatalog apps) => apps.Apps.Where(a => a.Ids.Any(AppIds.Contains));
}
