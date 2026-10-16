//! Keeps one `aws ssm start-session` port-forward alive per configured server, as the signed-in user.
//!
//! The manager runs on a background thread inside the app (or `ssmconnect run` on a terminal). It reacts to config changes,
//! renews sessions that AWS closed for being idle, backs off when something is really wrong, and publishes plain-language
//! state through `snapshot()`, the `on_change` callback and status.json.

use crate::models::{AppConfig, State, TunnelConfig, TunnelStatus};
use crate::util::{lock, now};
use crate::{applog, aws_cli, config_store, netcheck, status_store, validator};
use std::collections::HashMap;
use std::io::{BufRead, BufReader};
use std::os::unix::process::CommandExt;
use std::process::{Child, Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Condvar, Mutex};
use std::thread::JoinHandle;
use std::time::Duration;

struct Slot {
    tunnel: TunnelConfig,
    child: Option<Child>,
    generation: u64,       // tells output of an old session from the current one
    next_start: f64,
    started_at: f64,
    backoff: u64,
    state: State,
    message: String,
    since: f64,
    last_error: String,
    failures: u32,         // consecutive attempts that ended without ever connecting
    refreshing: bool,      // a healthy session ended (idle timeout) and a new one opens quietly
}

impl Slot {
    fn new(t: TunnelConfig) -> Self {
        Slot { tunnel: t, child: None, generation: 0, next_start: 0.0, started_at: 0.0, backoff: 5, state: State::Starting, message: "Starting...".into(),
               since: now(), last_error: String::new(), failures: 0, refreshing: false }
    }
}

#[derive(Default)]
struct Shared {
    slots: HashMap<String, Slot>,
    invalid: Vec<(usize, String)>,   // (index into cfg.tunnels, reason)
    cfg: Option<AppConfig>,
}

type Loader = Box<dyn Fn() -> Option<AppConfig> + Send + Sync>;
type Callback = Arc<dyn Fn() + Send + Sync>;

struct Inner {
    shared: Mutex<Shared>,
    wake: (Mutex<bool>, Condvar),
    stop: AtomicBool,
    generation: AtomicU64,
    load: Loader,
    interval: Duration,
    on_change: Mutex<Option<Callback>>,
}

pub struct TunnelManager { inner: Arc<Inner>, thread: Mutex<Option<JoinHandle<()>>> }

fn key_of(t: &TunnelConfig) -> String { format!("{}|{}|{}|{}|{}", t.instance_id, t.remote_port, t.local_port, t.profile, t.region) }

impl TunnelManager {
    pub fn new() -> Self { Self::with(Box::new(config_store::load), Duration::from_secs(3)) }

    pub fn with(load: Loader, interval: Duration) -> Self {
        TunnelManager {
            inner: Arc::new(Inner { shared: Mutex::new(Shared::default()), wake: (Mutex::new(false), Condvar::new()), stop: AtomicBool::new(false),
                                    generation: AtomicU64::new(0), load, interval, on_change: Mutex::new(None) }),
            thread: Mutex::new(None),
        }
    }

    pub fn set_on_change(&self, cb: impl Fn() + Send + Sync + 'static) { *lock(&self.inner.on_change) = Some(Arc::new(cb)); }

    pub fn running(&self) -> bool { lock(&self.thread).as_ref().is_some_and(|h| !h.is_finished()) }

    pub fn start(&self) {
        let mut th = lock(&self.thread);
        if th.as_ref().is_some_and(|h| !h.is_finished()) { return; }
        self.inner.stop.store(false, Ordering::SeqCst);
        let inner = self.inner.clone();
        *th = Some(std::thread::Builder::new().name("tunnel-manager".into()).spawn(move || inner.run()).expect("thread"));
    }

    pub fn stop(&self) {
        self.inner.stop.store(true, Ordering::SeqCst);
        self.poke();
        if let Some(h) = lock(&self.thread).take() { let _ = h.join(); }
    }

    /// Wakes the loop now, for example after the config changed or the person signed in.
    pub fn poke(&self) { self.inner.poke(); }

    /// Closes every session and opens fresh ones right away (also clears any back-off wait).
    pub fn restart_all(&self) {
        {
            let mut s = lock(&self.inner.shared);
            for slot in s.slots.values_mut() { stop_slot(slot); }
            s.slots.clear();
        }
        self.poke();
    }

    pub fn snapshot(&self) -> Vec<TunnelStatus> { statuses(&lock(&self.inner.shared)) }
}

impl Drop for TunnelManager { fn drop(&mut self) { self.stop(); } }

fn statuses(s: &Shared) -> Vec<TunnelStatus> {
    let Some(cfg) = &s.cfg else { return vec![] };
    cfg.tunnels.iter().enumerate().map(|(i, t)| {
        let mk = |state, message: String, since| TunnelStatus { name: t.name.clone(), instance_id: t.instance_id.clone(), local_port: t.local_port, state, message, since };
        if let Some(slot) = s.slots.get(&key_of(t)) { mk(slot.state, slot.message.clone(), slot.since) }
        else if let Some((_, why)) = s.invalid.iter().find(|(j, _)| *j == i) { mk(State::Error, why.clone(), now()) }
        else { mk(State::Disabled, "Turned off.".into(), now()) }
    }).collect()
}

impl Inner {
    fn poke(&self) { *lock(&self.wake.0) = true; self.wake.1.notify_all(); }

