// <copyright file="PlaylistRebuildTask.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MixFollower
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;
    using CliWrap;
    using CliWrap.Buffered;
    using Jellyfin.Data.Entities;
    using Jellyfin.Data.Enums;
    using MediaBrowser.Common.Plugins;
    using MediaBrowser.Controller.Entities.Audio;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Playlists;
    using MediaBrowser.Model.Drawing;
    using MediaBrowser.Model.Globalization;
    using MediaBrowser.Model.Playlists;
    using MediaBrowser.Model.Search;
    using MediaBrowser.Model.Tasks;
    using Microsoft.AspNetCore.Mvc.Diagnostics;
    using Microsoft.Extensions.Logging;
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


        private readonly ILocalizationManager localization;
        private readonly ILogger<PlaylistRebuildTask> logger;

        private readonly PlaylistHelper playlistHelper;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlaylistRebuildTask" /> class.
        /// </summary>
        /// <param name="userManager">The user manager.</param>
        /// <param name="libraryManager">The configuration manager.</param>
        /// <param name="playlistManager">The playlist manager.</param>
        /// <param name="logger">The  logger.</param>
        /// <param name="localization">The localization manager.</param>
        public PlaylistRebuildTask(ILibraryManager libraryManager, PlaylistHelper playlistHelper, IUserManager userManager, IPlaylistManager playlistManager, ILogger<PlaylistRebuildTask> logger, ILocalizationManager localization)
        {
            this.userManager = userManager;
            this.playlistManager = playlistManager;
            this.libraryManager = libraryManager;
            this.logger = logger;
            this.localization = localization;
            this.playlistHelper = playlistHelper;
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

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            this.logger.LogInformation("ExecuteAsync");
            cancellationToken.ThrowIfCancellationRequested();

            var commands_to_fetch = Plugin.Instance.Configuration.CommandsToFetch;

            this.logger.LogInformation("commands_to_fetch size : {size}", commands_to_fetch.Count);

            foreach (var command in commands_to_fetch)
            {
                this.logger.LogInformation("command {Command} Executing", command);
                await this.CreatePlaylistFromFetchCommand(this.firstAdmin, command).ConfigureAwait(false);
            }


        }


        private async Task<string> CreatePlaylistFromFetchCommand(User user, string command)
        {
            this.logger.LogInformation("cli command executing {Command}", command);
            CliWrap.Buffered.BufferedCommandResult? result = null;
            try
            {
                result = await Cli.Wrap(command).ExecuteBufferedAsync().ConfigureAwait(false);

                // result.ToString();
                var json = result.StandardOutput.ToString();


                var obj = JObject.Parse(json);

                var playlist = await playlistHelper.CreateUserPlaylist(obj, user, true).ConfigureAwait(false);


                return playlist.Id;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                this.logger.LogInformation("executing {command} gets crash! {msg} ", command, exception.Message);
                this.logger.LogInformation("{stack_trace}", exception.StackTrace?.ToString());
            }

            return string.Empty;
        }





    }
}
