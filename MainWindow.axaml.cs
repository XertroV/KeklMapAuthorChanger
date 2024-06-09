using Avalonia.Controls;
using Avalonia.Interactivity;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using System;
using System.IO;
using GBX.NET;
using GBX.NET.LZO;
using System.Threading.Tasks;
using Avalonia.Media;
using GBX.NET.Engines.Game;

namespace KeklMapAuthorChanger
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
        InitializeComponent();
#if DEBUG
        // this.AttachDevTools();
#endif
        }

        private async void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            var result = await dialog.ShowAsync(this);
            if (result != null)
            {
                FolderPathTextBox.Text = result;
                RefreshFolderPreview();
            }
        }

        private async void RefreshFolderPreview() {
            var nbMaps = 0;
            if (!AreDirectoriesOkay()) {
                MapsInDirTextBox.Text = $"Invalid directory";
                return;
            }
            nbMaps = GetMapFiles().Length;
            MapsInDirTextBox.Text = $"{nbMaps} Maps";
        }

        private bool AreDirectoriesOkay() {
            if (string.IsNullOrEmpty(FolderPathTextBox.Text) || !Directory.Exists(FolderPathTextBox.Text)) {
                return false;
            }
            return true;
        }

        private async void ProcessButton_Click(object sender, RoutedEventArgs e)
        {
            string? folderPath = FolderPathTextBox.Text;
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                await ShowMessageBox("Error", "Please select a valid folder.");
                return;
            }

            ProcessMaps(folderPath);
        }

        private string[] GetMapFiles()
        {
            string folderPath = FolderPathTextBox.Text;
            if (folderPath == null)
            {
                return new string[0];
            }
            return Directory.GetFiles(folderPath, "*.Map.Gbx", SearchOption.TopDirectoryOnly);
        }

        private async void ProcessMaps(string folderPath)
        {
            string? authorName = AuthorNameTextBox.Text;
            string? authorLogin = AuthorLoginTextBox.Text;
            string? authorRegion = AuthorRegionTextBox.Text;
            var maps = GetMapFiles();

            if (string.IsNullOrEmpty(authorName) || string.IsNullOrEmpty(authorLogin) || string.IsNullOrEmpty(authorRegion))
            {
                _ = ShowMessageBox("Error", "Please enter author name, login, and region.");
                return;
            }
            if (18 > authorLogin.Length || authorLogin.Length > 50)
            {
                _ = ShowMessageBox("Error", $"Author login ({authorLogin}) too short or too long. should be ~22 characters long.");
                return;
            }

            string[] mapFiles = GetMapFiles();
            ProgressBar.Maximum = mapFiles.Length;
            ProgressBar.Value = 0;

            var done = 0;
            var failed = 0;
            foreach (string mapFile in mapFiles)
            {
                var filename = Path.GetFileName(mapFile);
                StatusLabel.Text = $"{ProgressBar.Value} / {ProgressBar.Maximum} ------ (done: {done}, failed: {failed})";
                try
                {
                    await Task.Delay(1);
                    AddLogMessage($"Processing {filename}...", Colors.Black);
                    var success = await ProcessMapFile(mapFile, authorName, authorLogin, authorRegion);
                    if (success) {
                        AddLogMessage($"Success {filename}...", Colors.Green);
                        done++;
                    } else {
                        AddLogMessage($"Failed {filename} (Processing did not succeed)", Colors.Red);
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    // AddLogMessage($"Error: {filename}! {ex.ToString()}", Colors.DarkRed);
                    // AddLogMessage($"Error: {filename}! {ex.StackTrace}", Colors.DarkSalmon);
                    AddLogMessage($"Error: {filename}! {ex.Message}", Colors.Red);
                }
                ProgressBar.Value++;
            }

            StatusLabel.Text = $"{ProgressBar.Value} / {ProgressBar.Maximum} ------ (done: {done}, failed: {failed})";
        }

        private async Task<bool> ProcessMapFile(string mapFile, string authorName, string authorLogin, string authorRegion)
        {
            var map = await Gbx.ParseAsync<CGameCtnChallenge>(mapFile);
            if (map is null) {
                throw new Exception("Failed to parse map file. (Parse returned null)");
            }
            var origAuthorLogin = map.Node.AuthorLogin;
            var origAuthor = map.Node.AuthorNickname;
            var origRegion = map.Node.AuthorZone;
            map.Node.AuthorLogin = authorLogin;
            map.Node.AuthorNickname = authorName;
            map.Node.AuthorZone = authorRegion;
            map.Node.MapUid = Shuffle(map.Node.MapUid);

            // if (map.Node.LightmapCache is null) {
            //     throw new Exception($"Failed to process map file by {origAuthor}. (LightmapCache is null)");
            // }
            if (map.Node.LightmapVersion is null || map.Node.LightmapVersion.Value == 0) {
                throw new Exception($"Failed to process map file by {origAuthor}. (LightmapVersion is null or == 0)");
            } else {
                // AddLogMessage($"LightmapVersion: {map.Node.LightmapVersion}", Colors.DarkBlue);
            }
            if (!HasLightmap(map)) {
                throw new Exception($"Failed to process map file by {origAuthor}. (No lightmap)");
            }
            if (map.Node.AuthorTime is null) {
                throw new Exception($"Failed to process map file by {origAuthor}. (AuthorTime is null)");
            }
            if (map.Node.GoldTime is null) {
                throw new Exception($"Failed to process map file by {origAuthor}. (GoldTime is null)");
            }
            if (map.Node.AuthorTime.Value.TotalMilliseconds < 200) {
                throw new Exception($"Failed to process map file by {origAuthor}. (AuthorTime is < 200 ms)");
            }
            if (map.Node.GoldTime == map.Node.AuthorTime) {
                throw new Exception($"Failed to process map file by {origAuthor}. (AT == GoldTime)");
            }
            if (map.Node.AuthorTime.Value.TotalMilliseconds > 18000000) {
                throw new Exception($"Failed to process map file by {origAuthor}. (AT > 300 min)");
            }
            map.Node.RemovePassword();

            map.Save(mapFile.Replace(".Map.Gbx", $"_{authorName}.Map.Gbx"));

            return true;
        }

        private static bool HasLightmap(CGameCtnChallenge map) {
            foreach (var chunk in map.Chunks) {
                if (chunk.Id == 0x304305B) {
                    // lightmap
                    CGameCtnChallenge.Chunk0304305B lightmap = (CGameCtnChallenge.Chunk0304305B)chunk;
                    if (lightmap is null) return false;
                    if (lightmap.Data is null) return false;
                    if (lightmap.Data.Length < 65535) return false;
                    return true;
                }
            }
            return false;
        }

        private void AddLogMessage(string message, Avalonia.Media.Color color)
        {
            var textBlock = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(color),
                TextWrapping = TextWrapping.Wrap,
            };
            LogOutputPanel.Children.Insert(0, textBlock);
        }

        private async System.Threading.Tasks.Task ShowMessageBox(string title, string message)
        {
            var messageBoxStandardWindow = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ButtonDefinitions = ButtonEnum.Ok,
                ContentTitle = title,
                ContentMessage = message,
                // Icon = Icon.Error
            });

            await messageBoxStandardWindow.ShowAsPopupAsync(this);
        }

        private static Random rng = new Random();

        public static string Shuffle(string str)
        {
            char[] array = str.ToCharArray();
            int n = array.Length;
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                char temp = array[i];
                array[i] = array[j];
                array[j] = temp;
            }
            return new string(array);
        }
    }
}
