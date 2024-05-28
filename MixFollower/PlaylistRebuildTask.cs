// <copyright file="PlaylistRebuildTask.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MixFollower
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Emby.Naming.Common;
    using Jellyfin.Data.Enums;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Playlists;
    using MediaBrowser.Model.Globalization;
    using MediaBrowser.Model.Playlists;
    using MediaBrowser.Model.Tasks;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Deletes old log files.
    /// </summary>
    public class PlaylistRebuildTask : IScheduledTask, IConfigurableScheduledTask
    {
        private readonly ILibraryManager libraryManager;
        private readonly IPlaylistManager playlistManager;
        private readonly IUserManager userManager;

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
        public PlaylistRebuildTask(ILibraryManager libraryManager, IUserManager userManager, IPlaylistManager playlistManager, ILogger<PlaylistRebuildTask> logger, ILocalizationManager localization)
        {
            this.userManager = userManager;
            this.playlistManager = playlistManager;
            this.libraryManager = libraryManager;
            this.logger = logger;
            this.localization = localization;
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

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            this.logger.LogInformation("ExecuteAsync");

            var apis_download = Plugin.Instance.Configuration.apis_download;
            var commands_to_fetch = Plugin.Instance.Configuration.commands_to_fetch;

            const string PLAYLIST_NAME = "TOAST";
            var firstAdminId = this.userManager.Users
                .First(i => i.HasPermission(PermissionKind.IsAdministrator)).Id;
            var playlists = this.playlistManager.GetPlaylists(firstAdminId);
            var item_ids = Array.Empty<Guid>();
            foreach (var playlist in playlists)
            {
                this.logger.LogInformation("retrieve playlist : {Retri}", playlist.Name);
                if (playlist.Name == PLAYLIST_NAME)
                {
                    this.libraryManager.DeleteItem(playlist, new DeleteOptions { DeleteFileLocation = true }, true);
                    this.logger.LogInformation("matched and deleted ");
                }
            }

            var result = await this.playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = PLAYLIST_NAME,
                ItemIdList = item_ids,
                UserId = firstAdminId,
                MediaType = Data.Enums.MediaType.Audio,

                // Users = [],
                Public = true,
            }).ConfigureAwait(false);
            this.logger.LogInformation("playlist created {Id}", result.Id);
        }
    }
}
