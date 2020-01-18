﻿using Microsoft.Extensions.Configuration;
using Rofl.Executables;
using Rofl.Executables.Models;
using Rofl.Executables.Utilities;
using Rofl.Files;
using Rofl.Files.Models;
using Rofl.Requests;
using Rofl.Requests.Models;
using Rofl.Settings;
using Rofl.Settings.Models;
using Rofl.UI.Main.Extensions;
using Rofl.UI.Main.Models;
using Rofl.UI.Main.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace Rofl.UI.Main.ViewModels
{
    public class MainWindowViewModel
    {
        private readonly FileManager _fileManager;
        private readonly RequestManager _requestManager;

        /// <summary>
        /// 
        /// </summary>
        public SortPropertiesModel SortParameters { get; private set; }

        /// <summary>
        /// Smaller, preview objects of replays
        /// </summary>
        public ObservableCollection<ReplayPreviewModel> PreviewReplays { get; private set; }

        /// <summary>
        /// Full replay objects with the filepath as the key
        /// </summary>
        public Dictionary<string, FileResult> FileResults { get; private set; }

        public ObservableCollection<PlayerMarker> KnownPlayers { get; private set; }

        public SettingsManager SettingsManager { get; private set; }

        public MainWindowViewModel(FileManager files, RequestManager requests, SettingsManager settingsManager)
        {
            if (settingsManager == null) { throw new ArgumentNullException(nameof(settingsManager)); }
            SettingsManager = settingsManager;
            _fileManager = files ?? throw new ArgumentNullException(nameof(files));
            _requestManager = requests ?? throw new ArgumentNullException(nameof(requests));

            KnownPlayers = SettingsManager.Settings.KnownPlayers;

            PreviewReplays = new ObservableCollection<ReplayPreviewModel>();
            FileResults = new Dictionary<string, FileResult>();

            SortParameters = new SortPropertiesModel
            {
                SearchTerm = String.Empty,
                SortMethod = SortMethod.DateDesc
            };
        }

        public async Task LoadReplays()
        {
            var rawFileResults = await _fileManager.GetReplayFilesAsync().ConfigureAwait(false);

            foreach (var file in rawFileResults)
            {
                ReplayPreviewModel newItem = new ReplayPreviewModel(file.ReplayFile, file.FileInfo.CreationTime, file.IsNewFile);

                newItem.IsSupported = SettingsManager.Executables.DoesVersionExist(newItem.GameVersion);

                foreach (var bluePlayer in newItem.BluePreviewPlayers)
                {
                    bluePlayer.Marker = KnownPlayers.Where
                        (
                            x => x.Name.Equals(bluePlayer.PlayerName, StringComparison.OrdinalIgnoreCase)
                        ).FirstOrDefault();
                }

                foreach (var redPlayer in newItem.RedPreviewPlayers)
                {
                    redPlayer.Marker = KnownPlayers.Where
                        (
                            x => x.Name.Equals(redPlayer.PlayerName, StringComparison.OrdinalIgnoreCase)
                        ).FirstOrDefault();
                }

                App.Current.Dispatcher.Invoke((Action) delegate
                {
                    PreviewReplays.Add(newItem);
                });
                
                FileResults.Add(file.FileInfo.Path, file);
            }
        }

        public void SortPreviewReplays(CollectionViewSource replayView)
        {
            if(replayView == null) { throw new ArgumentNullException(nameof(replayView)); }

            SortMethod sort = SortParameters.SortMethod;

            switch (sort)
            {
                case SortMethod.NameAsc:
                    replayView.SortDescriptions.Clear();
                    replayView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
                    break;

                case SortMethod.NameDesc:
                    replayView.SortDescriptions.Clear();
                    replayView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Descending));
                    break;

                case SortMethod.DateAsc:
                    replayView.SortDescriptions.Clear();
                    replayView.SortDescriptions.Add(new SortDescription("CreationDate", ListSortDirection.Ascending));
                    break;

                case SortMethod.DateDesc:
                    replayView.SortDescriptions.Clear();
                    replayView.SortDescriptions.Add(new SortDescription("CreationDate", ListSortDirection.Descending));
                    break;

                case SortMethod.LengthAsc:
                    replayView.SortDescriptions.Clear();
                    replayView.SortDescriptions.Add(new SortDescription("GameDuration", ListSortDirection.Ascending));
                    break;

                case SortMethod.LengthDesc:
                    replayView.SortDescriptions.Clear();
                    replayView.SortDescriptions.Add(new SortDescription("GameDuration", ListSortDirection.Descending));
                    break;

                default:
                    break;
            }
        }

        public bool FilterPreviewReplay(ReplayPreviewModel replayItem)
        {
            if(replayItem == null) { throw new ArgumentNullException(nameof(replayItem)); }

            string searchTerm = SortParameters.SearchTerm;

            // Minimum requirement
            if(searchTerm.Length < 3) { return true; }

            if (replayItem.MapName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) { return true; }

            foreach (var bluePlayer in replayItem.BluePreviewPlayers)
            {
                if (bluePlayer.ChampionName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) { return true; }
                if (bluePlayer.PlayerName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) { return true; }
            }

            foreach (var redPlayer in replayItem.BluePreviewPlayers)
            {
                if (redPlayer.ChampionName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) { return true; }
                if (redPlayer.PlayerName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) { return true; }
            }

            return false;
        }

        public async Task LoadItemThumbnails(ReplayDetailModel replay)
        {
            if (replay == null) { throw new ArgumentNullException(nameof(replay)); }

            string dataVersion = await _requestManager.GetDataDragonVersionAsync(replay.PreviewModel.GameVersion).ConfigureAwait(false);

            List<ItemModel> allItems = new List<ItemModel>();
            List<Task> itemTasks = new List<Task>();

            allItems.AddRange(replay.BluePlayers.SelectMany(x => x.Items));
            allItems.AddRange(replay.RedPlayers.SelectMany(x => x.Items));

            foreach (var item in allItems)
            {
                itemTasks.Add(Task.Run(async () =>
                {
                    var response = await _requestManager.MakeRequestAsync(new ItemRequest
                    {
                        DataDragonVersion = dataVersion,
                        ItemID = item.ItemId
                    }).ConfigureAwait(false);

                    App.Current.Dispatcher.Invoke((Action)delegate
                    {
                        item.ImageSource = response.ResponsePath;
                    });
                }));
            }

            //await Task.WhenAll(itemTasks).ConfigureAwait(false);
        }

        public async Task LoadPreviewPlayerThumbnails()
        {
            foreach (var replay in PreviewReplays.ToList())
            {
                string dataVersion = await _requestManager.GetDataDragonVersionAsync(replay.GameVersion).ConfigureAwait(false);

                List<PlayerPreviewModel> allPlayers = new List<PlayerPreviewModel>();
                allPlayers.AddRange(replay.BluePreviewPlayers);
                allPlayers.AddRange(replay.RedPreviewPlayers);

                // Image tasks
                List<Task> imageTasks = new List<Task>();

                // Create requests for player images
                foreach (var player in allPlayers)
                {
                    imageTasks.Add(Task.Run(async () =>
                    {
                        var response = await _requestManager.MakeRequestAsync(new ChampionRequest
                        {
                            DataDragonVersion = dataVersion,
                            ChampionName = player.ChampionName
                        }).ConfigureAwait(false);

                        App.Current.Dispatcher.Invoke((Action)delegate
                        {
                            player.ImageSource = response.ResponsePath;
                        });
                    }));
                }

                // Wait for all images to finish before doing the next replay
                await Task.WhenAll(imageTasks).ConfigureAwait(false);
            }
        }

        public void ReloadPlayerMarkers()
        {
            // Look through all replays to get all players
            foreach (var replay in PreviewReplays)
            {
                IEnumerable<PlayerPreviewModel> allPlayers;
                if(replay.BluePreviewPlayers != null)
                {
                    allPlayers = replay.BluePreviewPlayers.Union(replay.RedPreviewPlayers);
                }
                else
                {
                    allPlayers = replay.RedPreviewPlayers;
                }

                foreach (var player in allPlayers)
                {
                    var matchedMarker = KnownPlayers.Where
                        (
                            x => x.Name.Equals(player.PlayerName, StringComparison.OrdinalIgnoreCase)
                        ).FirstOrDefault();

                    if(matchedMarker != null)
                    {
                        player.Marker = matchedMarker;
                    }
                }
            }
        }

        public async Task ShowSettingsDialog()
        {
            var settingsDialog = new SettingsWindow
            {
                Top = App.Current.MainWindow.Top + 50,
                Left = App.Current.MainWindow.Left + 50,
                DataContext = SettingsManager,
            };

            if (settingsDialog.ShowDialog().Equals(true))
            {
                // Refresh markers
                ReloadPlayerMarkers();

                // Refresh all replays
                FileResults.Clear();
                PreviewReplays.Clear();
                await LoadReplays().ConfigureAwait(true);
                await LoadPreviewPlayerThumbnails().ConfigureAwait(true);
            }
        }

        public void PlayReplay(ReplayPreviewModel preview)
        {
            if (preview == null) { throw new ArgumentNullException(nameof(preview)); }
            
            var replay = FileResults[preview.Location];

            var executables = SettingsManager.Executables.GetExecutablesByPatch(preview.GameVersion);

            if (!executables.Any())
            {
                MessageBox.Show
                (
                    Application.Current.TryFindResource("ExecutableNotFoundErrorText") as String + " " + preview.GameVersion,
                    Application.Current.TryFindResource("ExecutableNotFoundErrorTitle") as String,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }

            LeagueExecutable target;
            if (executables.Count > 1)
            {
                // More than one?????
                target = ShowChooseReplayDialog(executables);
                if (target == null) return;
            }
            else
            {
                // Show confirmation dialog
                var msgResult = MessageBox.Show
                    (
                        Application.Current.TryFindResource("ReplayPlayConfirmationText") as String,
                        Application.Current.TryFindResource("ReplayPlayConfirmationText") as String,
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Question
                    );

                if (msgResult != MessageBoxResult.OK) return;

                target = executables.First();
            }

            ReplayPlayer.Play(target, replay.FileInfo.Path);
        }

        public void OpenReplayContainingFolder(string location)
        {
            if (String.IsNullOrEmpty(location)) { throw new ArgumentNullException(nameof(location)); }

            FileResults.TryGetValue(location, out FileResult match);
            if (match == null) { throw new ArgumentException($"{location} does not match any known replays"); }

            string selectArg = $"/select, \"{match.FileInfo.Path}\"";
            Process.Start("explorer.exe", selectArg);
        }

        public static LeagueExecutable ShowChooseReplayDialog(IReadOnlyCollection<LeagueExecutable> executables)
        {
            var selectWindow = new ExecutableSelectWindow
            {
                Top = App.Current.MainWindow.Top + 50,
                Left = App.Current.MainWindow.Left + 50,
                DataContext = executables,
            };

            if (selectWindow.ShowDialog().Equals(true))
            {
                return selectWindow.Selection;
            }
            return null;
        }
    }
}