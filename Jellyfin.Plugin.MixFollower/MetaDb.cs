// <copyright file="MetaDb.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.MixFollower;

public class MetaDb
{
    private readonly ILibraryManager libraryManager;
    private IReadOnlyList<BaseItem> db;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetaDb"/> class.
    /// </summary>
    /// <param name="libraryManager">library manager from jellyfin.</param>
    public MetaDb(ILibraryManager libraryManager)
    {
        this.libraryManager = libraryManager;
        this.db = new List<BaseItem>();
    }

    /// <summary>
    /// Recreate _db based on the library state when called.
    /// </summary>
    public void RecreateDb()
    {
        var query = new InternalItemsQuery()
        {
            MediaTypes = [MediaType.Audio],
        };
        var result = this.libraryManager.QueryItems(query);
        this.db = result.Items;
    }

    /// <summary>
    /// Search songs containing a specific string in its path.
    /// </summary>
    /// <param name="path">path name in the system.</param>
    /// <returns>list of songs.</returns>
    public IEnumerable<BaseItem> SearchByPath(string path)
    {
        return this.db.Where(song => song is not null && song.Path.Contains(path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Search songs containing a specific string in its filename.
    /// </summary>
    /// <param name="filename">wanted file name.</param>
    /// <returns>list of songs. </returns>
    public IEnumerable<BaseItem> SearchByFilename(string filename)
    {
        return this.db
        .Where(song =>
            song is not null && song.FileNameWithoutExtension.Contains(filename, StringComparison.OrdinalIgnoreCase));
    }
}
