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
/// Opens one "aws ssm start-session" port-forward per server, but only while a tool needs it (on demand).
/// A connection stays open while a tool is attached to its local port; the idle countdown starts after the last tool
/// closes. A server that fails 3 times in a row stays down until <see cref="ResetAll"/> (the person signed in again).
/// Hosted by the tray / dashboard app. Writes live state to status.json for other windows.
/// </summary>
public sealed class TunnelManager : IDisposable
{
    public const int MaxTries = 3;

    sealed class Slot
    {
        public required TunnelConfig Tunnel;
        public Process? Proc;
        public DateTime NextStart = DateTime.MinValue;
        public DateTime StartedAt;
        public string State = TunnelStates.Ready;
        public string Message = "Ready. Click a tool to connect.";
        public DateTime Since = DateTime.UtcNow;
        public string LastError = "";
        /// <summary>Consecutive tries that ended without a working connection. Three in a row and the server stays down.</summary>
        public int Failures;
        /// <summary>True while somebody asked for this connection (clicked a tool). False = nothing should be open.</summary>
        public bool Wanted;
        /// <summary>Last moment a tool was attached to the local port (or the connection was opened).</summary>
        public DateTime LastActive = DateTime.UtcNow;
        public int Clients;
        public int IdleLeft = -1;
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

    // ---- on-demand control ----------------------------------------------------------------------
    /// <summary>Asks for this server's connection to be opened now. Does nothing for a server that stopped after 3 tries.</summary>
    public void Open(TunnelConfig t)
    {
        lock (_lock)
        {
            var key = KeyOf(t);
            if (!_slots.TryGetValue(key, out var slot)) _slots[key] = slot = new Slot { Tunnel = t };
            if (slot.State == TunnelStates.Stopped) return;
            slot.LastActive = DateTime.UtcNow;
            if (slot.Wanted) return;
            slot.Wanted = true;
            slot.Failures = 0;
            slot.NextStart = DateTime.UtcNow;
            SetState(slot, TunnelStates.Starting, "Opening secure session to the server...");
        }
        Poke();
    }

    /// <summary>Closes this server's connection and shows it as Ready again.</summary>
    public void Close(TunnelConfig t, string message = "Ready. Click a tool to connect.")
    {
        lock (_lock)
        {
            if (_slots.TryGetValue(KeyOf(t), out var slot) && slot.State != TunnelStates.Stopped) CloseSlot(slot, message);
        }
        Poke();
    }

    /// <summary>Closes every open connection (everything goes back to Ready).</summary>
    public void CloseAll()
    {
        lock (_lock)
        {
            foreach (var slot in _slots.Values) if (slot.State != TunnelStates.Stopped) CloseSlot(slot, "Ready. Click a tool to connect.");
        }
        Poke();
    }

    /// <summary>After a fresh sign-in: servers that gave up get another 3 tries (they show Ready again).</summary>
    public void ResetAll()
    {
        lock (_lock)
        {
            foreach (var slot in _slots.Values)
            {
                slot.Failures = 0;
                slot.LastError = "";
                if (slot.State == TunnelStates.Stopped) CloseSlot(slot, "Ready. Click a tool to connect.");
            }
        }
        Poke();
    }

    /// <summary>The live state of one server's connection (Ready when the manager has not seen it yet).</summary>
    public string StateOf(TunnelConfig t, out string message)
    {
        lock (_lock)
        {
            if (_slots.TryGetValue(KeyOf(t), out var slot)) { message = slot.Message; return slot.State; }
        }
        message = "";
        return TunnelStates.Ready;
    }

    /// <summary>True when the connection is open and its local port answers.</summary>
    public bool IsOpen(TunnelConfig t) => StateOf(t, out _) == TunnelStates.Connected && IsListening(t.LocalPort);

    void CloseSlot(Slot slot, string message)
    {
        slot.Wanted = false;
        slot.Failures = 0;
        slot.LastError = "";
        slot.Clients = 0;
        slot.IdleLeft = -1;
        StopSlot(slot);
        SetState(slot, TunnelStates.Ready, message);
    }

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

    static string KeyOf(TunnelConfig t) =>
        $"{t.InstanceId}|{t.RemotePort}|{t.LocalPort}|{t.Profile}|{t.Region}";

    /// <summary>Number of tools attached to a local port (established connections accepted on it).</summary>
    static int CountClients(int port)
    {
        try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Count(c => c.LocalEndPoint.Port == port && c.State == TcpState.Established); }
        catch { return 0; }
    }

