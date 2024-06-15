// <copyright file="MetaDb.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.MixFollower;

public class MetaDb
{
    private ILibraryManager libraryManager;
    private IReadOnlyList<BaseItem> db;

    public MetaDb(ILibraryManager libraryManager)
    {
        this.libraryManager = libraryManager;
    }

    public void RecreateDb()
    {
        var query = new InternalItemsQuery()
        {
            MediaTypes =[MediaType.Audio],
        };
        var result = this.libraryManager.QueryItems(query);
        this.db = result.Items;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetaDb"/> class.
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public IEnumerable<BaseItem> SearchByPath(string path)
    {
        return this.db.Where(song => song.Path.Contains(path, StringComparison.OrdinalIgnoreCase));
    }
}
