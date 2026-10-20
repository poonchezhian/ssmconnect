// Copyright 2026 Poonchezhian Periasamy <poonchezhian@gmail.com>
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.


using System.Text.Json;

namespace SSMConnect.Core;

/// <summary>
/// Per-user settings in %LOCALAPPDATA%\SSMConnect. Nothing is shared between Windows users and nothing
/// runs with elevated rights, so one user can never influence another user's tunnels.
/// </summary>
public static class ConfigStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SSMConnect");

    public static string ConfigPath => Path.Combine(DataDir, "config.json");

    /// <summary>Returns the saved config, an empty one if none exists yet, or null if the file is unreadable.</summary>
    public static AppConfig? Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new AppConfig();
            var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Options) ?? new AppConfig();
            // Settings saved by an older version had one account for everything: copy it onto each tunnel.
            foreach (var t in cfg.Tunnels)
            {
                if (string.IsNullOrWhiteSpace(t.Profile)) t.Profile = cfg.Profile;
                if (string.IsNullOrWhiteSpace(t.Region)) t.Region = cfg.Region;
            }
            return cfg;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Like <see cref="Load"/>, but a damaged file is copied to config.corrupt-*.json first so nothing is silently lost.
    /// <paramref name="warning"/> tells the person what happened (null when all is well).
    /// </summary>
    public static AppConfig LoadOrRecover(out string? warning)
    {
        warning = null;
        var cfg = Load();
        if (cfg != null) return cfg;
        try
        {
            var copy = Path.Combine(DataDir, $"config.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(ConfigPath, copy, overwrite: true);
            warning = "Your saved settings file could not be read, so SSMConnect is starting with an empty list. " +
                      "The damaged file was kept as " + copy + ".";
        }
        catch
        {
            warning = "Your saved settings file could not be read, so SSMConnect is starting with an empty list.";
        }
        AppLog.Warn(warning);
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(DataDir);
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Options));
        if (File.Exists(ConfigPath)) { try { File.Copy(ConfigPath, ConfigPath + ".bak", overwrite: true); } catch { } }
        File.Move(temp, ConfigPath, overwrite: true);
    }
}