    fn wait(&self) {
        let mut g = lock(&self.wake.0);
        if !*g { g = self.wake.1.wait_timeout(g, self.interval).map(|r| r.0).unwrap_or_else(|e| e.into_inner().0); }
        *g = false;
    }

    fn notify(&self) { let cb = lock(&self.on_change).clone(); if let Some(cb) = cb { cb(); } }

    fn publish(&self, s: &Shared) { status_store::save(&statuses(s)); self.notify(); }

    fn run(self: Arc<Self>) {
        applog::info("Tunnel manager started.");
        while !self.stop.load(Ordering::SeqCst) {
            {
                let mut s = lock(&self.shared);
                if let Some(c) = (self.load)() { s.cfg = Some(c); }   // keep the last good config if the file is mid-write
                if let Some(cfg) = s.cfg.clone() { self.reconcile(&mut s, &cfg); }
                self.publish(&s);
            }
            self.wait();
        }
        {
            let mut s = lock(&self.shared);
            for slot in s.slots.values_mut() { stop_slot(slot); }
            s.slots.clear();
            s.cfg = None;
            status_store::save(&[]);
        }
        self.notify();
        applog::info("Tunnel manager stopped.");
    }

    fn set(&self, s: &mut Slot, state: State, message: String) -> bool {
        if s.state == state && s.message == message { return false; }
        if s.state != state { s.since = now(); }
        s.state = state;
        s.message = message;
        true
    }

    fn reconcile(self: &Arc<Self>, s: &mut Shared, cfg: &AppConfig) {
        s.invalid.clear();
        let global = validator::validate_global(cfg);
        let mut desired: Vec<(String, &TunnelConfig)> = vec![];
        for (i, t) in cfg.tunnels.iter().enumerate() {
            if !t.enabled { continue; }
            if let Some(p) = global.clone().or_else(|| validator::validate_tunnel(t, &cfg.tunnels)) { s.invalid.push((i, p)); continue; }
            desired.push((key_of(t), t));
        }
        let gone: Vec<String> = s.slots.keys().filter(|k| !desired.iter().any(|(d, _)| d == *k)).cloned().collect();
        for k in gone {
            if let Some(mut slot) = s.slots.remove(&k) { applog::info(&format!("Stopping tunnel {}", slot.tunnel.name)); stop_slot(&mut slot); }
        }
        let mut changed = false;
        for (key, t) in desired {
            let slot = s.slots.entry(key).or_insert_with(|| Slot::new(t.clone()));
            slot.tunnel = t.clone();   // picks up renames and environment changes without a restart
            changed |= self.step(slot);
        }
        if changed { /* publish happens right after reconcile */ }
    }