    void Reconcile(AppConfig cfg)
    {
        _invalid.Clear();
        var idleSeconds = Math.Clamp(cfg.IdleMinutes <= 0 ? 15 : cfg.IdleMinutes, 5, 240) * 60;
        var globalProblem = ConfigValidator.ValidateGlobal(cfg);
        var desired = new Dictionary<string, TunnelConfig>();
        foreach (var t in cfg.Tunnels.Where(t => t.Enabled))
        {
            var problem = globalProblem ?? ConfigValidator.ValidateTunnel(t, cfg.Tunnels);
            if (problem != null) { _invalid[t] = problem; continue; }
            desired[KeyOf(t)] = t;
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

            // nothing asked for this server: nothing may stay open
            if (!slot.Wanted)
            {
                if (slot.Proc != null) StopSlot(slot);
                continue;
            }

            if (slot.Proc is { HasExited: false })
            {
                // Safety first: a tunnel must only ever listen on this PC (127.0.0.1 / ::1). If anything else shows up, close it.
                if (IsExposed(tunnel.LocalPort))
                {
                    AppLog.Error($"Tunnel {tunnel.Name}: port {tunnel.LocalPort} is open to the network. Closing the session.");
                    StopSlot(slot);
                    slot.Wanted = false;
                    SetState(slot, TunnelStates.Stopped, $"Stopped for safety: port {tunnel.LocalPort} was reachable from the network, not only from this PC. Click Sign in again to retry.");
                    continue;
                }

                // process alive and the local port is listening => the tunnel is up
                if (slot.State == TunnelStates.Starting && IsListening(tunnel.LocalPort))
                {
                    slot.Failures = 0;
                    slot.LastActive = DateTime.UtcNow;
                    SetState(slot, TunnelStates.Connected, $"Connected. Local port {tunnel.LocalPort} is ready.");
                }
                else if (slot.State == TunnelStates.Starting && DateTime.UtcNow - slot.StartedAt > TimeSpan.FromSeconds(45))
                {
                    var why = string.IsNullOrEmpty(slot.LastError) ? "It did not connect within 45 seconds. Check the server is online in SSM and you are signed in to AWS." : slot.LastError;
                    StopSlot(slot);
                    Fail(slot, why);
                    continue;
                }

                if (slot.State == TunnelStates.Connected)
                {
                    // keep the connection while a tool is attached or data moves; count down only when nobody uses it
                    slot.Clients = CountClients(tunnel.LocalPort);
                    if (slot.Clients > 0)
                    {
                        slot.LastActive = DateTime.UtcNow;
                        slot.IdleLeft = -1;
                    }
                    else
                    {
                        var left = idleSeconds - (int)(DateTime.UtcNow - slot.LastActive).TotalSeconds;
                        slot.IdleLeft = Math.Max(0, left);
                        if (left <= 0)
                        {
                            AppLog.Info($"Tunnel {tunnel.Name}: closed after {idleSeconds / 60} idle minutes.");
                            CloseSlot(slot, $"Closed after {idleSeconds / 60} minutes without use. Ready when you need it.");
                        }
                    }
                }
                continue;
            }

            if (slot.Proc != null)
            {
                // The session ended.
                var code = SafeExitCode(slot.Proc);
                var ranFor = DateTime.UtcNow - slot.StartedAt;
                var wasUp = slot.State == TunnelStates.Connected;
                slot.Proc.Dispose();
                slot.Proc = null;

                if (!string.IsNullOrEmpty(slot.LastError))
                {
                    AppLog.Warn($"Tunnel {slot.Tunnel.Name} exited with code {code}: {slot.LastError}");
                    Fail(slot, slot.LastError);
                    continue;
                }
                if (wasUp)
                {
                    // AWS closed a healthy session (its own idle timeout) or the tool closed it
                    if (DateTime.UtcNow - slot.LastActive < TimeSpan.FromSeconds(30) && ranFor > TimeSpan.FromSeconds(20))
                    {
                        // it was in use a moment ago: open the next session quietly
                        slot.NextStart = DateTime.UtcNow;
                        SetState(slot, TunnelStates.Starting, "Reconnecting...");
                        AppLog.Info($"Tunnel {slot.Tunnel.Name}: session ended after {(int)ranFor.TotalMinutes} min (code {code}); renewing it.");
                    }
                    else
                    {
                        AppLog.Info($"Tunnel {slot.Tunnel.Name}: session ended while idle (code {code}).");
                        CloseSlot(slot, "The session ended while idle. Ready when you need it.");
                        continue;
                    }
                }
                else
                {
                    AppLog.Warn($"Tunnel {slot.Tunnel.Name} ended after {(int)ranFor.TotalSeconds}s with code {code}.");
                    Fail(slot, $"The session closed straight away (code {code}). Check the server is online in SSM and you are signed in to AWS.");
                    continue;
                }
            }

            if (DateTime.UtcNow >= slot.NextStart) StartSession(slot);
        }
    }

