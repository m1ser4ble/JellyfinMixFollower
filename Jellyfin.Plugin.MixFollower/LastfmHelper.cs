// <copyright file="LastfmHelper.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MixFollower
{
    using System;
    using System.Globalization;
    using System.Reflection;
    using System.Reflection.Metadata.Ecma335;
    using System.Security.Principal;
    using System.Threading;
    using System.Threading.Tasks;
    using Jellyfin.Data.Entities;
    using MediaBrowser.Common.Plugins;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Model.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Microsoft.VisualBasic;
    using Newtonsoft.Json.Linq;



    /// <summary>
    /// Deletes old log files.
    /// </summary>
    public class LastfmHelper : IScheduledTask, IConfigurableScheduledTask
    {
        private readonly IPluginManager pluginManager;
        private readonly ILogger<LastfmHelper> logger;
        private readonly Guid LASTFM_GUID;
        private readonly IUserManager userManager;
        private readonly PlaylistHelper playlistHelper;


        /// <summary>
        /// Initializes a new instance of the <see cref="LastfmHelper" /> class.
        /// </summary>
        /// <param name="userManager">The user manager.</param>
        /// <param name="libraryManager">The configuration manager.</param>
        /// <param name="playlistManager">The playlist manager.</param>
        /// <param name="logger">The  logger.</param>
        /// <param name="localization">The localization manager.</param>
        public LastfmHelper(IUserManager userManager, IPluginManager pluginManager, PlaylistHelper playlistHelper, ILogger<LastfmHelper> logger)
        {
            this.pluginManager = pluginManager;
            this.userManager = userManager;
            this.logger = logger;
            this.playlistHelper = playlistHelper;
            this.LASTFM_GUID = new Guid("de7fe7f0-b048-439e-a431-b1a7e99c930d");

            this.logger.LogInformation("LastfmHelper constructed");
        }

        /// <inheritdoc />
        public string Name => "LastfmHelper";

        /// <inheritdoc />
        public string Description => string.Format(
            CultureInfo.InvariantCulture,
            "LastfmHelperDescription");

        /// <inheritdoc />
        public string Category => "TasksMaintenanceCategory";

        /// <inheritdoc />
        public string Key => "LastfmHelper";

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





        public bool IsLastfmPluginInstalled()
        {

            var plugin = pluginManager.GetPlugin(this.LASTFM_GUID);
            return plugin is not null;
        }

        public string? GetLastfmUsernameFromUser(User? user)
        {
            if (user is null)
            {
                logger.LogInformation("user is not specified");
                return null;
            }
            if (!IsLastfmPluginInstalled())
            {
                return null;
            }

            var plugin = pluginManager.GetPlugin(this.LASTFM_GUID);
            if (plugin is null)
            {
                logger.LogInformation("user {Username} does not link with lastfm", user.Username);
                return null;
            }
            var dll = plugin.DllFiles.FirstOrDefault();
            var direct_assembly = Assembly.LoadFile(dll);
            direct_assembly.GetTypes().ToList().ForEach(type => logger.LogInformation("direct have types : {Types}", type.FullName));

            var assembly = plugin.GetType().Assembly;
            assembly.GetType("MediaBrowser.Common.Plugins.IPluginAssembly").Assembly.GetTypes().ToList()
            .ForEach(type => logger.LogInformation("possbile  : {T}", type.FullName));

            if (assembly is null)
            {
                logger.LogInformation("assembly is null...");
            }

            var type = direct_assembly.GetType("Jellyfin.Plugin.Lastfm.Utils.UserHelpers");
            if (type is null)
            {
                logger.LogInformation("type is null.....bb");
            }
            var methods = direct_assembly.GetType("Jellyfin.Plugin.Lastfm.Utils.UserHelpers").GetMethods();
            methods.ToList().ForEach(method => this.logger.LogInformation("methodname : {Name}", method.Name));

            ///
            var getUser = direct_assembly.GetType("Jellyfin.Plugin.Lastfm.Utils.UserHelpers").GetMethod("GetUser", BindingFlags.Public | BindingFlags.Instance);
            if (getUser is null)
            {
                logger.LogInformation("getUser is not null");
            }
            var x = getUser.Invoke(null, [user]);
            if (x is null)
            {
                logger.LogInformation("getUser result is null");
            }
            x.GetType().GetProperties().ToList().ForEach(property => logger.LogInformation("property : {P}", property.Name));
            if (x.GetType().GetProperty("Username").GetValue(x) is null)
            {
                logger.LogInformation("cant get value ");
            }
            string? username = x.GetType().GetProperty("Username").GetValue(x).ToString();
            this.logger.LogInformation("to {aa} , linked lastfm username is {Username}", user.Username, username);
            return username;

        }

        JObject ConvertLastfmSongToMixFollowerEntry(JObject lastfm_song)
        {
            var mixfollower_entry = new JObject();
            var name = lastfm_song.GetValue("_name").ToString();
            var artists = lastfm_song.GetValue("artists");
            var artists_name_list = artists.Children<JObject>()
            .Select(artist => artist.GetValue("_name").ToString())
            .ToList();
            var artist = string.Join(" ", artists_name_list);
            mixfollower_entry.Add("name", name);
            mixfollower_entry.Add("artist", artist);
            return mixfollower_entry;

        }

        async Task<JObject> GetRecommendedMixFromLastfmUser(string username)
        {
            ///https://www.last.fm/player/station/user/{username}/recommended
            var url = $"https://www.last.fm/player/station/user/{username}/recommended";
            HttpClient client = new HttpClient();
            var response = await client.GetAsync(url).ConfigureAwait(false);
            this.logger.LogInformation("returned msg : \n {Msg} ", response);
            var data = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var mixfollower_formatted_data = new JObject();
            var lastfm_jobject = JObject.Parse(data);
            mixfollower_formatted_data.Add("name", "Recommended by Lastfm");
            var songlist = new JArray();

            var list = lastfm_jobject.GetValue("playlist");
            list.Children<JObject>()
            .Select(jobject => ConvertLastfmSongToMixFollowerEntry(jobject))
            .ToList()
            .ForEach(mixfollower_entry => songlist.Add(mixfollower_entry));

            mixfollower_formatted_data.Add("chart", songlist);
            return mixfollower_formatted_data;

        }

        /// <summary>
        /// Creates the triggers that define when the task will run.
        /// </summary>
        /// <returns>IEnumerable{BaseTaskTrigger}.</returns>




        private async Task CreatePlaylistsFromLastfm()
        {
            this.userManager.Users.Select(user => new { MediaBrowserUser = user, LastfmUser = GetLastfmUsernameFromUser(user) })
            .Where(usermap => usermap.LastfmUser is not null)
            .ToList()
            .ForEach(async usermap =>
            {
                var mix = await GetRecommendedMixFromLastfmUser(usermap.LastfmUser).ConfigureAwait(false);
                await playlistHelper.CreateUserPlaylist(mix, usermap.MediaBrowserUser, false).ConfigureAwait(false);
            });


        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (IsLastfmPluginInstalled())
            {
                await CreatePlaylistsFromLastfm().ConfigureAwait(false);

            }

        }
    }
}
