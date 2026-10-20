using System;
using System.IO;
using HackRfIqDataTest.Storage;
using Microsoft.Extensions.Configuration;

namespace HackRfIqDataTest;

/// <summary>
/// Settings read from appsettings.json next to the executable.
/// </summary>
public sealed record AppSettings(string RecordingsDirectory)
{
    public static AppSettings Load()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var recordingsDirectory = configuration["Recordings:Directory"];
        return new AppSettings(
            string.IsNullOrWhiteSpace(recordingsDirectory) ? RecordingLibrary.DefaultDirectory : ResolvePath(recordingsDirectory));
    }

    /// <summary>
    /// Expands a leading ~ to the home folder; relative paths are relative to the executable.
    /// </summary>
    private static string ResolvePath(string path)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal))
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];
        }

        return Path.GetFullPath(path, AppContext.BaseDirectory);
    }
}