    /// <summary>One more failed try. After <see cref="MaxTries"/> the server stays down until the person signs in again.</summary>
    void Fail(Slot slot, string why)
    {
        slot.Failures++;
        slot.LastError = "";
        if (slot.Failures >= MaxTries)
        {
            slot.Wanted = false;
            StopSlot(slot);
            AppLog.Warn($"Tunnel {slot.Tunnel.Name}: stopped after {MaxTries} tries. {why}");
            SetState(slot, TunnelStates.Stopped, $"Stopped after {MaxTries} tries. {why} Click Sign in again to retry.");
            return;
        }
        slot.NextStart = DateTime.UtcNow.AddSeconds(2 * slot.Failures);
        SetState(slot, TunnelStates.Retrying, $"Try {slot.Failures} of {MaxTries} did not work. {why} Trying again...");
    }

    static int SafeExitCode(Process p) { try { return p.ExitCode; } catch { return -1; } }

    static bool IsListening(int port)
    {
        try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(l => l.Port == port); }
        catch { return false; }
    }

    /// <summary>True when something listens on the port on an address other than this PC's own loopback.</summary>
    static bool IsExposed(int port)
    {
        try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(l => l.Port == port && !System.Net.IPAddress.IsLoopback(l.Address)); }
        catch { return false; }
    }

    void StartSession(Slot slot)
    {
        var t = slot.Tunnel;
        slot.LastError = "";

        if (AwsCli.FindAws() == null)
        {
            Fail(slot, "The AWS CLI is not installed. Click Add / change servers in the dashboard to install it.");
            return;
        }
        if (AwsCli.FindPlugin() == null)
        {
            Fail(slot, "The Session Manager plugin is not installed. Click Add / change servers in the dashboard to install it.");
            return;
        }

        // Never start on a port another program already owns: that program could answer in place of the server and the
        // dashboard would wrongly show "connected" (and SSH / RDP would send the login to it).
        if (IsListening(t.LocalPort))
        {
            Fail(slot, AwsCli.FriendlyError("address already in use", t.LocalPort));
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
            if (slot.State != TunnelStates.Starting) SetState(slot, TunnelStates.Starting, "Opening secure session to the server...");
            AppLog.Info($"Started tunnel {t.Name}: 127.0.0.1:{t.LocalPort} -> {t.InstanceId}:{t.RemotePort} (try {slot.Failures + 1} of {MaxTries})");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not start tunnel {t.Name}: {ex.Message}");
            Fail(slot, AwsCli.FriendlyError(ex.Message, t.LocalPort));
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
                slot.Failures = 0;
                slot.LastActive = DateTime.UtcNow;
                if (slot.Wanted) SetState(slot, TunnelStates.Connected, $"Connected. Local port {port} is ready.");
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
                var key = KeyOf(t);
                if (_slots.TryGetValue(key, out var s))
                    file.Tunnels.Add(new TunnelStatus
                    {
                        Name = t.Name, InstanceId = t.InstanceId, LocalPort = t.LocalPort, State = s.State, Message = s.Message, SinceUtc = s.Since,
                        Clients = s.Clients, IdleLeftSeconds = s.State == TunnelStates.Connected ? s.IdleLeft : -1,
                        Attempt = s.State is TunnelStates.Starting or TunnelStates.Retrying ? Math.Min(s.Failures + 1, MaxTries) : 0,
                    });
                else if (_invalid.TryGetValue(t, out var why))
                    file.Tunnels.Add(new TunnelStatus { Name = t.Name, InstanceId = t.InstanceId, LocalPort = t.LocalPort, State = TunnelStates.Error, Message = why, SinceUtc = DateTime.UtcNow });
                else if (t.Enabled)
                    file.Tunnels.Add(new TunnelStatus { Name = t.Name, InstanceId = t.InstanceId, LocalPort = t.LocalPort, State = TunnelStates.Ready, Message = "Ready. Click a tool to connect.", SinceUtc = DateTime.UtcNow });
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
