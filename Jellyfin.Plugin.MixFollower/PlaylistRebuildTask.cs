// <copyright file="PlaylistRebuildTask.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MixFollower
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using CliWrap;
    using CliWrap.Buffered;
    using Jellyfin.Data.Entities;
    using Jellyfin.Data.Entities.Libraries;
    using Jellyfin.Data.Enums;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Entities.Audio;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Playlists;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Dto;
    using MediaBrowser.Model.Globalization;
    using MediaBrowser.Model.Playlists;
    using MediaBrowser.Model.Search;
    using MediaBrowser.Model.Tasks;
    using Microsoft.AspNetCore.Components.Web;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Microsoft.VisualBasic;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// Deletes old log files.
    /// </summary>
    public class PlaylistRebuildTask : IScheduledTask, IConfigurableScheduledTask
    {
        private readonly Guid firstAdminId;
        private readonly User firstAdmin;
        private readonly ILibraryManager libraryManager;
        private readonly IPlaylistManager playlistManager;
        private readonly IUserManager userManager;
        private readonly ISearchEngine searchEngine;

        private readonly ILocalizationManager localization;
        private readonly ILogger<PlaylistRebuildTask> logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlaylistRebuildTask" /> class.
        /// </summary>
        /// <param name="userManager">The user manager.</param>
        /// <param name="libraryManager">The configuration manager.</param>
        /// <param name="playlistManager">The playlist manager.</param>
        /// <param name="logger">The  logger.</param>
        /// <param name="localization">The localization manager.</param>
        public PlaylistRebuildTask(ILibraryManager libraryManager, ISearchEngine searchEngine, IUserManager userManager, IPlaylistManager playlistManager, ILogger<PlaylistRebuildTask> logger, ILocalizationManager localization)
        {
            this.userManager = userManager;
            this.playlistManager = playlistManager;
            this.libraryManager = libraryManager;
            this.logger = logger;
            this.localization = localization;
            this.searchEngine = searchEngine;
            this.firstAdmin = this.userManager.Users.First(i => i.HasPermission(PermissionKind.IsAdministrator));
            this.firstAdminId = this.firstAdmin.Id;
            this.logger.LogInformation("PlaylistRebuildTask constructed");
        }

        /// <inheritdoc />
        public string Name => this.localization.GetLocalizedString("TaskPlaylistRebuild");

        /// <inheritdoc />
        public string Description => string.Format(
            CultureInfo.InvariantCulture,
            this.localization.GetLocalizedString("TaskPlaylistRebuildDescription"));

        /// <inheritdoc />
        public string Category => this.localization.GetLocalizedString("TasksMaintenanceCategory");

        /// <inheritdoc />
        public string Key => "PlaylistRebuild";

        /// <inheritdoc />
        public bool IsHidden => false;

        /// <inheritdoc />
        public bool IsEnabled => true;

        /// <inheritdoc />
        public bool IsLogged => true;

        /// <summary>
        /// Creates the triggers that define when the task will run.
        /// </summary>
        /// <returns>IEnumerable{BaseTaskTrigger}.</returns>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            this.logger.LogInformation("PlaylistRebuild GetDefaultTriggers");
            return new[]
            {
                new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerStartup },

                new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerInterval, IntervalTicks = TimeSpan.FromHours(24).Ticks },
            };
        }

        private void DeletePlaylist(string playlist_name)
        {
            var playlists = this.playlistManager.GetPlaylists(this.firstAdminId);
            var playlist = playlists.FirstOrDefault(playlist => playlist.Name == playlist_name);
            if (playlist is null)
            {
                this.logger.LogInformation("there is no playlist {Name}", playlist_name);
                return;
            }

            this.libraryManager.DeleteItem(playlist, new DeleteOptions { DeleteFileLocation = true }, true);
            this.logger.LogInformation("matched and deleted ");
        }

        private async Task<string> CreatePlaylistFromFetchCommand(Guid user, string command)
        {
            this.logger.LogInformation("cli command executing {Command}", command);
            CliWrap.Buffered.BufferedCommandResult? result = null;
            try
            {
                result = await Cli.Wrap(command).ExecuteBufferedAsync().ConfigureAwait(false);

                // result.ToString();
                string json = result.StandardOutput.ToString();

                JObject obj = JObject.Parse(json);

                string playlist_name = obj.GetValue("name").ToString();

                var songs = obj.GetValue("songs");

                var list_items = new List<Guid>();
                foreach (var song in songs.Children<JObject>())
                {
                    var title = song.GetValue("title").ToString();
                    var artist = song.GetValue("artist").ToString();

                    var item = this.GetMostMatchedSong(title, artist);
                    if (item is null)
                    {
                        item = await this.DownloadMusic(title, artist).ConfigureAwait(false);
                    }
                }

                // _iLibraryMonitor.ReportFileSystemChangeComplete(path, false);
                await this.libraryManager.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None).ConfigureAwait(false);
                this.DeletePlaylist(playlist_name);

                foreach (var song in songs.Children<JObject>())
                {
                    var title = song.GetValue("title").ToString();
                    var artist = song.GetValue("artist").ToString();

                    var item = this.GetMostMatchedSong(title, artist);
                    if (item is null)
                    {
                        item = this.GetMostMatchedSongWithLibrarySearch(title, artist);
                        if (item is null)
                        {
                            continue;
                        }
                    }

                    list_items.Add(item.Id);
                }

                var playlist = await this.playlistManager.CreatePlaylist(new PlaylistCreationRequest
                {
                    Name = playlist_name,
                    ItemIdList = list_items,
                    UserId = user,
                    MediaType = Data.Enums.MediaType.Audio,

                    // Users = [],
                    Public = true,
                }).ConfigureAwait(false);

                return playlist.Id;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                this.logger.LogInformation("executing {command} gets crash! {msg} ", command, exception.Message);
                this.logger.LogInformation("{stack_trace}", exception.StackTrace.ToString());
            }

            return string.Empty;
        }

        private Audio? ConvertSearchHintInfoToAudio(SearchHintInfo hintInfo)
        {
            var item = hintInfo.Item;
            return this.ConvertItemToAudio(item);
        }

        private Audio? ConvertItemToAudio(BaseItem item)
        {
            switch (item)
            {
                case Audio song:
                    return song;
                default:
                    return null;
            }
        }

        private Audio? GetMostMatchedSongWithLibrarySearch(string title, string artist)
        {
            this.logger.LogInformation("LibrarySearchQuerying with {Query}", title);
            var query = new InternalItemsQuery(this.firstAdmin)
            {
                MediaTypes =[MediaType.Audio,],
            };
            var tokenized_artist = artist.Split(['(', ' ', ')']);

            var result = this.libraryManager.GetItemList(query);
            this.logger.LogInformation("# of query results ( all music) : {Count}", result.Count);
            this.logger.LogInformation("The first item path : {Path}", result.FirstOrDefault().Path);
            this.logger.LogInformation("The first item name : {Name}", result.FirstOrDefault().Name);
            return null;
            var song = result.Select(this.ConvertItemToAudio)
            .Where(song => this.SubstrMetric(song, tokenized_artist))
            .FirstOrDefault();
            if (song is null)
            {
                this.logger.LogInformation("even LibrarySearch failed...");
            }

            return song;
        }

        private bool SubstrMetric(Audio? song, string[] tokenized_artist)
        {
            if (song is null)
            {
                return false;
            }

            var contains = (string a) =>
            {
                return tokenized_artist.Any(token => a.Contains(token) || a.Contains(token));

                // this.logger.LogInformation("searchResult artist : {A} vs {Find}", a, artist);
            };
            var result = song.Artists.Any(contains);

            return result;
        }

        private Audio? GetMostMatchedSong(string title, string artist)
        {
            this.logger.LogInformation("Querying with {Query}...", title);
            var tokenized_artist = artist.Split(['(', ' ', ')']);
            var hints = this.searchEngine.GetSearchHints(new SearchQuery()
            {
                MediaTypes =[MediaType.Audio],
                SearchTerm = title,
            });

            var song = hints.Items
            .Select(this.ConvertSearchHintInfoToAudio)
            .Where(song => this.SubstrMetric(song, tokenized_artist))
            /*song =>
            song is not null &&
            song.Artists.Any(song_artist => song_artist.Contains(artist) || artist.Contains(song_artist))*/
            .FirstOrDefault();
            if (song is null)
            {
                this.logger.LogInformation("Query failed with artist {Artist}", artist);
            }

            return song;
        }

        private async Task<bool> DownloadMusicFromSource(string source, string title, string artist)
        {
            if (source.StartsWith("https"))
            {
                return false;
            }

            var interpolated = source.Replace("${title}", "\"" + title + "\"")
                                     .Replace("${artist}", "\"" + artist + "\"");
            var cmd = interpolated.Split(' ', 2);

            var result = await Cli.Wrap(cmd[0])
            .WithArguments(cmd[1])
            .ExecuteBufferedAsync()
            .ConfigureAwait(false);
            return result.IsSuccess;
        }

        private async Task<Audio?> DownloadMusic(string title, string artist)
        {
            var methods_to_download = Plugin.Instance.Configuration.ApisDownload;
            foreach (var source in methods_to_download)
            {
                try
                {
                    var success = await this.DownloadMusicFromSource(source, title, artist).ConfigureAwait(false);
                    if (success)
                    {
                        return this.GetMostMatchedSong(title, artist);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    this.logger.LogInformation("download from {Source} failed  {Msg}", source, e.Message);
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            this.logger.LogInformation("ExecuteAsync");
            cancellationToken.ThrowIfCancellationRequested();

            var commands_to_fetch = Plugin.Instance.Configuration.CommandsToFetch;

            this.logger.LogInformation("commands_to_fetch size : {size}", commands_to_fetch.Count);
            commands_to_fetch.ForEach((command) => this.logger.LogInformation("each command {Command}", command));

            commands_to_fetch.ForEach(async command => await this.CreatePlaylistFromFetchCommand(this.firstAdminId, command).ConfigureAwait(false));
        }
    }
}