    /// One look at one tunnel. Returns true when its state changed.
    fn step(self: &Arc<Self>, slot: &mut Slot) -> bool {
        let t = slot.tunnel.clone();
        let n = now();
        let mut changed = false;
        let alive = match slot.child.as_mut() { Some(c) => matches!(c.try_wait(), Ok(None)), None => false };
        if slot.child.is_some() && alive {
            // Safety first: a tunnel must only ever listen on this computer. If anything else shows up, close it.
            if netcheck::is_exposed(t.local_port) {
                applog::error(&format!("Tunnel {}: port {} is open to the network. Closing the session.", t.name, t.local_port));
                stop_slot(slot);
                slot.refreshing = false; slot.backoff = 60; slot.next_start = n + 60.0;
                return self.set(slot, State::Error, format!("Stopped for safety: port {} was reachable from the network, not only from this computer.", t.local_port));
            }
            // Fallback detection: process alive and the local port is listening => the tunnel is up.
            if (slot.state == State::Starting || slot.refreshing) && netcheck::is_listening(t.local_port) {
                slot.refreshing = false; slot.failures = 0;
                changed |= self.set(slot, State::Connected, format!("Connected. Local port {} is ready.", t.local_port));
            } else if slot.refreshing && n - slot.started_at > 25.0 {
                slot.refreshing = false;   // the quiet renewal is not coming up: now it is worth telling the person
                changed |= self.set(slot, State::Starting, "Reconnecting...".into());
            } else if slot.state == State::Starting && n - slot.started_at > 45.0 {
                let m = if slot.last_error.is_empty() { "Still connecting... check the server is online in SSM and you are signed in to AWS.".to_string() } else { slot.last_error.clone() };
                changed |= self.set(slot, State::Starting, m);
            }
            return changed;
        }

        if let Some(mut child) = slot.child.take() {
            // The session ended. Usually that is normal: AWS closes an idle port-forward session, and a session also ends
            // when the client using it disconnects. Those are renewed quietly.
            let code = child.wait().ok().and_then(|s| s.code()).unwrap_or(-1);
            let ran_for = n - slot.started_at;
            let was_up = slot.state == State::Connected;
            if !slot.last_error.is_empty() {
                slot.refreshing = false;
                slot.backoff = if ran_for > 60.0 { 5 } else { (slot.backoff * 2).min(60) };
                slot.next_start = n + slot.backoff as f64;
                let m = format!("{} Retrying in {}s.", slot.last_error, slot.backoff);
                changed |= self.set(slot, State::Error, m);
                applog::warn(&format!("Tunnel {} exited with code {code}: {}", t.name, slot.last_error));
            } else if was_up && ran_for > 20.0 {
                slot.failures = 0; slot.backoff = 5; slot.next_start = n; slot.refreshing = true;
                applog::info(&format!("Tunnel {}: session ended after {} min (code {code}); renewing it quietly.", t.name, (ran_for / 60.0) as u64));
            } else {
                slot.failures += 1;   // ended without ever being healthy: retry a couple of times without alarm
                slot.backoff = (2 * slot.failures as u64).min(60);
                slot.next_start = n + slot.backoff as f64;
                applog::warn(&format!("Tunnel {} ended after {}s with code {code} (attempt {}).", t.name, ran_for as u64, slot.failures));
                if slot.failures >= 3 {
                    slot.refreshing = false;
                    slot.backoff = (slot.backoff * 2).min(60);
                    slot.next_start = n + slot.backoff as f64;
                    let m = format!("Cannot keep a connection open (code {code}). Check the server is online in SSM and you are signed in to AWS. Trying again in {}s.", slot.backoff);
                    changed |= self.set(slot, State::Retrying, m);
                } else if !slot.refreshing {
                    changed |= self.set(slot, State::Starting, "Reconnecting...".into());
                }
            }
        }
        if now() >= slot.next_start { changed |= self.start_slot(slot); }
        changed
    }

