using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NeuralFX.Hub.Services;

internal static class SteamLibraryLocator
{
    internal static IEnumerable<(string Key, string Value)> Pairs(string text)
    {
        foreach (Match match in Regex.Matches(text, "\"(?<key>(?:\\\\.|[^\"\\\\])*)\"\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\""))
            yield return (Unescape(match.Groups["key"].Value), Unescape(match.Groups["value"].Value));
    }
    private static string Unescape(string value) => value.Replace("\\\\", "\\").Replace("\\\"", "\"");
    internal static IEnumerable<string> Libraries(string steamRoot)
    {
        yield return steamRoot;
        string path = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(path)) yield break;
        foreach (var pair in Pairs(File.ReadAllText(path)))
            if ((pair.Key == "path" || int.TryParse(pair.Key, out _)) && Path.IsPathFullyQualified(pair.Value)) yield return pair.Value;
    }
    internal static string? FindInLibraries(IEnumerable<string> libraries)
    {
        foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string app = Path.Combine(library, "steamapps", "appmanifest_255710.acf");
            if (!File.Exists(app)) continue;
            string? folder = Pairs(File.ReadAllText(app)).FirstOrDefault(x => x.Key == "installdir").Value;
            if (string.IsNullOrWhiteSpace(folder) || folder != Path.GetFileName(folder) || folder is "." or "..") continue;
            string candidate = Path.Combine(library, "steamapps", "common", folder, "Cities.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
    public static string? Find()
    {
        using var user = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        using var machine = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
        string root = user?.GetValue("SteamPath") as string ?? machine?.GetValue("InstallPath") as string ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return FindInLibraries(Libraries(root));
    }
}
