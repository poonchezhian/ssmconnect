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


using System.Text.RegularExpressions;

namespace SSMConnect.Core;

/// <summary>
/// Works out what kind of server something is (Production, Staging, ...) from its AWS tags first, because the name is only a guess.
/// The name is used only when no environment-style tag exists.
/// </summary>
public static class EnvironmentClassifier
{
    static readonly string[] KeyPriority = { "environment", "env", "stage", "tier", "appenv", "app-env", "deployment", "lifecycle", "landscape", "purpose" };

    /// <summary>Picks the environment tag (case-insensitive, also names like "app:environment") from an instance's tags. Null when there is none.</summary>
    public static string? FromTags(IEnumerable<KeyValuePair<string, string>> tags)
    {
        var list = tags.Where(t => !string.IsNullOrWhiteSpace(t.Value) && !t.Key.StartsWith("aws:", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var want in KeyPriority)
        {
            var hit = list.FirstOrDefault(t => t.Key.Trim().Equals(want, StringComparison.OrdinalIgnoreCase));
            if (hit.Key != null) return Normalise(hit.Value);
        }
        var loose = list.FirstOrDefault(t =>
        {
            var k = t.Key.Trim().ToLowerInvariant();
            return k.EndsWith(":environment") || k.EndsWith("-environment") || k.EndsWith("_environment") || k.EndsWith(":env") || k.EndsWith("-env") || k.EndsWith("_env");
        });
        return loose.Key != null ? Normalise(loose.Value) : null;
    }

    /// <summary>The environment for a server: its tag when it has one, otherwise a careful guess from the name, otherwise empty.</summary>
    public static string Classify(IEnumerable<KeyValuePair<string, string>> tags, string name) =>
        FromTags(tags) ?? GuessFromName(name) ?? "";

    /// <summary>True only for real production servers. "non-prod", "pre-prod" and names that merely contain "prod" (like "product-api") are not.</summary>
    public static bool IsProd(string? label) => Normalise(label ?? "") == "Production";

    static string[] Tokens(string s) => Regex.Split(s.ToLowerInvariant(), @"[^a-z0-9]+").Where(t => t.Length > 0).ToArray();

    /// <summary>Maps a free-text tag value such as "prod", "PRD", "non-production" or "uat2" to a short standard label.</summary>
    public static string Normalise(string value)
    {
        var v = value.Trim();
        if (v.Length == 0) return "";
        var t = Tokens(v);
        var joined = string.Concat(t);
        bool Has(params string[] x) => t.Any(k => x.Contains(k)) || x.Contains(joined);

        if (Has("nonprod", "nonproduction", "preprod", "preproduction") || (t.Contains("non") && Has("prod", "production")) || (t.Contains("pre") && Has("prod", "production")))
            return "Pre-production";
        if (Has("prod", "production", "prd", "live")) return "Production";
        if (Has("uat")) return "UAT";
        if (Has("stg", "stage", "staging")) return "Staging";
        if (Has("qa", "test", "testing", "tst", "sit")) return "Test";
        if (Has("dev", "development")) return "Development";
        if (Has("dr", "disasterrecovery")) return "DR";
        if (Has("sandbox", "sbx", "demo", "poc")) return "Sandbox";
        if (Has("internal")) return "Internal";
        return v.Length > 24 ? v.Substring(0, 24) : char.ToUpperInvariant(v[0]) + v.Substring(1);
    }

    /// <summary>Last resort when a server has no environment tag: whole words in its name.</summary>
    static string? GuessFromName(string name)
    {
        var t = Tokens(name);
        if (t.Length == 0) return null;
        if (t.Contains("nonprod") || t.Contains("preprod")) return "Pre-production";
        if (t.Contains("prod") || t.Contains("production") || t.Contains("prd")) return "Production";
        if (t.Contains("uat")) return "UAT";
        if (t.Contains("staging") || t.Contains("stg")) return "Staging";
        if (t.Contains("qa") || t.Contains("test")) return "Test";
        if (t.Contains("dev") || t.Contains("development")) return "Development";
        return null;
    }
}
