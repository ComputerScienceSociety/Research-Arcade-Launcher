﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Xml;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Runtime.InteropServices;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json.Linq;
using SharpDX.DirectInput;
using System.Threading.Tasks;
using XamlAnimatedGif;

namespace ArcademiaGameLauncher
{
    enum LauncherState
    {
        ready,
        failed,
        downloadingGame,
        downloadingUpdate
    }

    public class ControllerState
    {
        private int index;
        private bool[] buttonStates;
        private int leftStickX;
        private int leftStickY;
        private int rightStickX;
        private int rightStickY;

        int joystickDeadzone = 7700;
        int joystickMidpoint = 32767;

        public Joystick joystick;
        public JoystickState state;

        public ControllerState(Joystick _joystick, int index)
        {
            this.index = index;
            joystick = _joystick;
            state = new JoystickState();

            buttonStates = new bool[128];
            state = joystick.GetCurrentState();
        }

        public void UpdateButtonStates()
        {
            joystick.Poll();
            state = joystick.GetCurrentState();

            leftStickX = state.X;
            leftStickY = state.Y;

            rightStickX = state.RotationX;
            rightStickY = state.RotationY;

            for (int i = 0; i < buttonStates.Length; i++)
            {
                SetButtonState(i, state.Buttons[i]);
            }
        }

        public int GetLeftStickX()
        {
            return leftStickX;
        }
        public int GetLeftStickY() {
            return leftStickY;
        }
        public int GetRightStickX()
        {
            return rightStickX;
        }
        public int GetRightStickY()
        {
            return rightStickY;
        }

        public int[] GetLeftStickDirection()
        {
            int[] direction = new int[2];

            if (leftStickX > joystickMidpoint + joystickDeadzone)
            {
                direction[0] = 1;
            }
            else if (leftStickX < joystickMidpoint - joystickDeadzone)
            {
                direction[0] = -1;
            }
            else
            {
                direction[0] = 0;
            }

            if (leftStickY > joystickMidpoint + joystickDeadzone)
            {
                direction[1] = 1;
            }
            else if (leftStickY < joystickMidpoint - joystickDeadzone)
            {
                direction[1] = -1;
            }
            else
            {
                direction[1] = 0;
            }

            return direction;
        }

        public int[] GetRightStickDirection()
        {
            int[] direction = new int[2];

            if (rightStickX > joystickMidpoint + joystickDeadzone)
            {
                direction[0] = 1;
            }
            else if (rightStickX < joystickMidpoint - joystickDeadzone)
            {
                direction[0] = -1;
            }
            else
            {
                direction[0] = 0;
            }

            if (rightStickY > joystickMidpoint + joystickDeadzone)
            {
                direction[1] = 1;
            }
            else if (rightStickY < joystickMidpoint - joystickDeadzone)
            {
                direction[1] = -1;
            }
            else
            {
                direction[1] = 0;
            }

            return direction;
        }

        public void SetButtonState(int _button, bool _state)
        {
            buttonStates[_button] = _state;
        }

        public bool GetButtonState(int _button)
        {
            return buttonStates[_button];
        }
    }

    public partial class MainWindow : Window
    {
        [DllImport("User32.dll")]
        public static extern int GetAsyncKeyState(Int32 i);

