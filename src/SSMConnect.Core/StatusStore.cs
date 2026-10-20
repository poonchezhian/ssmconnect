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

/// <summary>Shared status file: the tunnel manager writes it, the dashboard and tray read it.</summary>
public static class StatusStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string StatusPath => Path.Combine(ConfigStore.DataDir, "status.json");

    public static void Save(StatusFile status)
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.DataDir);
            var temp = StatusPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(status, Options));
            File.Move(temp, StatusPath, overwrite: true);
        }
        catch (IOException) { /* a reader had it open; the next heartbeat rewrites it */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Returns null when the file is missing or mid-write.</summary>
    public static StatusFile? Load()
    {
        try
        {
            if (!File.Exists(StatusPath)) return null;
            using var fs = new FileStream(StatusPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<StatusFile>(fs, Options);
        }
        catch
        {
            return null;
        }
    }
}
