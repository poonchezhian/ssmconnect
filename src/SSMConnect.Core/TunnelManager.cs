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

using System.Diagnostics;
using System.Net.NetworkInformation;

namespace SSMConnect.Core;

/// <summary>
/// Keeps one "aws ssm start-session" port-forward alive per configured server, as the signed-in user.
/// Hosted by the tray / dashboard app. Writes live state to status.json for other windows.
/// </summary>
public sealed class TunnelManager : IDisposable
{
    sealed class Slot
    {
        public required TunnelConfig Tunnel;
        public Process? Proc;
        public DateTime NextStart = DateTime.MinValue;
        public DateTime StartedAt;
        public int BackoffSeconds = 5;
        public string State = TunnelStates.Starting;
        public string Message = "Starting...";
        public DateTime Since = DateTime.UtcNow;
        public string LastError = "";
    }

    readonly object _lock = new();
    readonly Dictionary<string, Slot> _slots = new();
    readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    readonly Dictionary<TunnelConfig, string> _invalid = new();
    AppConfig? _cfg;

    CancellationTokenSource? _cts;
    Task? _loop;

    public bool IsRunning => _loop is { IsCompleted: false };

    /// <summary>Raised (on a background thread) whenever any tunnel changes state.</summary>
    public event Action? Changed;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loop = Task.Run(() => RunAsync(ct));
    }

    public void Stop()
    {
        var cts = _cts;
        if (cts == null) return;
        try { cts.Cancel(); } catch { }
        try { _loop?.Wait(TimeSpan.FromSeconds(10)); } catch { }
        cts.Dispose();
        _cts = null;
        _loop = null;
    }

    public void Dispose() => Stop();

    /// <summary>Wakes the loop now, for example after the config changed or the person signed in.</summary>
    public void Poke() { try { _wake.Release(); } catch (SemaphoreFullException) { } }

    async Task RunAsync(CancellationToken ct)
    {
        AppLog.Info("Tunnel manager started. Config: " + ConfigStore.ConfigPath);
        Directory.CreateDirectory(ConfigStore.DataDir);

        using var watcher = new FileSystemWatcher(ConfigStore.DataDir, "config.json") { EnableRaisingEvents = true };
        void OnFs(object? _, FileSystemEventArgs __) => Poke();
        watcher.Changed += OnFs;
        watcher.Created += OnFs;
        watcher.Renamed += (s, e) => Poke();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    lock (_lock)
                    {
                        _cfg = ConfigStore.Load() ?? _cfg; // keep the last good config if the file is mid-write
                        if (_cfg != null) Reconcile(_cfg);
                        WriteStatus();
                    }
                }
                catch (Exception ex) { AppLog.Error("Reconcile failed: " + ex); }

                await _wake.WaitAsync(TimeSpan.FromSeconds(3), ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_lock)
            {
                foreach (var s in _slots.Values) StopSlot(s);
                _slots.Clear();
                _cfg = null;
                StatusStore.Save(new StatusFile { UpdatedUtc = DateTime.UtcNow });
            }
            Changed?.Invoke();
            AppLog.Info("Tunnel manager stopped.");
        }
    }

    static string KeyOf(TunnelConfig t, AppConfig cfg) =>
        $"{t.InstanceId}|{t.RemotePort}|{t.LocalPort}|{t.Profile}|{t.Region}";

    void Reconcile(AppConfig cfg)
    {
        _invalid.Clear();
        var globalProblem = ConfigValidator.ValidateGlobal(cfg);
        var desired = new Dictionary<string, TunnelConfig>();
        foreach (var t in cfg.Tunnels.Where(t => t.Enabled))
        {
            var problem = globalProblem ?? ConfigValidator.ValidateTunnel(t, cfg.Tunnels);
            if (problem != null) { _invalid[t] = problem; continue; }
            desired[KeyOf(t, cfg)] = t;
        }

        foreach (var key in _slots.Keys.Where(k => !desired.ContainsKey(k)).ToList())
        {
            AppLog.Info($"Stopping tunnel {_slots[key].Tunnel.Name}");
            StopSlot(_slots[key]);
            _slots.Remove(key);
        }

        foreach (var (key, tunnel) in desired)
        {
            if (!_slots.TryGetValue(key, out var slot))
                _slots[key] = slot = new Slot { Tunnel = tunnel };

            if (slot.Proc is { HasExited: false })
            {
                // Fallback detection: process alive and the local port is listening => the tunnel is up.
                if (slot.State == TunnelStates.Starting && IsListening(tunnel.LocalPort))
                    SetState(slot, TunnelStates.Connected, $"Connected. Local port {tunnel.LocalPort} is ready.");
                else if (slot.State == TunnelStates.Starting && DateTime.UtcNow - slot.StartedAt > TimeSpan.FromSeconds(45))
                    SetState(slot, TunnelStates.Starting,
                        string.IsNullOrEmpty(slot.LastError)
                            ? "Still connecting... check the server is online in SSM and you are signed in to AWS."
                            : slot.LastError);
                continue;
            }

            if (slot.Proc != null)
            {
                // It ran and died. Healthy long runs reset the back-off; quick failures slow down.
                var code = SafeExitCode(slot.Proc);
                slot.BackoffSeconds = DateTime.UtcNow - slot.StartedAt > TimeSpan.FromMinutes(1)
                    ? 5 : Math.Min(slot.BackoffSeconds * 2, 60);
                slot.NextStart = DateTime.UtcNow.AddSeconds(slot.BackoffSeconds);
                slot.Proc.Dispose();
                slot.Proc = null;

                if (!string.IsNullOrEmpty(slot.LastError))
                    SetState(slot, TunnelStates.Error, $"{slot.LastError} Retrying in {slot.BackoffSeconds}s.");
                else
                    SetState(slot, TunnelStates.Retrying, $"Connection ended (code {code}). Reconnecting in {slot.BackoffSeconds}s.");
                AppLog.Warn($"Tunnel {slot.Tunnel.Name} exited with code {code}; retry in {slot.BackoffSeconds}s");
            }

            if (DateTime.UtcNow >= slot.NextStart) Start(slot, cfg);
        }
    }

    static int SafeExitCode(Process p) { try { return p.ExitCode; } catch { return -1; } }

    static bool IsListening(int port)
    {
        try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(l => l.Port == port); }
        catch { return false; }
    }

    void Start(Slot slot, AppConfig cfg)
    {
        var t = slot.Tunnel;
        slot.LastError = "";

        if (AwsCli.FindAws() == null)
        {
            SetState(slot, TunnelStates.Error, "AWS CLI is not installed. Use Tools > Install or check AWS tools in the dashboard.");
            slot.NextStart = DateTime.UtcNow.AddSeconds(10);
            return;
        }
        if (AwsCli.FindPlugin() == null)
        {
            SetState(slot, TunnelStates.Error, "The Session Manager plugin is not installed. Use Tools > Install or check AWS tools in the dashboard.");
            slot.NextStart = DateTime.UtcNow.AddSeconds(10);
            return;
        }

        try
        {
            var psi = AwsCli.BuildStartInfo(new AppConfig { Profile = t.Profile, Region = t.Region }, new[]
            {
                "ssm", "start-session",
                "--target", t.InstanceId,
                "--document-name", "AWS-StartPortForwardingSession",
                "--parameters", $"portNumber={t.RemotePort},localPortNumber={t.LocalPort}",
            });
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => OnLine(slot, e.Data, isError: false);
            p.ErrorDataReceived += (_, e) => OnLine(slot, e.Data, isError: true);
            p.Exited += (_, _) => Poke();
            p.Start();
            ChildProcessJob.Add(p);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            slot.Proc = p;
            slot.StartedAt = DateTime.UtcNow;
            SetState(slot, TunnelStates.Starting, "Opening secure session to the server...");
            AppLog.Info($"Started tunnel {t.Name}: 127.0.0.1:{t.LocalPort} -> {t.InstanceId}:{t.RemotePort}");
        }
        catch (Exception ex)
        {
            slot.BackoffSeconds = Math.Min(slot.BackoffSeconds * 2, 60);
            slot.NextStart = DateTime.UtcNow.AddSeconds(slot.BackoffSeconds);
            SetState(slot, TunnelStates.Error, AwsCli.FriendlyError(ex.Message, t.LocalPort));
            AppLog.Error($"Could not start tunnel {t.Name}: {ex.Message}");
        }
    }

    void OnLine(Slot slot, string? line, bool isError)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_lock)
        {
            var port = slot.Tunnel.LocalPort;
            if (line.Contains("Waiting for connections", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("opened for sessionId", StringComparison.OrdinalIgnoreCase))
            {
                slot.LastError = "";
                SetState(slot, TunnelStates.Connected, $"Connected. Local port {port} is ready.");
                AppLog.Info($"[{slot.Tunnel.Name}] {line}");
            }
            else if (isError || line.Contains("An error occurred", StringComparison.OrdinalIgnoreCase))
            {
                slot.LastError = AwsCli.FriendlyError(line, port);
                AppLog.Warn($"[{slot.Tunnel.Name}] {line}");
            }
            else
            {
                AppLog.Info($"[{slot.Tunnel.Name}] {line}");
            }
        }
    }

    /// <summary>Must be called with _lock held. Writes status.json straight away when something changed.</summary>
    void SetState(Slot slot, string state, string message)
    {
        if (slot.State == state && slot.Message == message) return;
        if (slot.State != state) slot.Since = DateTime.UtcNow;
        slot.State = state;
        slot.Message = message;
        WriteStatus();
    }

    void WriteStatus()
    {
        var file = new StatusFile { UpdatedUtc = DateTime.UtcNow };
        if (_cfg != null)
        {
            foreach (var t in _cfg.Tunnels)
            {
                var key = KeyOf(t, _cfg);
                if (_slots.TryGetValue(key, out var s))
                    file.Tunnels.Add(new TunnelStatus { Name = t.Name, InstanceId = t.InstanceId, LocalPort = t.LocalPort, State = s.State, Message = s.Message, SinceUtc = s.Since });
                else if (_invalid.TryGetValue(t, out var why))
                    file.Tunnels.Add(new TunnelStatus { Name = t.Name, InstanceId = t.InstanceId, LocalPort = t.LocalPort, State = TunnelStates.Error, Message = why, SinceUtc = DateTime.UtcNow });
                else
                    file.Tunnels.Add(new TunnelStatus { Name = t.Name, InstanceId = t.InstanceId, LocalPort = t.LocalPort, State = TunnelStates.Disabled, Message = "Turned off.", SinceUtc = DateTime.UtcNow });
            }
        }
        StatusStore.Save(file);
        Changed?.Invoke();
    }

    static void StopSlot(Slot slot)
    {
        try { if (slot.Proc is { HasExited: false }) slot.Proc.Kill(entireProcessTree: true); } catch { }
        slot.Proc?.Dispose();
        slot.Proc = null;
    }
}