        [DllImport("User32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);



        private string rootPath;
        private string gameDirectoryPath;

        private string configPath;
        private string gameDatabaseURL;

        private string localGameDatabasePath;
        private JObject gameDatabaseFile;

        private string localGameInfoPath;
        private JObject gameInfoFile;

        int updateIndexOfGame;
        private System.Timers.Timer aTimer;

        int selectionUpdateInterval = 150;
        int selectionUpdateInternalCounter = 0;
        int selectionUpdateInternalCounterMax = 10;
        int selectionUpdateCounter = 0;

        int currentlySelectedHomeIndex = 0;

        int currentlySelectedGameIndex;
        int previousPageIndex = 0;

        int afkTimer = 0;
        bool afkTimerActive = false;

        int timeSinceLastButton = 0;

        private JObject[] gameInfoFilesList;

        private TextBlock[] homeOptionsList;
        private TextBlock[] gameTitlesList;

        private DirectInput directInput;
        private List<ControllerState> controllerStates = new List<ControllerState>();

        private Process currentlyRunningProcess;

        private LauncherState _state;
        internal LauncherState State
        {
            get => _state;
            set
            {
                _state = value;
                switch (_state)
                {
                    case LauncherState.ready:
                        StartButton.IsEnabled = true;
                        StartButton.Content = "Start";
                        break;
                    case LauncherState.failed:
                        StartButton.IsEnabled = false;
                        StartButton.Content = "Failed";
                        break;
                    case LauncherState.downloadingGame:
                        StartButton.IsEnabled = false;
                        StartButton.Content = "Downloading...";
                        break;
                    case LauncherState.downloadingUpdate:
                        StartButton.IsEnabled = false;
                        StartButton.Content = "Updating...";
                        break;
                    default:
                        break;
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            rootPath = Directory.GetCurrentDirectory();

            configPath = Path.Combine(rootPath, "Config.json");
            gameDirectoryPath = Path.Combine(rootPath, "Games");

            localGameDatabasePath = Path.Combine(gameDirectoryPath, "GameDatabase.json");
            localGameInfoPath = "";

            // Create the games directory if it doesn't exist
            if (!Directory.Exists(gameDirectoryPath))
                Directory.CreateDirectory(gameDirectoryPath);
        }

        private void CheckForUpdates()
        {
            if (gameDatabaseFile["Games"][updateIndexOfGame]["FolderName"] == null)
                gameDatabaseFile["Games"][updateIndexOfGame]["FolderName"] = "";

            string folderName = gameDatabaseFile["Games"][updateIndexOfGame]["FolderName"].ToString();

            if (folderName != "")
                localGameInfoPath = Path.Combine(gameDirectoryPath, folderName, "GameInfo.json");

            if (localGameInfoPath != "" && File.Exists(localGameInfoPath))
            {
                gameInfoFile = JObject.Parse(File.ReadAllText(localGameInfoPath));
                gameInfoFilesList[updateIndexOfGame] = gameInfoFile;
                if (updateIndexOfGame < 10)
                {
                    gameTitlesList[updateIndexOfGame].Text = gameInfoFile["GameName"].ToString();
                    gameTitlesList[updateIndexOfGame].Visibility = Visibility.Visible;
                }

                Version localVersion = new Version(gameInfoFile["GameVersion"].ToString());
                VersionText.Text = "v" + localVersion.ToString();

                try
                {
                    WebClient webClient = new WebClient();
                    JObject onlineJson = JObject.Parse(webClient.DownloadString(gameDatabaseFile["Games"][updateIndexOfGame]["LinkToGameInfo"].ToString()));

                    Version onlineVersion = new Version(onlineJson["GameVersion"].ToString());

                    if (onlineVersion.IsDifferentVersion(localVersion))
                    {
                        InstallGameFiles(true, onlineJson, gameDatabaseFile["Games"][updateIndexOfGame]["LinkToGameInfo"].ToString());
                    }
                    else
                    {
                        State = LauncherState.ready;
                    }
                }
                catch (Exception ex)
                {
                    State = LauncherState.failed;
                    MessageBox.Show($"Failed to check for updates: {ex.Message}");
                }
            }
            else
            {
                InstallGameFiles(false, JObject.Parse("{\r\n\"GameVersion\": \"0.0.0\"\r\n}\r\n"), gameDatabaseFile["Games"][updateIndexOfGame]["LinkToGameInfo"].ToString());
            }
        }

        private void InstallGameFiles(bool _isUpdate, JObject _onlineJson, string _downloadURL)
        {
            try
            {
                WebClient webClient = new WebClient();
                if (_isUpdate)
                {
                    State = LauncherState.downloadingUpdate;
                }
                else
                {
                    State = LauncherState.downloadingGame;
                    _onlineJson = JObject.Parse(webClient.DownloadString(_downloadURL));
                }

                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadGameCompletedCallback);
                webClient.DownloadFileAsync(new Uri(_onlineJson["LinkToGameZip"].ToString()), Path.Combine(rootPath, _onlineJson["GameName"].ToString() + ".zip"), _onlineJson);
            }
            catch (Exception ex)
            {
                State = LauncherState.failed;
                MessageBox.Show($"Failed installing game files: {ex.Message}");
            }
        }

        private void DownloadGameCompletedCallback(object sender, AsyncCompletedEventArgs e)
        {
            try
            {
                JObject onlineJson = (JObject)e.UserState;

                int currentUpdateIndexOfGame = -1;

                WebClient webClient = new WebClient();
                JArray games = (JArray)gameDatabaseFile["Games"];
                for (int i = 0; i < games.Count; i++)
                {
                    if (JObject.Parse(webClient.DownloadString(games[i]["LinkToGameInfo"].ToString()))["LinkToGameZip"].ToString() == onlineJson["LinkToGameZip"].ToString())
                    {
                        currentUpdateIndexOfGame = i;
                        break;
                    }
                }

                if (currentUpdateIndexOfGame == -1)
                {
                    MessageBox.Show("Failed to update game: Game not found in database.");
                    return;
                }

                string pathToZip = Path.Combine(rootPath, onlineJson["FolderName"].ToString() + ".zip");
                FastZip fastZip = new FastZip();
                fastZip.ExtractZip(pathToZip, Path.Combine(gameDirectoryPath, onlineJson["FolderName"].ToString()), null);
                File.Delete(pathToZip);

                JObject gameDatabase = JObject.Parse(File.ReadAllText(localGameDatabasePath));
                gameDatabase["Games"][currentUpdateIndexOfGame]["FolderName"] = onlineJson["FolderName"].ToString();
                File.WriteAllText(localGameDatabasePath, gameDatabase.ToString());

                gameDatabaseFile = gameDatabase;

                localGameInfoPath = Path.Combine(gameDirectoryPath, onlineJson["FolderName"].ToString(), "GameInfo.json");

                File.WriteAllText(localGameInfoPath, onlineJson.ToString());

                gameInfoFile = onlineJson;
                gameInfoFilesList[currentUpdateIndexOfGame] = onlineJson;
                if (currentUpdateIndexOfGame < 10)
                {
                    gameTitlesList[currentUpdateIndexOfGame].Text = onlineJson["GameName"].ToString();
                    gameTitlesList[currentUpdateIndexOfGame].Visibility = Visibility.Visible;
                }

                State = LauncherState.ready;
            }
            catch (Exception ex)
            {
                State = LauncherState.failed;
                MessageBox.Show($"Failed to complete download: {ex.Message}");
            }
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            Copyright.Text = "Copyright ©️ 2018 - " + DateTime.Now.Year + " University of Lincoln, All rights reserved.";
            homeOptionsList = new TextBlock[3] { GameLibraryText, AboutText, ExitText };
            gameTitlesList = new TextBlock[10] { GameTitleText0, GameTitleText1, GameTitleText2, GameTitleText3, GameTitleText4, GameTitleText5, GameTitleText6, GameTitleText7, GameTitleText8, GameTitleText9 };

            GenerateCredits();

            bool foundGameDatabase = false;

            if (File.Exists(configPath))
            {
                gameDatabaseURL = JObject.Parse(File.ReadAllText(configPath))["GameDatabaseURL"].ToString();

                try
                {
                    WebClient webClient = new WebClient();
                    gameDatabaseFile = JObject.Parse(webClient.DownloadString(gameDatabaseURL));

                    foundGameDatabase = true;

                    if (!File.Exists(localGameDatabasePath))
                    {
                        File.WriteAllText(localGameDatabasePath, gameDatabaseFile.ToString());
                    }

                    // Save the FolderName property of each local game and write it to the new game database file
                    JObject localGameDatabaseFile = JObject.Parse(File.ReadAllText(localGameDatabasePath));
                    JArray localGames = (JArray)localGameDatabaseFile["Games"];

                    gameInfoFilesList = new JObject[localGames.Count];

                    for (int i = 0; i < localGames.Count; i++)
                    {
                        // Check if the game is missing the FolderName property
                        if (localGames[i]["FolderName"] == null)
                        {
                            gameDatabaseFile["Games"][i]["FolderName"] = "";
                        }
                        else
                        {
                            gameDatabaseFile["Games"][i]["FolderName"] = localGames[i]["FolderName"];
                        }
                    }

                    File.WriteAllText(localGameDatabasePath, gameDatabaseFile.ToString());

                    JArray games = (JArray)gameDatabaseFile["Games"];

                    if (games.Count > 0)
                    {
                        for (int i = 0; i < games.Count; i++)
                        {
                            updateIndexOfGame = i;
                            CheckForUpdates();
                        }
                    }
                    else
                    {
                        MessageBox.Show("Failed to get game database: No games found.");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to get game database: {ex.Message}");
                }
            }
            else MessageBox.Show("Failed to get game database URL: GameDatabaseURL.json does not exist.");
            
            if (!foundGameDatabase)
            {
                // Quit the application
                Application.Current.Shutdown();
            }


            // Initialize Direct Input
            directInput = new DirectInput();

            // Find a JoyStick Guid
            var joystickGuid = Guid.Empty;

            // Find a Gamepad Guid
            foreach (var deviceInstance in directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AllDevices))
            {
                joystickGuid = deviceInstance.InstanceGuid;
                break;
            }

            // If no Gamepad is found, find a Joystick
            if (joystickGuid == Guid.Empty)
            {
                foreach (var deviceInstance in directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AllDevices))
                {
                    joystickGuid = deviceInstance.InstanceGuid;
                    break;
                }
            }

            // If no Joystick is found, throw an error
            if (joystickGuid == Guid.Empty)
            {
                MessageBox.Show("No joystick or gamepad found.");
                Application.Current.Shutdown();
                return;
            }

            // Instantiate the joystick
            Joystick joystick = new Joystick(directInput, joystickGuid);

            // Query all suported ForceFeedback effects
            var allEffects = joystick.GetEffects();
            foreach (var effectInfo in allEffects)
            {
                Console.WriteLine(effectInfo.Name);
            }

            // Set BufferSize in order to use buffered data.
            joystick.Properties.BufferSize = 128;

            // Acquire the joystick
            joystick.Acquire();

            // Create a new ControllerState object for the joystick
            ControllerState controllerState = new ControllerState(joystick, controllerStates.Count);
            controllerStates.Add(controllerState);

            currentlySelectedGameIndex = 0;

            UpdateGameInfoDisplay();

            // Timer Setup
            aTimer = new System.Timers.Timer();
            aTimer.Interval = 10;

            // Hook up the Elapsed event for the timer. 
            aTimer.Elapsed += OnTimedEvent;

            // Have the timer fire repeated events (true is the default)
            aTimer.AutoReset = true;

            // Start the timer
            aTimer.Enabled = true;
        }

        private void GenerateCredits()
        {
            // Read the Credits.json file
            string creditsPath = Path.Combine(rootPath, "Credits.json");

            if (File.Exists(creditsPath))
            {
                JObject creditsFile = JObject.Parse(File.ReadAllText(creditsPath));

                JArray creditsArray = (JArray)creditsFile["Credits"];

                CreditsPanel.RowDefinitions.Clear();
                CreditsPanel.Children.Clear();

                // Create a new TextBlock for each credit
                for (int i = 0; i < creditsArray.Count; i++)
                {
                    switch(creditsArray[i]["Type"].ToString())
                    {
                        case "Title":
                            // Create a new RowDefinition
                            RowDefinition titleRow = new RowDefinition();
                            titleRow.Height = new GridLength(60, GridUnitType.Pixel);
                            CreditsPanel.RowDefinitions.Add(titleRow);

                            // Create a new Grid
                            Grid titleGrid = new Grid();
                            Grid.SetRow(titleGrid, 2 * i);
                            titleGrid.HorizontalAlignment = HorizontalAlignment.Left;
                            titleGrid.VerticalAlignment = VerticalAlignment.Center;

                            // Create 2 new RowDefinitions
                            RowDefinition titleGridTitleRow = new RowDefinition();
                            titleGridTitleRow.Height = new GridLength(40, GridUnitType.Pixel);
                            titleGrid.RowDefinitions.Add(titleGridTitleRow);

                            RowDefinition titleGridSubtitleRow = new RowDefinition();
                            titleGridSubtitleRow.Height = new GridLength(20, GridUnitType.Pixel);
                            titleGrid.RowDefinitions.Add(titleGridSubtitleRow);

                            // Create a new TextBlock (Title)
                            TextBlock titleText = new TextBlock();
                            titleText.Text = creditsArray[i]["Value"].ToString();
                            titleText.Style = (Style)FindResource("Early GameBoy");
                            titleText.FontSize = 24;
                            titleText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
                            titleText.HorizontalAlignment = HorizontalAlignment.Left;
                            titleText.VerticalAlignment = VerticalAlignment.Center;

                            // Add the TextBlock to the Grid
                            Grid.SetRow(titleText, 0);
                            titleGrid.Children.Add(titleText);

                            // Create a new TextBlock (Subtitle)
                            if (creditsArray[i]["Subtitle"] != null)
                            {
                                TextBlock subtitleText = new TextBlock();
                                subtitleText.Text = creditsArray[i]["Subtitle"].ToString();
                                subtitleText.Style = (Style)FindResource("Early GameBoy");
                                subtitleText.FontSize = 16;
                                subtitleText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
                                subtitleText.HorizontalAlignment = HorizontalAlignment.Left;
                                subtitleText.VerticalAlignment = VerticalAlignment.Center;

                                // Add the TextBlock to the Grid
                                Grid.SetRow(subtitleText, 1);
                                titleGrid.Children.Add(subtitleText);
                            }

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(titleGrid);

                            break;
                        case "Heading":
                            // Check the Subheadings property
                            JArray subheadingsArray = (JArray)creditsArray[i]["Subheadings"];

                            // Create a new RowDefinition
                            RowDefinition headingRow = new RowDefinition();
                            headingRow.Height = new GridLength(30 + (subheadingsArray.Count * 25), GridUnitType.Pixel);
                            CreditsPanel.RowDefinitions.Add(headingRow);

                            // Create a new Grid
                            Grid headingGrid = new Grid();
                            Grid.SetRow(headingGrid, 2 * i);
                            headingGrid.HorizontalAlignment = HorizontalAlignment.Left;
                            headingGrid.VerticalAlignment = VerticalAlignment.Center;

                            // Create 2 new RowDefinitions
                            RowDefinition headingGridTitleRow = new RowDefinition();
                            headingGridTitleRow.Height = new GridLength(30, GridUnitType.Pixel);
                            headingGrid.RowDefinitions.Add(headingGridTitleRow);

                            RowDefinition headingGridSubheadingsRow = new RowDefinition();
                            headingGridSubheadingsRow.Height = new GridLength(subheadingsArray.Count * 25, GridUnitType.Pixel);
                            headingGrid.RowDefinitions.Add(headingGridSubheadingsRow);

                            // Create a new TextBlock (Title)
                            TextBlock headingText = new TextBlock();
                            headingText.Text = creditsArray[i]["Value"].ToString();
                            headingText.Style = (Style)FindResource("Early GameBoy");
                            headingText.FontSize = 20;
                            headingText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
                            headingText.HorizontalAlignment = HorizontalAlignment.Left;
                            headingText.VerticalAlignment = VerticalAlignment.Center;

                            // Add the TextBlock to the Grid
                            Grid.SetRow(headingText, 0);
                            headingGrid.Children.Add(headingText);

                            // Create a new Grid for the Subheadings
                            Grid subheadingsGrid = new Grid();
                            Grid.SetRow(subheadingsGrid, 1);
                            subheadingsGrid.HorizontalAlignment = HorizontalAlignment.Left;
                            subheadingsGrid.VerticalAlignment = VerticalAlignment.Center;

                            // For each Subheading
                            for (int j = 0; j < subheadingsArray.Count; j++)
                            {
                                // Create new RowDefinitions & for each Subheading
                                RowDefinition subheadingRow = new RowDefinition();
                                subheadingRow.Height = new GridLength(25, GridUnitType.Pixel);
                                subheadingsGrid.RowDefinitions.Add(subheadingRow);

                                // Create a new TextBlock (Subheading)
                                TextBlock subheadingText = new TextBlock();
                                subheadingText.Text = subheadingsArray[j]["Value"].ToString();
                                subheadingText.Style = (Style)FindResource("Early GameBoy");
                                subheadingText.FontSize = 16;
                                subheadingText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(subheadingsArray[j]["Colour"].ToString()));
                                subheadingText.HorizontalAlignment = HorizontalAlignment.Left;
                                subheadingText.VerticalAlignment = VerticalAlignment.Center;

                                // Add the TextBlock to the Grid
                                Grid.SetRow(subheadingText, j);
                                subheadingsGrid.Children.Add(subheadingText);
                            }

                            // Add the Subheading Grid to the Heading Grid
                            headingGrid.Children.Add(subheadingsGrid);

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(headingGrid);

                            break;
                        case "Note":
                            int noteHeight = 25 + (creditsArray[i]["Value"].ToString().Length / 100 * 25);

                            // Create a new RowDefinition
                            RowDefinition noteRow = new RowDefinition();
                            noteRow.Height = new GridLength(noteHeight, GridUnitType.Pixel);
                            CreditsPanel.RowDefinitions.Add(noteRow);

                            // Create a new Grid
                            Grid noteGrid = new Grid();
                            Grid.SetRow(noteGrid, 2 * i);
                            noteGrid.HorizontalAlignment = HorizontalAlignment.Left;
                            noteGrid.VerticalAlignment = VerticalAlignment.Center;

                            // Create a new TextBlock
                            TextBlock noteText = new TextBlock();
                            noteText.Text = creditsArray[i]["Value"].ToString();
                            noteText.Style = (Style)FindResource("Early GameBoy");
                            noteText.FontSize = 16;
                            noteText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
                            noteText.HorizontalAlignment = HorizontalAlignment.Left;
                            noteText.VerticalAlignment = VerticalAlignment.Center;
                            noteText.TextWrapping = TextWrapping.Wrap;

                            // Add the TextBlock to the Grid
                            noteGrid.Children.Add(noteText);

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(noteGrid);

                            break;
                        case "Break":
                            // Create a new RowDefinition
                            RowDefinition breakRow = new RowDefinition();
                            breakRow.Height = new GridLength(25, GridUnitType.Pixel);
                            CreditsPanel.RowDefinitions.Add(breakRow);

                            // Create a new Grid
                            Grid breakGrid = new Grid();
                            Grid.SetRow(breakGrid, 2 * i);
                            breakGrid.HorizontalAlignment = HorizontalAlignment.Left;
                            breakGrid.VerticalAlignment = VerticalAlignment.Center;

                            // Create a new TextBlock
                            TextBlock breakText = new TextBlock();
                            breakText.Text = "----------------------";
                            breakText.Style = (Style)FindResource("Early GameBoy");
                            breakText.FontSize = 16;
                            breakText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
                            breakText.HorizontalAlignment = HorizontalAlignment.Left;
                            breakText.VerticalAlignment = VerticalAlignment.Center;

                            // Add the TextBlock to the Grid
                            breakGrid.Children.Add(breakText);

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(breakGrid);

                            break;
                        case "Image":
                            // Create a new RowDefinition
                            RowDefinition imageRow = new RowDefinition();
                            imageRow.Height = new GridLength(100, GridUnitType.Pixel);
                            CreditsPanel.RowDefinitions.Add(imageRow);

                            // Create a new Grid
                            Grid imageGrid = new Grid();
                            Grid.SetRow(imageGrid, 2 * i);
                            imageGrid.HorizontalAlignment = HorizontalAlignment.Left;
                            imageGrid.VerticalAlignment = VerticalAlignment.Center;

                            string imagePath = creditsArray[i]["Path"].ToString();

                            // Create a new Image (Static)
                            Image imageStatic = new Image();
                            imageStatic.Source = new BitmapImage(new Uri(imagePath, UriKind.Relative));
                            imageStatic.Stretch = Stretch.None;

                            // Add the Image to the Grid
                            imageGrid.Children.Add(imageStatic);

                            // Set Grid Height to Image Height
                            double imageHeight = imageStatic.Source.Height;
                            imageGrid.Height = imageHeight;

                            // Create a new Image (Gif)
                            if (imagePath.EndsWith(".gif"))
                            {
                                // Copy GifTemplateElement_Parent's child element to make a new Image
                                Image imageGif = CloneXamlElement((Image)GifTemplateElement_Parent.Children[0]);
                                AnimationBehavior.SetSourceUri(imageGif, new Uri(imagePath, UriKind.Relative));
                                imageGif.Stretch = Stretch.None;

                                // Add the Image to the Grid
                                imageGrid.Children.Add(imageGif);

                                AnimationBehavior.AddLoadedHandler(imageGif, (sender, e) =>
                                {
                                    // Hide the static image
                                    imageStatic.Visibility = Visibility.Collapsed;
                                });
                            }

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(imageGrid);

                            break;
                        default:
                            break;
                    }


                    // Create a space between each credit
                    if (i < creditsArray.Count - 1)
                    {
                        RowDefinition spaceRow = new RowDefinition();
                        spaceRow.Height = new GridLength(25, GridUnitType.Pixel);
                        CreditsPanel.RowDefinitions.Add(spaceRow);
                    }
                }
            }
        }

        private void GameLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the Selection Menu
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StartMenu.Visibility = Visibility.Collapsed;
                        HomeMenu.Visibility = Visibility.Collapsed;
                        SelectionMenu.Visibility = Visibility.Visible;
                    });
                }
                catch (TaskCanceledException) { }
            }

            // Set the focus to the game launcher
            SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);

            // Set the currently selected game index to 0
            currentlySelectedGameIndex = 0;
            UpdateGameInfoDisplay();
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            // Open the About menu
            MessageBox.Show("Arcademia Game Launcher\n\nVersion: 1.0.0\n\nDeveloped by:\nMatthew Freeman\n\n©️ 2018 - " + DateTime.Now.Year + " University of Lincoln, All rights reserved.");
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the Start Menu
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StartMenu.Visibility = Visibility.Visible;
                        HomeMenu.Visibility = Visibility.Collapsed;
                        SelectionMenu.Visibility = Visibility.Collapsed;
                    });
                }
                catch (TaskCanceledException) { }
            }

            // Set the focus to the game launcher
            SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);

            // Reset AFK Timer after Half a Second
            Task.Delay(500).ContinueWith(t =>
            {
                afkTimerActive = false;
                afkTimer = 0;
            });
        }

        private void BackFromGameLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the Home Menu
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StartMenu.Visibility = Visibility.Collapsed;
                        HomeMenu.Visibility = Visibility.Visible;
                        SelectionMenu.Visibility = Visibility.Collapsed;
                    });
                }
                catch (TaskCanceledException) { }
            }

            // Set the focus to the game launcher
            SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);

            // Set the currently selected Home Index to 0
            currentlySelectedHomeIndex = 0;
            HighlightCurrentHomeMenuOption();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            string currentGameFolder = gameDatabaseFile["Games"][currentlySelectedGameIndex]["FolderName"].ToString();

            JObject currentGameInfo = JObject.Parse(File.ReadAllText(Path.Combine(gameDirectoryPath, currentGameFolder, "GameInfo.json")));
            string currentGameExe = Path.Combine(gameDirectoryPath, currentGameFolder, currentGameInfo["NameOfExecutable"].ToString());

            if (File.Exists(currentGameExe) && State == LauncherState.ready)
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(currentGameExe);
                startInfo.WorkingDirectory = currentGameFolder;
                if (currentlyRunningProcess == null || currentlyRunningProcess.HasExited)
                    currentlyRunningProcess = Process.Start(startInfo);
                else // Set focus to the currently running process
                    SetForegroundWindow(currentlyRunningProcess.MainWindowHandle);
            }
            else if (State == LauncherState.failed)
            {
                CheckForUpdates();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            aTimer.Stop();
            aTimer.Dispose();
        }

        private void OnTimedEvent(Object source, System.Timers.ElapsedEventArgs e)
        {
            controllerStates[0].UpdateButtonStates();

            // Keylogger for AFK Timer
            if (afkTimerActive)
            {
                // Check for any key press and reset the timer if any key is pressed
                for (int i = 8; i < 91; i++)
                    if (GetAsyncKeyState(i) != 0)
                    {
                        afkTimer = 0;
                        break;
                    }
            }
            else
            {
                // Check for any key press and start the timer if any key is pressed
                for (int i = 8; i < 91; i++)
                    if (GetAsyncKeyState(i) != 0)
                    {
                        afkTimerActive = true;
                        afkTimer = 0;
                        timeSinceLastButton = 0;

                        // Show the Home Menu
                        if (Application.Current != null && Application.Current.Dispatcher != null)
                        {
                            try
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    StartMenu.Visibility = Visibility.Collapsed;
                                    HomeMenu.Visibility = Visibility.Visible;
                                    SelectionMenu.Visibility = Visibility.Collapsed;

                                    currentlySelectedHomeIndex = 0;
                                    HighlightCurrentHomeMenuOption();
                                });
                            }
                            catch (TaskCanceledException) { }
                        }

                        // Set the focus to the game launcher
                        SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);

                        break;
                    }
            }

            // If the user is AFK for 3 minutes, Warn them and then close the currently running application
            if (afkTimer >= 180000)
            {
                if (afkTimer >= 185000)
                {
                    // Reset the timer
                    afkTimerActive = false;
                    afkTimer = 0;

                    // Close the currently running application
                    if (currentlyRunningProcess != null && !currentlyRunningProcess.HasExited)
                    {
                        currentlyRunningProcess.Kill();
                        SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
                        currentlyRunningProcess = null;
                    }

                    // Show the Start Menu
                    if (Application.Current != null && Application.Current.Dispatcher != null)
                    {
                        try
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                StartMenu.Visibility = Visibility.Visible;
                                HomeMenu.Visibility = Visibility.Collapsed;
                                SelectionMenu.Visibility = Visibility.Collapsed;
                            });
                        }
                        catch (TaskCanceledException) { }
                    }

                }
                else
                {
                    // Warn the user

                }
            }
            else
            {
                // Hide the warning

            }

            // Update the Selection Menu
            if ((HomeMenu.Visibility == Visibility.Visible || SelectionMenu.Visibility == Visibility.Visible) &&
                Application.Current != null &&
                Application.Current.Dispatcher != null)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateCurrentSelection();
                    });
                }
                catch (TaskCanceledException) { }
            }

            // Check if the currently running process has exited, and set the focus back to the launcher
            if (currentlyRunningProcess != null && currentlyRunningProcess.HasExited)
            {
                SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
                currentlyRunningProcess = null;
            }

            if (afkTimerActive)
                afkTimer += 10;

            if (selectionUpdateCounter > selectionUpdateInterval)
                selectionUpdateInternalCounter = 0;
            selectionUpdateCounter += 10;
            timeSinceLastButton += 10;
        }

        private void UpdateCurrentSelection()
        {
            double multiplier = 1.00;

            if (selectionUpdateInternalCounter > 0)
                multiplier = (double)1.00 - ((double)selectionUpdateInternalCounter / ((double)selectionUpdateInternalCounterMax * 1.6));

            if (selectionUpdateCounter >= selectionUpdateInterval * multiplier)
            {
                int[] leftStickDirection = controllerStates[0].GetLeftStickDirection();
                int[] rightStickDirection = controllerStates[0].GetRightStickDirection();

                if (leftStickDirection[1] == -1 || rightStickDirection[1] == -1)
                {
                    selectionUpdateCounter = 0;
                    if (selectionUpdateInternalCounter < selectionUpdateInternalCounterMax)
                        selectionUpdateInternalCounter++;

                    if (HomeMenu.Visibility == Visibility.Visible)
                    {
                        currentlySelectedHomeIndex -= 1;
                        if (currentlySelectedHomeIndex < 0)
                            currentlySelectedHomeIndex = 0;

                        HighlightCurrentHomeMenuOption();
                    }
                    else if (SelectionMenu.Visibility == Visibility.Visible)
                    {
                        currentlySelectedGameIndex -= 1;
                        if (currentlySelectedGameIndex < -1)
                            currentlySelectedGameIndex = -1;

                        UpdateGameInfoDisplay();
                    }
                }
                else if (leftStickDirection[1] == 1 || rightStickDirection[1] == 1)
                {
                    selectionUpdateCounter = 0;
                    if (selectionUpdateInternalCounter < selectionUpdateInternalCounterMax)
                        selectionUpdateInternalCounter++;

                    if (HomeMenu.Visibility == Visibility.Visible)
                    {
                        currentlySelectedHomeIndex += 1;
                        if (currentlySelectedHomeIndex > 2)
                            currentlySelectedHomeIndex = 2;

                        HighlightCurrentHomeMenuOption();
                    }
                    else if (SelectionMenu.Visibility == Visibility.Visible)
                    {
                        currentlySelectedGameIndex += 1;
                        if (currentlySelectedGameIndex > gameInfoFilesList.Length - 1)
                            currentlySelectedGameIndex = gameInfoFilesList.Length - 1;

                        UpdateGameInfoDisplay();
                    }
                }
            }

            // Check if the A button is pressed
            if (timeSinceLastButton > 250 && controllerStates[0].GetButtonState(0))
            {
                timeSinceLastButton = 0;

                if (HomeMenu.Visibility == Visibility.Visible)
                {
                    if (currentlySelectedHomeIndex == 0)
                    {
                        // Run the GameLibraryButton_Click method
                        GameLibraryButton_Click(null, null);
                    }
                    else if (currentlySelectedHomeIndex == 1)
                    {
                        // Run the AboutButton_Click method
                        AboutButton_Click(null, null);
                    }
                    else if (currentlySelectedHomeIndex == 2)
                    {
                        // Run the ExitButton_Click method
                        ExitButton_Click(null, null);
                    }
                }
                else if (SelectionMenu.Visibility == Visibility.Visible)
                {
                    if (currentlySelectedGameIndex >= 0) StartButton_Click(null, null);
                    else BackFromGameLibraryButton_Click(null, null);
                }
            }

            // Check if the B button is pressed
            if (timeSinceLastButton > 250 && controllerStates[0].GetButtonState(1))
            {
                timeSinceLastButton = 0;

                if (HomeMenu.Visibility == Visibility.Visible)
                {
                    ExitButton_Click(null, null);
                }
                else if (SelectionMenu.Visibility == Visibility.Visible)
                {
                    BackFromGameLibraryButton_Click(null, null);
                }
            }
        }

        private void HighlightCurrentHomeMenuOption()
        {
            foreach (TextBlock option in homeOptionsList)
                option.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));

            homeOptionsList[currentlySelectedHomeIndex].Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xBA, 0x3D, 0x71));
        }

        private void ChangePage(int _pageIndex)
        {
            if (_pageIndex < 0)
                _pageIndex = 0;
            else if (_pageIndex > gameInfoFilesList.Length / 10)
                _pageIndex = gameInfoFilesList.Length / 10;

            previousPageIndex = _pageIndex;

            for (int i = 0; i < 10; i++)
            {
                gameTitlesList[i].Visibility = Visibility.Hidden;
            }

            for (int i = 0; i < 10; i++)
            {
                if (i + _pageIndex * 10 >= gameInfoFilesList.Length)
                    break;

                gameTitlesList[i].Text = gameInfoFilesList[i + _pageIndex * 10]["GameName"].ToString();
                gameTitlesList[i].Visibility = Visibility.Visible;
            }
        }

        private void ResetTitles()
        {
            for (int i = 0; i < 10; i++)
            {
                gameTitlesList[i].Visibility = Visibility.Hidden;
            }
        }

        private void ResetGameInfoDisplay()
        {
            NonGif_GameThumbnail.Source = new BitmapImage(new Uri("Images/ThumbnailPlaceholder.png", UriKind.Relative));
            AnimationBehavior.SetSourceUri(Gif_GameThumbnail, new Uri("Images/ThumbnailPlaceholder.png", UriKind.Relative));

            GameTitle.Text = "Select A Game";
            GameAuthors.Text = "";
            GameDescription.Text = "Select a game using the joystick and by pressing A.";
            VersionText.Text = "";

            GameTagBorder0.Visibility = Visibility.Hidden;
            GameTagBorder1.Visibility = Visibility.Hidden;
            GameTagBorder2.Visibility = Visibility.Hidden;
            GameTagBorder3.Visibility = Visibility.Hidden;
            GameTagBorder4.Visibility = Visibility.Hidden;
            GameTagBorder5.Visibility = Visibility.Hidden;
            GameTagBorder6.Visibility = Visibility.Hidden;
            GameTagBorder7.Visibility = Visibility.Hidden;
            GameTagBorder8.Visibility = Visibility.Hidden;

            GameTag0.Text = "";
            GameTag1.Text = "";
            GameTag2.Text = "";
            GameTag3.Text = "";
            GameTag4.Text = "";
            GameTag5.Text = "";
            GameTag6.Text = "";
            GameTag7.Text = "";
            GameTag8.Text = "";
            
            GameTag0.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag1.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag2.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag3.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag4.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag5.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag6.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag7.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag8.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));

            GameTagBorder0.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder1.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder2.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder3.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder4.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder5.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder6.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder7.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder8.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
        }

        private void UpdateGameInfoDisplay()
        {

            if (currentlySelectedGameIndex < 0)
            {
                ResetGameInfoDisplay();

                foreach (TextBlock title in gameTitlesList)
                    title.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));

                BackFromGameLibraryButton.IsChecked = true;
                StartButton.IsChecked = false;
                StartButton.Content = "Select a Game";
                StartButton.IsEnabled = false;

                return;
            }
            BackFromGameLibraryButton.IsChecked = false;
            StartButton.Content = "Start";

            int pageIndex = currentlySelectedGameIndex / 10;
            if (pageIndex != previousPageIndex)
            {
                ChangePage(pageIndex);
            }

            for (int i = pageIndex * 10; i < (pageIndex + 1) * 10; i++)
            {

                if (i == currentlySelectedGameIndex)
                {
                    gameTitlesList[i % 10].Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xBA, 0x3D, 0x71));

                    // Update the game info
                    if (gameInfoFilesList[i] != null)
                    {
                        ResetGameInfoDisplay();

                        StartButton.IsChecked = true;

                        NonGif_GameThumbnail.Source = new BitmapImage(new Uri(Path.Combine(gameDirectoryPath, gameInfoFilesList[i]["FolderName"].ToString(), gameInfoFilesList[i]["GameThumbnail"].ToString()), UriKind.Absolute));
                        AnimationBehavior.SetSourceUri(Gif_GameThumbnail, new Uri(Path.Combine(gameDirectoryPath, gameInfoFilesList[i]["FolderName"].ToString(), gameInfoFilesList[i]["GameThumbnail"].ToString()), UriKind.Absolute));

                        GameTitle.Text = gameInfoFilesList[i]["GameName"].ToString();
                        GameAuthors.Text = string.Join(", ", gameInfoFilesList[i]["GameAuthors"].ToObject<string[]>());

                        Border[] GameTagBorder = new Border[9] { GameTagBorder0, GameTagBorder1, GameTagBorder2, GameTagBorder3, GameTagBorder4, GameTagBorder5, GameTagBorder6, GameTagBorder7, GameTagBorder8 };
                        TextBlock[] GameTag = new TextBlock[9] { GameTag0, GameTag1, GameTag2, GameTag3, GameTag4, GameTag5, GameTag6, GameTag7, GameTag8 };

                        JArray tags = (JArray)gameInfoFilesList[i]["GameTags"];

                        for (int j = 0; j < tags.Count; j++)
                        {
                            // Change Visibility
                            GameTagBorder[j].Visibility = Visibility.Visible;

                            // Change Text Content
                            GameTag[j].Text = tags[j]["Name"].ToString();

                            // Change Border and Text Colour
                            string colour = "#FF777777";

                            if (tags[j]["Colour"] != null && tags[j]["Colour"].ToString() != "")
                            {
                                colour = tags[j]["Colour"].ToString();
                            }

                            GameTag[j].Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour));
                            GameTagBorder[j].BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour));
                        }

                        GameDescription.Text = gameInfoFilesList[i]["GameDescription"].ToString();

                        VersionText.Text = "v" + gameInfoFilesList[i]["GameVersion"].ToString();
                    }
                }
                else
                {
                    gameTitlesList[i % 10].Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
                }
            }
        }
    
        private T CloneXamlElement<T>(T element) where T : UIElement
        {
            string xaml = XamlWriter.Save(element);
            StringReader stringReader = new StringReader(xaml);
            XmlReader xmlReader = XmlReader.Create(stringReader);
            return (T)XamlReader.Load(xmlReader);
        }
    }

    struct Version
    {
        internal static Version zero = new Version(0, 0, 0);

        public int major;
        public int minor;
        public int subMinor;

        internal Version(short _major, short _minor, short _subMinor)
        {
            major = _major;
            minor = _minor;
            subMinor = _subMinor;
        }

        internal Version(string version)
        {
            string[] parts = version.Split('.');
            
            if (parts.Length != 3)
            {
                major = 0;
                minor = 0;
                subMinor = 0;
                return;
            }

            major = int.Parse(parts[0]);
            minor = int.Parse(parts[1]);
            subMinor = int.Parse(parts[2]);
        }

        internal bool IsDifferentVersion(Version _otherVersion)
        {
            if (major != _otherVersion.major)
                return true;
            else if (minor != _otherVersion.minor)
                return true;
            else if (subMinor != _otherVersion.subMinor)
                return true;
            else return false;
        }

        public override string ToString()
        {
            return $"{major}.{minor}.{subMinor}";
        }
    }
}
