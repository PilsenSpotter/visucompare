using System.Windows;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;

namespace VisuCompare;

public partial class App : Application
{
    // Keep the original ID so existing Windows file associations continue to work.
    private const string ProgId = "PredPo.Bfrafr";

    private static readonly IReadOnlyDictionary<string, Color> LightPalette = new Dictionary<string, Color>
    {
        ["WindowBackgroundBrush"] = Color.FromRgb(243, 245, 247),
        ["SurfaceBrush"] = Colors.White,
        ["SurfaceSubtleBrush"] = Color.FromRgb(251, 252, 253),
        ["TextPrimaryBrush"] = Color.FromRgb(24, 33, 47),
        ["TextSecondaryBrush"] = Color.FromRgb(120, 131, 148),
        ["TextTertiaryBrush"] = Color.FromRgb(137, 147, 160),
        ["BorderBrush"] = Color.FromRgb(231, 234, 240),
        ["DividerBrush"] = Color.FromRgb(233, 237, 241),
        ["PreviewBackgroundBrush"] = Color.FromRgb(240, 243, 246),
        ["AccentBrush"] = Color.FromRgb(35, 122, 114),
        ["AccentHoverBrush"] = Color.FromRgb(28, 104, 97),
        ["AccentPressedBrush"] = Color.FromRgb(22, 87, 80),
        ["AccentLightBrush"] = Color.FromRgb(232, 244, 242),
        ["StatusBackgroundBrush"] = Color.FromRgb(242, 247, 246),
        ["StatusTextBrush"] = Color.FromRgb(65, 103, 94),
        ["StatusDotBrush"] = Color.FromRgb(58, 154, 117),
        ["BeforeBadgeBackgroundBrush"] = Color.FromRgb(255, 241, 232),
        ["BeforeBadgeTextBrush"] = Color.FromRgb(185, 106, 54),
        ["BeforeLabelBrush"] = Color.FromRgb(156, 99, 61),
        ["AfterBadgeBackgroundBrush"] = Color.FromRgb(231, 244, 241),
        ["AfterLabelBrush"] = Color.FromRgb(50, 125, 109),
        ["ButtonSurfaceBrush"] = Color.FromRgb(245, 247, 249),
        ["ButtonTextBrush"] = Color.FromRgb(39, 51, 68),
        ["ButtonBorderBrush"] = Color.FromRgb(227, 232, 238),
        ["ButtonHoverBrush"] = Color.FromRgb(237, 241, 244),
        ["LineBrush"] = Color.FromRgb(229, 233, 239),
        ["HandleBorderBrush"] = Color.FromRgb(220, 226, 232),
        ["EmptyIconBorderBrush"] = Color.FromRgb(227, 232, 237)
    };

    private static readonly IReadOnlyDictionary<string, Color> DarkPalette = new Dictionary<string, Color>
    {
        ["WindowBackgroundBrush"] = Color.FromRgb(16, 21, 27),
        ["SurfaceBrush"] = Color.FromRgb(27, 33, 42),
        ["SurfaceSubtleBrush"] = Color.FromRgb(35, 43, 54),
        ["TextPrimaryBrush"] = Color.FromRgb(235, 241, 247),
        ["TextSecondaryBrush"] = Color.FromRgb(176, 188, 202),
        ["TextTertiaryBrush"] = Color.FromRgb(142, 154, 169),
        ["BorderBrush"] = Color.FromRgb(49, 59, 72),
        ["DividerBrush"] = Color.FromRgb(58, 69, 82),
        ["PreviewBackgroundBrush"] = Color.FromRgb(18, 25, 33),
        ["AccentBrush"] = Color.FromRgb(72, 184, 165),
        ["AccentHoverBrush"] = Color.FromRgb(91, 200, 181),
        ["AccentPressedBrush"] = Color.FromRgb(39, 150, 131),
        ["AccentLightBrush"] = Color.FromRgb(32, 58, 55),
        ["StatusBackgroundBrush"] = Color.FromRgb(29, 50, 46),
        ["StatusTextBrush"] = Color.FromRgb(166, 217, 205),
        ["StatusDotBrush"] = Color.FromRgb(90, 203, 155),
        ["BeforeBadgeBackgroundBrush"] = Color.FromRgb(58, 42, 33),
        ["BeforeBadgeTextBrush"] = Color.FromRgb(243, 183, 138),
        ["BeforeLabelBrush"] = Color.FromRgb(241, 178, 135),
        ["AfterBadgeBackgroundBrush"] = Color.FromRgb(32, 58, 53),
        ["AfterLabelBrush"] = Color.FromRgb(145, 215, 198),
        ["ButtonSurfaceBrush"] = Color.FromRgb(37, 47, 59),
        ["ButtonTextBrush"] = Color.FromRgb(235, 241, 247),
        ["ButtonBorderBrush"] = Color.FromRgb(58, 70, 85),
        ["ButtonHoverBrush"] = Color.FromRgb(48, 60, 74),
        ["LineBrush"] = Color.FromRgb(53, 64, 77),
        ["HandleBorderBrush"] = Color.FromRgb(86, 98, 115),
        ["EmptyIconBorderBrush"] = Color.FromRgb(58, 69, 82)
    };

    public void SetDarkMode(bool enabled)
    {
        var palette = enabled ? DarkPalette : LightPalette;
        foreach (var item in palette)
            Resources[item.Key] = new SolidColorBrush(item.Value);
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        RegisterBfrafrFileType();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (e.Args.Length > 0 && Path.GetExtension(e.Args[0]).Equals(".bfrafr", StringComparison.OrdinalIgnoreCase))
            window.OpenComparisonFromFile(e.Args[0]);
    }

    private static void RegisterBfrafrFileType()
    {
        try
        {
            var executablePath = Path.Combine(AppContext.BaseDirectory, "VisuCompare.exe");
            if (!File.Exists(executablePath))
                executablePath = Environment.ProcessPath ?? throw new FileNotFoundException("Spustitelný soubor aplikace nebyl nalezen.");

            var changed = false;
            using (var extensionKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.bfrafr"))
            {
                var currentProgId = extensionKey?.GetValue(null) as string;
                if (!string.Equals(currentProgId, ProgId, StringComparison.OrdinalIgnoreCase))
                {
                    extensionKey?.SetValue(null, ProgId, RegistryValueKind.String);
                    changed = true;
                }
            }

            using (var openWithKey = Registry.CurrentUser.CreateSubKey(@$"Software\Classes\.bfrafr\OpenWithProgids"))
                openWithKey?.SetValue(ProgId, string.Empty, RegistryValueKind.String);

            using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
                progIdKey?.SetValue(null, "Porovnání VisuCompare", RegistryValueKind.String);

            using (var iconKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
            {
                var iconValue = $"\"{executablePath}\",0";
                if (!string.Equals(iconKey?.GetValue(null) as string, iconValue, StringComparison.OrdinalIgnoreCase))
                {
                    iconKey?.SetValue(null, iconValue, RegistryValueKind.String);
                    changed = true;
                }
            }

            using (var commandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
                commandKey?.SetValue(null, $"\"{executablePath}\" \"%1\"", RegistryValueKind.String);

            if (changed)
                SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // The app still opens files through its own Open button if Windows blocks registration.
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