    fn start_slot(self: &Arc<Self>, slot: &mut Slot) -> bool {
        let t = slot.tunnel.clone();
        slot.last_error.clear();
        if aws_cli::find_aws().is_none() {
            slot.next_start = now() + 10.0;
            return self.set(slot, State::Error, "The AWS CLI is not installed. Click Add / change servers in the dashboard to install it.".into());
        }
        if aws_cli::find_plugin().is_none() {
            slot.next_start = now() + 10.0;
            return self.set(slot, State::Error, "The Session Manager plugin is not installed. Click Add / change servers in the dashboard to install it.".into());
        }
        // Never start on a port another program already owns: it could answer in place of the server and the dashboard would
        // wrongly show "connected" (and SSH would send the login to it).
        if netcheck::is_listening(t.local_port) {
            if slot.refreshing && slot.failures < 3 { slot.failures += 1; slot.next_start = now() + 2.0; return false; }   // the old session is still letting go
            slot.backoff = (slot.backoff * 2).min(60);
            slot.next_start = now() + slot.backoff as f64;
            let m = format!("{} Trying again in {}s.", aws_cli::friendly_error("address already in use", Some(t.local_port)), slot.backoff);
            return self.set(slot, State::Error, m);
        }
        let params = format!("portNumber={},localPortNumber={}", t.remote_port, t.local_port);
        let cmdline = match aws_cli::build_args(&["ssm", "start-session", "--target", &t.instance_id, "--document-name", "AWS-StartPortForwardingSession", "--parameters", &params], &t.profile, &t.region) {
            Ok(c) => c,
            Err(e) => { slot.next_start = now() + 10.0; return self.set(slot, State::Error, e); }
        };
        let mut cmd = Command::new(&cmdline[0]);
        cmd.args(&cmdline[1..]).stdin(Stdio::null()).stdout(Stdio::piped()).stderr(Stdio::piped()).process_group(0);
        aws_cli::apply_env(&mut cmd);
        // if SSMConnect dies without cleaning up, the session dies with it
        unsafe { cmd.pre_exec(|| { libc::prctl(libc::PR_SET_PDEATHSIG, libc::SIGTERM); Ok(()) }); }
        match cmd.spawn() {
            Ok(mut child) => {
                slot.generation = self.generation.fetch_add(1, Ordering::SeqCst) + 1;
                let (key, generation) = (key_of(&t), slot.generation);
                if let Some(out) = child.stdout.take() { self.reader(BufReader::new(out), key.clone(), generation, false); }
                if let Some(err) = child.stderr.take() { self.reader(BufReader::new(err), key, generation, true); }
                slot.child = Some(child);
                slot.started_at = now();
                applog::info(&format!("Started tunnel {}: 127.0.0.1:{} -> {}:{}", t.name, t.local_port, t.instance_id, t.remote_port));
                if !slot.refreshing { return self.set(slot, State::Starting, "Opening secure session to the server...".into()); }
                false
            }
            Err(e) => {
                slot.backoff = (slot.backoff * 2).min(60);
                slot.next_start = now() + slot.backoff as f64;
                applog::error(&format!("Could not start tunnel {}: {e}", t.name));
                self.set(slot, State::Error, aws_cli::friendly_error(&e.to_string(), Some(t.local_port)))
            }
        }
    }

    fn reader<R: std::io::Read + Send + 'static>(self: &Arc<Self>, r: BufReader<R>, key: String, generation: u64, is_err: bool) {
        let me = self.clone();
        std::thread::spawn(move || {
            for line in r.lines().map_while(Result::ok) { me.on_line(&key, generation, line.trim(), is_err); }
            me.poke();   // a session that ended is noticed straight away
        });
    }

    fn on_line(&self, key: &str, generation: u64, line: &str, is_err: bool) {
        if line.is_empty() { return; }
        let low = line.to_lowercase();
        let mut s = lock(&self.shared);
        let Some(slot) = s.slots.get_mut(key).filter(|sl| sl.generation == generation) else { return };
        let (port, name) = (slot.tunnel.local_port, slot.tunnel.name.clone());
        let mut changed = false;
        if low.contains("waiting for connections") || low.contains("opened for sessionid") {
            slot.last_error.clear(); slot.refreshing = false; slot.failures = 0;
            changed = self.set(slot, State::Connected, format!("Connected. Local port {port} is ready."));
            applog::info(&format!("[{name}] {line}"));
        } else if is_err || low.contains("an error occurred") {
            slot.last_error = aws_cli::friendly_error(line, Some(port));
            applog::warn(&format!("[{name}] {line}"));
        } else {
            applog::info(&format!("[{name}] {line}"));
        }
        if changed { self.publish(&s); }
    }
}

fn stop_slot(slot: &mut Slot) {
    if let Some(mut child) = slot.child.take() {
        if matches!(child.try_wait(), Ok(None)) {
            let pid = child.id() as i32;
            unsafe { libc::killpg(pid, libc::SIGTERM); }   // the CLI and the plugin run in their own process group
            for _ in 0..30 {
                if matches!(child.try_wait(), Ok(Some(_))) { return; }
                std::thread::sleep(Duration::from_millis(100));
            }
            unsafe { libc::killpg(pid, libc::SIGKILL); }
        }
        let _ = child.wait();
    }
}
