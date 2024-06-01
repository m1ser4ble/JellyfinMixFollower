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
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// Deletes old log files.
    /// </summary>
    public class PlaylistRebuildTask : IScheduledTask, IConfigurableScheduledTask
    {
        private readonly Guid firstAdminId;
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
            this.firstAdminId = this.userManager.Users
                .First(i => i.HasPermission(PermissionKind.IsAdministrator)).Id;
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
                new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerInterval, IntervalTicks = TimeSpan.FromMinutes(1).Ticks /*TimeSpan.FromHours(24).Ticks*/ },
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
                result = await Cli.Wrap(command).ExecuteBufferedAsync();

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
                        this.logger.LogInformation("song {Title} by {Artist} not found in library", title, artist);

                        item = await this.DownloadMusic(title, artist).ConfigureAwait(false);
                        if (item is null)
                        {
                            this.logger.LogInformation("tried download , but still not in library");
                            continue;
                        }
                    }

                    list_items.Add(item.Id);
                }

                this.DeletePlaylist(playlist_name);

                var playlist = await this.playlistManager.CreatePlaylist(new PlaylistCreationRequest
                {
                    Name = playlist_name,
                    ItemIdList = list_items,
                    UserId = user,
                    MediaType = Data.Enums.MediaType.Audio,

                    // Users = [],
                    Public = true,
                }).ConfigureAwait(false);

                this.logger.LogInformation("playlist created {Id}", playlist.Id);
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
            switch (item)
            {
                case Audio song:
                    return song;
                default:
                    return null;
            }
        }

        private Audio? GetMostMatchedSong(string title, string artist)
        {
            var hints = this.searchEngine.GetSearchHints(new SearchQuery()
            {
                MediaTypes =[MediaType.Audio],
                SearchTerm = title,
            });
            var lambda = (Audio? song) =>
            {
                if (song is null)
                {
                    return false;
                }

                var contains = (string a) =>
                {
                    this.logger.LogInformation("existing song artist : {A}", a);

                    return a.Contains(artist) || artist.Contains(a);
                };
                var result = song.Artists.Any(contains);
                this.logger.LogInformation("result : {R}", result);

                return result;
            };

            return hints.Items
            .Select(this.ConvertSearchHintInfoToAudio)
            .Where(lambda)
            /*song =>
            song is not null &&
            song.Artists.Any(song_artist => song_artist.Contains(artist) || artist.Contains(song_artist))*/
            .FirstOrDefault();
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
            .ExecuteBufferedAsync();
            this.logger.LogInformation("Cli output Msg\n {Msg}", result.StandardOutput);
            return result.IsSuccess;
        }

        private async Task<Audio?> DownloadMusic(string title, string artist)
        {
            var methods_to_download = Plugin.Instance.Configuration.ApisDownload;
            foreach (var source in methods_to_download)
            {
                this.logger.LogInformation("try to download from {Source}", source);
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

            this.logger.LogInformation("tried all wonload source but all failed.");
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
