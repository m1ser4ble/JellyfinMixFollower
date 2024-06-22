// <copyright file="PluginConfiguration.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MixFollower.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        // set default options here
        this.ApisDownload = new List<string>();
        this.CommandsToFetch =[];
    }

    /// <summary>
    /// Gets or sets a value indicating whether some true or false setting is enabled..
    /// </summary>
    public bool TrueFalseSetting { get; set; }

    private List<string> apiDownload;

    /// <summary>
    /// Gets or sets api for downloading missing songs in my library.
    /// </summary>
    public List<string> ApisDownload
    {
        get => this.apiDownload;
        set
        {
            this.apiDownload = value.Where((source) => !string.IsNullOrEmpty(source)).ToList();
        }
    }

    private List<string> commandsToFetch;

    /// <summary>
    /// Gets or sets linux commands to fetch
    /// e.g) python fetch_billboard.py.
    /// </summary>
    public required List<string> CommandsToFetch
    {
        get => this.commandsToFetch;
        set
        {
            this.commandsToFetch = value.Where((command) => !string.IsNullOrEmpty(command)).ToList();
        }
    }
}
