#![allow(dead_code)]
//! Every test gets its own HOME so nothing touches the real ~/.aws, ~/.ssh or ~/.config. Tests that change the environment
//! run one at a time (the environment is shared by all threads of a test binary).

use std::os::unix::fs::PermissionsExt;
use std::path::{Path, PathBuf};
use std::sync::{Mutex, MutexGuard};

static ENV: Mutex<()> = Mutex::new(());

pub struct Home { pub dir: tempfile::TempDir, _guard: MutexGuard<'static, ()> }

impl Home {
    pub fn new() -> Home {
        let guard = ENV.lock().unwrap_or_else(|e| e.into_inner());
        let dir = tempfile::tempdir().unwrap();
        std::env::set_var("HOME", dir.path());
        for v in ["XDG_CONFIG_HOME", "XDG_STATE_HOME", "XDG_DATA_HOME", "AWS_CONFIG_FILE", "AWS_SHARED_CREDENTIALS_FILE"] { std::env::remove_var(v); }
        Home { dir, _guard: guard }
    }
    pub fn path(&self) -> &Path { self.dir.path() }
    pub fn join(&self, p: &str) -> PathBuf { self.dir.path().join(p) }

    /// Installs a script as ~/.local/share/ssmconnect/bin/<name>.
    pub fn tool(&self, name: &str, body: &str) {
        let b = self.join(".local/share/ssmconnect/bin");
        std::fs::create_dir_all(&b).unwrap();
        std::fs::write(b.join(name), body).unwrap();
        std::fs::set_permissions(b.join(name), std::fs::Permissions::from_mode(0o755)).unwrap();
    }
}

/// A small fake `aws` that listens on the requested local port, or fails for the "offline" instance.
pub const FAKE_AWS: &str = r#"#!/usr/bin/env python3
import socket, sys, time
a = sys.argv
if "start-session" in a:
    if "i-0fa1100000000000" in a:
        sys.stderr.write("An error occurred (TargetNotConnected) when calling the StartSession operation\n"); sys.exit(254)
    port = int(a[a.index("--parameters") + 1].split("localPortNumber=")[1])
    s = socket.socket(); s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("127.0.0.1", port)); s.listen(1)
    print("Starting session with SessionId: x"); print("Port %d opened for sessionId x." % port); print("Waiting for connections...", flush=True)
    time.sleep(600)
"#;

pub fn wait_for(timeout_s: u64, mut cond: impl FnMut() -> bool) -> bool {
    let end = std::time::Instant::now() + std::time::Duration::from_secs(timeout_s);
    while std::time::Instant::now() < end {
        if cond() { return true; }
        std::thread::sleep(std::time::Duration::from_millis(100));
    }
    false
}
