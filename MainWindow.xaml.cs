using System.IO.Compression;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PredPo;

public partial class MainWindow : Window
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PredPo");
    private static readonly string ImagesDirectory = Path.Combine(DataDirectory, "Images");
    private static readonly string StateFile = Path.Combine(DataDirectory, "state.json");
    private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"];
    private readonly List<PresentationSlide> _presentationSlides = [];

    private string? _beforePath;
    private string? _afterPath;
    private string? _beforeName;
    private string? _afterName;
    private bool _isDragging;
    private bool _isUpdatingSlider;
    private bool _isDarkMode;
    private bool _autoSaveEnabled = true;
    private int _currentSlideIndex = -1;
    private bool _isFullscreen;
    private WindowStyle _previousWindowStyle;
    private ResizeMode _previousResizeMode;
    private WindowState _previousWindowState;
    private bool _previousTopmost;
    private Rect _previousBounds;

    public MainWindow()
    {
        InitializeComponent();
        LoadSavedState();
        CleanupStoredImages();
        ApplyTheme(_isDarkMode);
        UpdatePreview();
    }

    private void ApplyTheme(bool darkMode)
    {
        if (Application.Current is App app)
            app.SetDarkMode(darkMode);
        ThemeToggleButton.Content = darkMode ? "Světlý režim" : "Tmavý režim";
        UpdateAutoSaveButton();
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        _isDarkMode = !_isDarkMode;
        ApplyTheme(_isDarkMode);
        SaveState();
    }

    private void ToggleAutoSave_Click(object sender, RoutedEventArgs e)
    {
        _autoSaveEnabled = !_autoSaveEnabled;
        UpdateAutoSaveButton();
        SaveState();
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 || (_isFullscreen && e.Key == Key.Escape))
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (_presentationSlides.Count > 0 && e.Key == Key.Left)
        {
            NavigatePresentation(-1);
            e.Handled = true;
        }
        else if (_presentationSlides.Count > 0 && e.Key == Key.Right)
        {
            NavigatePresentation(1);
            e.Handled = true;
        }
    }

    private void PreviousSlide_Click(object sender, RoutedEventArgs e) => NavigatePresentation(-1);
    private void NextSlide_Click(object sender, RoutedEventArgs e) => NavigatePresentation(1);

    private void AddCurrentSlide_Click(object sender, RoutedEventArgs e)
    {
        if (_beforePath is null || _afterPath is null || BeforeImage.Source is null || AfterImage.Source is null)
        {
            MessageBox.Show(this, "Nejprve vyber obrázek PŘED i PO.", "Nelze přidat snímek", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _presentationSlides.Add(CaptureCurrentSlide());
        _currentSlideIndex = _presentationSlides.Count - 1;
        UpdatePresentationControls();
    }

    private void AddComparisonFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Přidat porovnání jako snímky",
            Filter = "Soubory Před / Po (*.bfrafr)|*.bfrafr",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true) return;
        var added = 0;
        foreach (var file in dialog.FileNames)
        {
            var imported = new List<string>();
            try
            {
                _presentationSlides.Add(LoadComparisonSlide(file, imported));
                added++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or JsonException or ArgumentException)
            {
                DeleteImportedFiles(imported);
                MessageBox.Show(this, $"Soubor se nepodařilo přidat jako snímek:\n{file}\n\n{ex.Message}", "Chyba při přidávání", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        if (added > 0)
        {
            _currentSlideIndex = _presentationSlides.Count - 1;
            ShowPresentationSlide(_currentSlideIndex);
            CleanupStoredImages();
        }
    }

    private void SavePresentation_Click(object sender, RoutedEventArgs e)
    {
        if (_presentationSlides.Count == 0) return;
        var dialog = new SaveFileDialog
        {
            Title = "Uložit prezentaci",
            Filter = "Prezentace Před / Po (*.bfrafr)|*.bfrafr",
            DefaultExt = ".bfrafr",
            AddExtension = true,
            FileName = "Prezentace.bfrafr",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) == true)
            SavePresentationFile(dialog.FileName);
    }

    private void NavigatePresentation(int direction)
    {
        if (_presentationSlides.Count == 0) return;
        var target = _currentSlideIndex < 0
            ? (direction > 0 ? 0 : _presentationSlides.Count - 1)
            : Math.Clamp(_currentSlideIndex + direction, 0, _presentationSlides.Count - 1);
        if (target != _currentSlideIndex)
            ShowPresentationSlide(target);
    }

    private void ShowPresentationSlide(int index)
    {
        if (index < 0 || index >= _presentationSlides.Count) return;
        _currentSlideIndex = index;
        var slide = _presentationSlides[index];
        _beforePath = slide.BeforePath;
        _beforeName = slide.BeforeName;
        _afterPath = slide.AfterPath;
        _afterName = slide.AfterName;
        _isUpdatingSlider = true;
        PositionSlider.Value = slide.Position;
        _isUpdatingSlider = false;
        UpdatePreview();
        UpdatePresentationControls();
        if (_autoSaveEnabled) SaveState();
    }

    private PresentationSlide CaptureCurrentSlide() =>
        new(_beforePath, _beforeName, _afterPath, _afterName, PositionSlider.Value);

    private void UpdateCurrentPresentationSlide()
    {
        if (_currentSlideIndex >= 0 && _currentSlideIndex < _presentationSlides.Count)
            _presentationSlides[_currentSlideIndex] = CaptureCurrentSlide();
    }

    private void UpdatePresentationControls()
    {
        var count = _presentationSlides.Count;
        SlideCounter.Text = count == 0
            ? "0 snímků"
            : _currentSlideIndex < 0 ? $"Nový • {count} snímků" : $"{_currentSlideIndex + 1} / {count}";
        PreviousSlideButton.IsEnabled = count > 0 && (_currentSlideIndex < 0 || _currentSlideIndex > 0);
        NextSlideButton.IsEnabled = count > 0 && (_currentSlideIndex < 0 || _currentSlideIndex < count - 1);
        SavePresentationButton.IsEnabled = count > 0;
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _previousWindowStyle = WindowStyle;
            _previousResizeMode = ResizeMode;
            _previousWindowState = WindowState;
            _previousTopmost = Topmost;
            _previousBounds = new Rect(Left, Top, Width, Height);

            HeaderRow.Height = new GridLength(0);
            HeaderBar.Visibility = Visibility.Collapsed;
            ControlsColumn.Width = new GridLength(0);
            GapColumn.Width = new GridLength(0);
            ControlsPanel.Visibility = Visibility.Collapsed;
            MainContentGrid.Margin = new Thickness(0);
            PreviewCard.Padding = new Thickness(0);
            PreviewCard.BorderThickness = new Thickness(0);
            PreviewCard.CornerRadius = new CornerRadius(0);
            PreviewHeading.Visibility = Visibility.Collapsed;
            PositionControlRow.Visibility = Visibility.Collapsed;
            PreviewFrame.BorderThickness = new Thickness(0);
            PreviewFrame.CornerRadius = new CornerRadius(0);

            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
            FullscreenButton.ToolTip = "Ukončit celou obrazovku (Esc)";
        }
        else
        {
            HeaderRow.Height = new GridLength(82);
            HeaderBar.Visibility = Visibility.Visible;
            ControlsColumn.Width = new GridLength(300);
            GapColumn.Width = new GridLength(24);
            ControlsPanel.Visibility = Visibility.Visible;
            MainContentGrid.Margin = new Thickness(34, 25, 34, 28);
            PreviewCard.Padding = new Thickness(22);
            PreviewCard.BorderThickness = new Thickness(1);
            PreviewCard.CornerRadius = new CornerRadius(16);
            PreviewHeading.Visibility = Visibility.Visible;
            PositionControlRow.Visibility = Visibility.Visible;
            PreviewFrame.BorderThickness = new Thickness(1);
            PreviewFrame.CornerRadius = new CornerRadius(12);

            WindowState = WindowState.Normal;
            Topmost = _previousTopmost;
            WindowStyle = _previousWindowStyle;
            ResizeMode = _previousResizeMode;
            if (_previousWindowState == WindowState.Normal)
            {
                Left = _previousBounds.Left;
                Top = _previousBounds.Top;
                Width = _previousBounds.Width;
                Height = _previousBounds.Height;
            }
            WindowState = _previousWindowState;
            _isFullscreen = false;
            FullscreenButton.ToolTip = "Celá obrazovka (F11)";
        }
    }

    private void UpdateAutoSaveButton()
    {
        AutoSaveLabel.Text = _autoSaveEnabled ? "Auto ukládání: zapnuto" : "Auto ukládání: vypnuto";
        AutoSaveDot.Opacity = _autoSaveEnabled ? 1 : 0.35;
    }

    private void ChooseBefore_Click(object sender, RoutedEventArgs e) => ChooseImage(isBefore: true);
    private void ChooseAfter_Click(object sender, RoutedEventArgs e) => ChooseImage(isBefore: false);

    private void SaveComparison_Click(object sender, RoutedEventArgs e)
    {
        if (_beforePath is null && _afterPath is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Uložit porovnání",
            Filter = "Soubor Před / Po (*.bfrafr)|*.bfrafr",
            DefaultExt = ".bfrafr",
            AddExtension = true,
            FileName = "Moje porovnání.bfrafr",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) == true)
            SaveComparisonFile(dialog.FileName);
    }

    private void OpenComparison_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Otevřít porovnání",
            Filter = "Soubor Před / Po (*.bfrafr)|*.bfrafr",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            OpenComparisonFromFile(dialog.FileName);
    }

    private void SaveComparisonFile(string path)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = File.Create(temporaryPath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var beforeExtension = AddImageToArchive(archive, _beforePath, "images/before");
                var afterExtension = AddImageToArchive(archive, _afterPath, "images/after");
                var document = new ComparisonDocument(
                    FormatVersion: 1,
                    BeforeExtension: beforeExtension,
                    BeforeName: beforeExtension is null ? null : _beforeName,
                    AfterExtension: afterExtension,
                    AfterName: afterExtension is null ? null : _afterName,
                    Position: PositionSlider.Value);

                var entry = archive.CreateEntry("comparison.json", CompressionLevel.Optimal);
                using var manifest = entry.Open();
                JsonSerializer.Serialize(manifest, document, new JsonSerializerOptions { WriteIndented = true });
            }

            var imageCount = VerifyComparisonArchive(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
            MessageBox.Show(this, $"Uloženo včetně {imageCount} obrázků a polohy dělící čáry.\n\n{path}", "Porovnání uloženo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or JsonException)
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            MessageBox.Show(this, $"Porovnání se nepodařilo uložit.\n\n{ex.Message}", "Chyba ukládání", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static int VerifyComparisonArchive(string path)
    {
        using var file = File.OpenRead(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("comparison.json")
            ?? throw new InvalidDataException("V uloženém souboru chybí nastavení porovnání.");
        using var manifest = manifestEntry.Open();
        var document = JsonSerializer.Deserialize<ComparisonDocument>(manifest)
            ?? throw new InvalidDataException("Uložené nastavení porovnání nelze načíst.");

        var imageCount = 0;
        VerifyImageEntry(archive, "before", document.BeforeExtension, ref imageCount);
        VerifyImageEntry(archive, "after", document.AfterExtension, ref imageCount);
        if (imageCount == 0)
            throw new InvalidDataException("Soubor neobsahuje žádný obrázek.");
        return imageCount;
    }

    private static void VerifyImageEntry(ZipArchive archive, string role, string? extension, ref int imageCount)
    {
        if (string.IsNullOrWhiteSpace(extension)) return;
        if (!SupportedExtensions.Contains(extension))
            throw new InvalidDataException($"Uložený formát obrázku {role} není podporován.");

        var entry = archive.GetEntry($"images/{role}{extension}");
        if (entry is null || entry.Length == 0)
            throw new InvalidDataException($"Obrázek {role} se do souboru neuložil.");
        imageCount++;
    }

    private void SavePresentationFile(string path)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = File.Create(temporaryPath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var savedSlides = new List<PresentationSlideDocument>(_presentationSlides.Count);
                for (var i = 0; i < _presentationSlides.Count; i++)
                {
                    var slide = _presentationSlides[i];
                    var folder = $"slides/{i + 1:D4}";
                    var beforeExtension = AddImageToArchive(archive, slide.BeforePath, $"{folder}/before");
                    var afterExtension = AddImageToArchive(archive, slide.AfterPath, $"{folder}/after");
                    if (beforeExtension is null || afterExtension is null)
                        throw new InvalidDataException($"Snímek {i + 1} nemá oba obrázky.");
                    savedSlides.Add(new PresentationSlideDocument(
                        beforeExtension, slide.BeforeName, afterExtension, slide.AfterName, slide.Position));
                }

                var manifestEntry = archive.CreateEntry("presentation.json", CompressionLevel.Optimal);
                using var manifest = manifestEntry.Open();
                JsonSerializer.Serialize(manifest, new PresentationDocument(1, savedSlides), new JsonSerializerOptions { WriteIndented = true });
            }

            var imageCount = VerifyPresentationArchive(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
            MessageBox.Show(this, $"Prezentace uložena jako jeden soubor. Obsahuje {_presentationSlides.Count} snímků a {imageCount} obrázků.\n\n{path}", "Prezentace uložena", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or JsonException)
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            MessageBox.Show(this, $"Prezentaci se nepodařilo uložit.\n\n{ex.Message}", "Chyba ukládání", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static int VerifyPresentationArchive(string path)
    {
        using var file = File.OpenRead(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("presentation.json")
            ?? throw new InvalidDataException("V souboru chybí seznam snímků.");
        using var manifest = manifestEntry.Open();
        var document = JsonSerializer.Deserialize<PresentationDocument>(manifest)
            ?? throw new InvalidDataException("Seznam snímků nelze načíst.");
        if (document.FormatVersion != 1 || document.Slides is null || document.Slides.Count == 0)
            throw new InvalidDataException("Prezentace neobsahuje žádné platné snímky.");

        var imageCount = 0;
        for (var i = 0; i < document.Slides.Count; i++)
        {
            var folder = $"slides/{i + 1:D4}";
            VerifyPresentationImage(archive, $"{folder}/before", document.Slides[i].BeforeExtension, ref imageCount);
            VerifyPresentationImage(archive, $"{folder}/after", document.Slides[i].AfterExtension, ref imageCount);
        }
        return imageCount;
    }

    private static void VerifyPresentationImage(ZipArchive archive, string nameWithoutExtension, string? extension, ref int imageCount)
    {
        if (string.IsNullOrWhiteSpace(extension) || !SupportedExtensions.Contains(extension))
            throw new InvalidDataException("Prezentace obsahuje nepodporovaný formát obrázku.");
        var entry = archive.GetEntry(nameWithoutExtension + extension);
        if (entry is null || entry.Length == 0)
            throw new InvalidDataException("Některý obrázek se do prezentace neuložil.");
        imageCount++;
    }

    private static string? AddImageToArchive(ZipArchive archive, string? sourcePath, string nameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Uložený obrázek nebyl nalezen.", sourcePath);

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
            throw new InvalidDataException("Formát obrázku není podporován.");

        var entry = archive.CreateEntry(nameWithoutExtension + extension, CompressionLevel.Optimal);
        using var input = File.OpenRead(sourcePath);
        using var output = entry.Open();
        input.CopyTo(output);
        return extension;
    }

    internal void OpenComparisonFromFile(string path)
    {
        bool isPresentation;
        try
        {
            using var file = File.OpenRead(path);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read);
            isPresentation = archive.GetEntry("presentation.json") is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            MessageBox.Show(this, $"Soubor se nepodařilo otevřít.\n\n{ex.Message}", "Chyba při otevírání", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (isPresentation)
            LoadPresentationFile(path);
        else
            LoadSingleComparisonFile(path);
    }

    private void LoadSingleComparisonFile(string path)
    {
        var importedFiles = new List<string>();
        string? beforePath = null;
        string? afterPath = null;

        try
        {
            using (var file = File.OpenRead(path))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                var manifestEntry = archive.GetEntry("comparison.json")
                    ?? throw new InvalidDataException("Soubor neobsahuje nastavení porovnání.");
                using var manifest = manifestEntry.Open();
                var document = JsonSerializer.Deserialize<ComparisonDocument>(manifest)
                    ?? throw new InvalidDataException("Nastavení porovnání je prázdné.");

                if (document.FormatVersion != 1 || !double.IsFinite(document.Position))
                    throw new InvalidDataException("Verze nebo poloha posuvníku v souboru není platná.");

                beforePath = ExtractImageFromArchive(archive, "images/before", "before", document.BeforeExtension, importedFiles);
                afterPath = ExtractImageFromArchive(archive, "images/after", "after", document.AfterExtension, importedFiles);

                _presentationSlides.Clear();
                _currentSlideIndex = -1;
                UpdatePresentationControls();
                _beforeName = beforePath is null ? null : document.BeforeName;
                _afterName = afterPath is null ? null : document.AfterName;
                _beforePath = beforePath;
                _afterPath = afterPath;
                PositionSlider.Value = Math.Clamp(document.Position, 0, 100);
            }

            SaveState();
            CleanupStoredImages();
            UpdatePreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or JsonException or ArgumentException)
        {
            foreach (var importedFile in importedFiles)
            {
                try { File.Delete(importedFile); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            MessageBox.Show(this, $"Porovnání se nepodařilo otevřít.\n\n{ex.Message}", "Chyba při otevírání", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadPresentationFile(string path)
    {
        var importedFiles = new List<string>();
        var loadedSlides = new List<PresentationSlide>();
        try
        {
            using (var file = File.OpenRead(path))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                var manifestEntry = archive.GetEntry("presentation.json")
                    ?? throw new InvalidDataException("Soubor neobsahuje seznam snímků.");
                using var manifest = manifestEntry.Open();
                var document = JsonSerializer.Deserialize<PresentationDocument>(manifest)
                    ?? throw new InvalidDataException("Seznam snímků je prázdný.");
                if (document.FormatVersion != 1 || document.Slides is null || document.Slides.Count == 0)
                    throw new InvalidDataException("Prezentace neobsahuje žádné platné snímky.");

                for (var i = 0; i < document.Slides.Count; i++)
                {
                    var savedSlide = document.Slides[i];
                    if (!double.IsFinite(savedSlide.Position))
                        throw new InvalidDataException($"Poloha čáry na snímku {i + 1} není platná.");
                    var folder = $"slides/{i + 1:D4}";
                    var beforePath = ExtractImageFromArchive(archive, $"{folder}/before", "before", savedSlide.BeforeExtension, importedFiles);
                    var afterPath = ExtractImageFromArchive(archive, $"{folder}/after", "after", savedSlide.AfterExtension, importedFiles);
                    if (beforePath is null || afterPath is null)
                        throw new InvalidDataException($"Snímek {i + 1} neobsahuje oba obrázky.");
                    loadedSlides.Add(new PresentationSlide(beforePath, savedSlide.BeforeName, afterPath, savedSlide.AfterName, Math.Clamp(savedSlide.Position, 0, 100)));
                }
            }

            _presentationSlides.Clear();
            _presentationSlides.AddRange(loadedSlides);
            _currentSlideIndex = 0;
            ShowPresentationSlide(0);
            CleanupStoredImages();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or JsonException or ArgumentException)
        {
            DeleteImportedFiles(importedFiles);
            MessageBox.Show(this, $"Prezentaci se nepodařilo otevřít.\n\n{ex.Message}", "Chyba při otevírání", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private PresentationSlide LoadComparisonSlide(string path, List<string> importedFiles)
    {
        using var file = File.OpenRead(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("comparison.json")
            ?? throw new InvalidDataException("Soubor není samostatné porovnání.");
        using var manifest = manifestEntry.Open();
        var document = JsonSerializer.Deserialize<ComparisonDocument>(manifest)
            ?? throw new InvalidDataException("Nastavení porovnání je prázdné.");
        if (document.FormatVersion != 1 || !double.IsFinite(document.Position))
            throw new InvalidDataException("Verze nebo poloha posuvníku v souboru není platná.");

        var beforePath = ExtractImageFromArchive(archive, "images/before", "before", document.BeforeExtension, importedFiles);
        var afterPath = ExtractImageFromArchive(archive, "images/after", "after", document.AfterExtension, importedFiles);
        if (beforePath is null || afterPath is null)
            throw new InvalidDataException("Porovnání musí obsahovat obrázek PŘED i PO.");
        return new PresentationSlide(beforePath, document.BeforeName, afterPath, document.AfterName, Math.Clamp(document.Position, 0, 100));
    }

    private string? ExtractImageFromArchive(ZipArchive archive, string nameWithoutExtension, string role, string? extension, List<string> importedFiles)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        extension = extension.ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
            throw new InvalidDataException("Soubor obsahuje nepodporovaný formát obrázku.");

        var entry = archive.GetEntry(nameWithoutExtension + extension)
            ?? throw new InvalidDataException($"V souboru chybí obrázek {role}.");

        Directory.CreateDirectory(ImagesDirectory);
        var destination = Path.Combine(ImagesDirectory, $"{role}-{Guid.NewGuid():N}{extension}");
        using (var input = entry.Open())
        using (var output = File.Create(destination))
            input.CopyTo(output);

        try
        {
            _ = LoadBitmap(destination);
        }
        catch (Exception ex)
        {
            try { File.Delete(destination); }
            catch (IOException) { }
            throw new InvalidDataException($"Obrázek {role} v souboru nelze načíst.", ex);
        }

        importedFiles.Add(destination);
        return destination;
    }

    private static void DeleteImportedFiles(IEnumerable<string> importedFiles)
    {
        foreach (var importedFile in importedFiles)
        {
            try { File.Delete(importedFile); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void ChooseImage(bool isBefore)
    {
        var dialog = new OpenFileDialog
        {
            Title = isBefore ? "Vyberte obrázek PŘED" : "Vyberte obrázek PO",
            Filter = "Obrázky|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|Všechny soubory|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            ImportImage(dialog.FileName, isBefore);
    }

    private void ImportImage(string sourcePath, bool isBefore)
    {
        try
        {
            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (!SupportedExtensions.Contains(extension))
                throw new InvalidDataException("Tento formát obrázku není podporován.");

            // Decode first so invalid files are rejected before replacing the saved image.
            _ = LoadBitmap(sourcePath);
            Directory.CreateDirectory(ImagesDirectory);
            var destination = Path.Combine(ImagesDirectory, $"{(isBefore ? "before" : "after")}-{Guid.NewGuid():N}{extension}");
            File.Copy(sourcePath, destination, overwrite: true);

            if (isBefore)
            {
                _beforePath = destination;
                _beforeName = Path.GetFileName(sourcePath);
            }
            else
            {
                _afterPath = destination;
                _afterName = Path.GetFileName(sourcePath);
            }

            _currentSlideIndex = -1;
            SaveState();
            CleanupStoredImages();
            UpdatePreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            MessageBox.Show(this, $"Obrázek se nepodařilo otevřít nebo uložit.\n\n{ex.Message}", "Chyba při načítání obrázku", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CleanupStoredImages()
    {
        if (!Directory.Exists(ImagesDirectory)) return;
        var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_beforePath is not null) retained.Add(Path.GetFullPath(_beforePath));
        if (_afterPath is not null) retained.Add(Path.GetFullPath(_afterPath));
        foreach (var slide in _presentationSlides)
        {
            if (slide.BeforePath is not null) retained.Add(Path.GetFullPath(slide.BeforePath));
            if (slide.AfterPath is not null) retained.Add(Path.GetFullPath(slide.AfterPath));
        }
        foreach (var file in Directory.EnumerateFiles(ImagesDirectory))
        {
            if (retained.Contains(Path.GetFullPath(file))) continue;
            try { File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.UriSource = new Uri(Path.GetFullPath(path));
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void LoadSavedState()
    {
        if (!File.Exists(StateFile)) return;

        try
        {
            var state = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(StateFile));
            if (state is null) return;
            _beforePath = ExistingImagePath(state.BeforePath);
            _afterPath = ExistingImagePath(state.AfterPath);
            _autoSaveEnabled = state.AutoSaveEnabled ?? true;
            if (!_autoSaveEnabled)
            {
                _beforePath = null;
                _afterPath = null;
            }
            _beforeName = _beforePath is null ? null : state.BeforeName;
            _afterName = _afterPath is null ? null : state.AfterName;
            _isDarkMode = state.IsDarkMode;
            if (_autoSaveEnabled)
                PositionSlider.Value = Math.Clamp(state.Position, 0, 100);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged settings file should not prevent the app from opening.
            _beforePath = null;
            _afterPath = null;
            _beforeName = null;
            _afterName = null;
            _isDarkMode = false;
            _autoSaveEnabled = true;
        }
    }

    private static string? ExistingImagePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;

    private void SaveState()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var state = _autoSaveEnabled
                ? new SavedState(_beforePath, _afterPath, PositionSlider.Value, _beforeName, _afterName, _isDarkMode, true)
                : new SavedState(null, null, 50, null, null, _isDarkMode, false);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Nastavení se nepodařilo uložit.\n\n{ex.Message}", "Chyba ukládání", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdatePreview()
    {
        var before = TryLoadImage(_beforePath);
        var after = TryLoadImage(_afterPath);
        BeforeImage.Source = before;
        AfterImage.Source = after;
        BeforeFileName.Text = DisplayName(_beforePath, _beforeName);
        AfterFileName.Text = DisplayName(_afterPath, _afterName);

        var hasAnyImage = before is not null || after is not null;
        SaveComparisonButton.IsEnabled = hasAnyImage;
        EmptyState.Visibility = hasAnyImage ? Visibility.Collapsed : Visibility.Visible;
        BeforeImage.Visibility = before is not null ? Visibility.Visible : Visibility.Collapsed;
        AfterClip.Visibility = after is not null ? Visibility.Visible : Visibility.Collapsed;
        BeforeTag.Visibility = before is not null ? Visibility.Visible : Visibility.Collapsed;
        AfterTag.Visibility = after is not null ? Visibility.Visible : Visibility.Collapsed;
        Divider.Visibility = before is not null && after is not null ? Visibility.Visible : Visibility.Collapsed;
        UpdateOverlaySize();
        UpdatePresentationControls();
    }

    private static BitmapImage? TryLoadImage(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try { return LoadBitmap(path); }
        catch { return null; }
    }

    private static string DisplayName(string? path, string? savedName) => path is not null && File.Exists(path)
        ? (string.IsNullOrWhiteSpace(savedName) ? Path.GetFileName(path) : savedName)
        : "Zatím nevybráno";

    private void PreviewArea_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateOverlaySize();

    private void UpdateOverlaySize()
    {
        if (!IsInitialized) return;
        var width = Math.Max(0, PreviewArea.ActualWidth);
        var height = Math.Max(0, PreviewArea.ActualHeight);

        var hasBefore = BeforeImage.Source is not null;
        var hasAfter = AfterImage.Source is not null;
        var split = width * PositionSlider.Value / 100d;
        AfterImage.Width = width;
        AfterImage.Height = height;
        AfterClip.Width = hasBefore ? split : (hasAfter ? width : 0);
        DividerTransform.X = split;
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateOverlaySize();
        if (!_isUpdatingSlider)
            UpdateCurrentPresentationSlide();
        if (!_isUpdatingSlider && IsInitialized && _autoSaveEnabled)
            SaveState();
    }

    private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (BeforeImage.Source is null || AfterImage.Source is null) return;
        _isDragging = true;
        PreviewArea.CaptureMouse();
        SetPositionFromPointer(e.GetPosition(PreviewArea).X);
        e.Handled = true;
    }

    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            SetPositionFromPointer(e.GetPosition(PreviewArea).X);
    }

    private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => StopDragging();
    private void Preview_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) StopDragging();
    }

    private void StopDragging()
    {
        if (!_isDragging) return;
        _isDragging = false;
        PreviewArea.ReleaseMouseCapture();
        SaveState();
    }

    private void SetPositionFromPointer(double x)
    {
        if (PreviewArea.ActualWidth <= 0) return;
        _isUpdatingSlider = true;
        PositionSlider.Value = Math.Clamp(x / PreviewArea.ActualWidth * 100d, 0, 100);
        _isUpdatingSlider = false;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _currentSlideIndex = -1;
        _beforePath = null;
        _afterPath = null;
        _beforeName = null;
        _afterName = null;
        PositionSlider.Value = 50;
        CleanupStoredImages();
        SaveState();
        UpdatePreview();
        UpdatePresentationControls();
    }

    private void Swap_Click(object sender, RoutedEventArgs e)
    {
        (_beforePath, _afterPath) = (_afterPath, _beforePath);
        (_beforeName, _afterName) = (_afterName, _beforeName);
        PositionSlider.Value = 100 - PositionSlider.Value;
        UpdateCurrentPresentationSlide();
        SaveState();
        UpdatePreview();
    }

    private void ImageCardDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void BeforeDrop(object sender, DragEventArgs e) => HandleImageDrop(e, isBefore: true);
    private void AfterDrop(object sender, DragEventArgs e) => HandleImageDrop(e, isBefore: false);

    private void HandleImageDrop(DragEventArgs e, bool isBefore)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            ImportImage(files[0], isBefore);
        e.Handled = true;
    }

    private sealed record SavedState(string? BeforePath, string? AfterPath, double Position, string? BeforeName, string? AfterName, bool IsDarkMode, bool? AutoSaveEnabled);
    private sealed record ComparisonDocument(int FormatVersion, string? BeforeExtension, string? BeforeName, string? AfterExtension, string? AfterName, double Position);
    private sealed record PresentationSlide(string? BeforePath, string? BeforeName, string? AfterPath, string? AfterName, double Position);
    private sealed record PresentationSlideDocument(string? BeforeExtension, string? BeforeName, string? AfterExtension, string? AfterName, double Position);
    private sealed record PresentationDocument(int FormatVersion, List<PresentationSlideDocument> Slides);
}
