namespace Jellyfin.Plugin.MixFollower
{
    using CliWrap;
    using CliWrap.Buffered;
    using Jellyfin.Data.Entities;
    using Jellyfin.Data.Enums;
    using MediaBrowser.Controller.Entities.Audio;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Playlists;
    using MediaBrowser.Model.Playlists;
    using MediaBrowser.Model.Search;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json.Linq;

    public class PlaylistHelper
    {
        private readonly IPlaylistManager playlistManager;
        private readonly ILibraryManager libraryManager;
        private readonly ILogger<PlaylistHelper> logger;
        private readonly MetaDb db;
        private readonly ISearchEngine searchEngine;
        public PlaylistHelper(IPlaylistManager playlistManager, ISearchEngine searchEngine, ILibraryManager libraryManager, ILogger<PlaylistHelper> logger)
        {
            this.playlistManager = playlistManager;
            this.libraryManager = libraryManager;
            this.logger = logger;
            this.searchEngine = searchEngine;
            this.db = new MetaDb(this.libraryManager);

        }


        public async Task<PlaylistCreationResult> CreateUserPlaylist(JObject obj, User user, bool is_public)
        {


            var playlist_name = obj?.GetValue("name")?.ToString()!;

            var songs = obj?.GetValue("songs");
            this.db.RecreateDb();
            var list_items = new List<Guid>();
            songs?.Children<JObject>()
            .ToList()
            .ForEach(async jobject => await this.GetMostMatchedSongFromJObject(jobject, this.DownloadMusic).ConfigureAwait(false));

            // _iLibraryMonitor.ReportFileSystemChangeComplete(path, false);
            await this.libraryManager.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None).ConfigureAwait(false);
            this.DeletePlaylist(playlist_name, user);
            this.db.RecreateDb();

            songs?.Children<JObject>()
            .ToList()
            .ForEach(async jobject =>
            {
                var item = await this.GetMostMatchedSongFromJObject(jobject, null).ConfigureAwait(false);
                if (item is not null)
                {
                    list_items.Add(item.Id);
                }
            });

            var playlist = await this.playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = playlist_name,
                ItemIdList = list_items,
                UserId = user.Id,
                MediaType = Data.Enums.MediaType.Audio,

                // Users = [],
                Public = is_public,
            }).ConfigureAwait(false);
            return playlist;

        }
        private void DeletePlaylist(string playlist_name, User user)
        {
            var playlists = this.playlistManager.GetPlaylists(user.Id);
            var playlist = playlists.FirstOrDefault(playlist => playlist.Name == playlist_name);
            if (playlist is null)
            {
                return;
            }

            this.libraryManager.DeleteItem(playlist, new DeleteOptions { DeleteFileLocation = true }, true);
            this.logger.LogInformation("matched and deleted {PN}", playlist_name);
        }

        private async Task<Audio?> GetMostMatchedSongFromJObject(JObject jobject, Func<string, string, Task<Audio?>>? action)
        {
            var title = jobject?.GetValue("title")?.ToString()!;
            var artist = jobject?.GetValue("artist")?.ToString()!;

            var item = this.GetMostMatchedSong(title, artist);
            if (item is null && action is not null)
            {
                item = await action(title, artist).ConfigureAwait(false);
            }

            return item;
        }

        private bool SubstrMetric(Audio? song, string[] tokenized_artist)
        {
            if (song is null)
            {
                return false;
            }

            bool ContainsToken(string a)
            {
                return tokenized_artist.Any(token => a.Contains(token, StringComparison.InvariantCulture) || a.Contains(token, StringComparison.InvariantCulture));
            }

            var result = song.Artists.Any(ContainsToken);
            if (!result)
            {
                var join = string.Join(' ', tokenized_artist);
                this.logger.LogInformation("I want artist {Joined}", join);
                this.logger.LogInformation("song artists...");
                song.Artists.ToList().ForEach(a => this.logger.LogInformation("Artist : {A}", a));
            }

            return result;
        }

        private Audio? GetMostMatchedSong(string title, string artist)
        {
            this.logger.LogInformation("Querying with {Query}...", title);
            var tokenized_artist = artist.Split(['(', ' ', ')']);
            MediaType[] audioTypes = [MediaType.Audio];
            var hints = this.searchEngine.GetSearchHints(new SearchQuery()
            {
                MediaTypes = audioTypes,
                SearchTerm = title,
            });

            var song = hints.Items
            .Select(hint => hint.Item is Audio song ? song : null)
            .Where(song => this.SubstrMetric(song, tokenized_artist))
            .FirstOrDefault();

            if (song is null)
            {
                var result = this.db.SearchByFilename(title);
                if (result.Count() != 1)
                {
                    this.logger.LogInformation("# of query results : {Count} ({Title}, {Artist})", result.Count(), title, artist);
                }

                song = result.Select(item => item is Audio song ? song : null)

                // .Where(song => this.SubstrMetric(song, tokenized_artist)) // we have to solve beyonce problem
                .FirstOrDefault();
                if (song is null)
                {
                    this.logger.LogInformation("even LibrarySearch failed with artist {Artist}...", artist);
                }
            }

            return song;
        }

        private static async Task<bool> DownloadMusicFromSource(string source, string title, string artist)
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
                    var success = await DownloadMusicFromSource(source, title, artist).ConfigureAwait(false);
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



    }


}
