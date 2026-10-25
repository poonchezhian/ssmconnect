mod common;
use common::Home;
use ssmconnect::transfer::*;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};

/// An `ssh` that runs the command on this computer, so the transfer engine can be tried without a server.
const LOCAL_SSH: &str = "#!/bin/sh\nfor last; do :; done\nexec sh -c \"$last\"\n";

fn remote() -> Remote { Remote { port: 2201, user: "ubuntu".into(), key: None, host_alias: "ssc-a-i-1".into(), strict: false, sudo: None } }

fn with_local_ssh(h: &Home) -> String {
    h.tool("ssh", LOCAL_SSH);
    let old = std::env::var("PATH").unwrap_or_default();
    std::env::set_var("PATH", format!("{}:{old}", h.join(".local/share/ssmconnect/bin").display()));
    old
}

#[test]
fn quoting_and_paths() {
    assert_eq!(q("it's here"), "'it'\"'\"'s here'");
    assert_eq!((parent("/home/ubuntu/x"), parent("/home"), parent("/")), ("/home/ubuntu".to_string(), "/".to_string(), "/".to_string()));
    assert_eq!((join("/a", "b"), join("/", "b")), ("/a/b".to_string(), "/b".to_string()));
    assert!(is_root_name("root") && is_root_name(" ROOT ") && !is_root_name("ubuntu"));
}

#[test]
fn switched_user_goes_through_sudo_with_the_helper() {
    let mut r = remote();
    assert_eq!(wrap(&r, "ls"), "ls");
    r.sudo = Some(Sudo { user: "deploy".into(), askpass_dir: None });
    assert_eq!(wrap(&r, "ls 'x'"), "sudo -n -u 'deploy' sh -c 'ls '\"'\"'x'\"'\"''");
    r.sudo = Some(Sudo { user: "deploy".into(), askpass_dir: Some("/tmp/.ssc-AbC123".into()) });
    assert!(wrap(&r, "ls").starts_with("SUDO_ASKPASS='/tmp/.ssc-AbC123/ap' sudo -A -u 'deploy' sh -c "));
}

#[test]
fn root_is_refused_by_name_and_by_uid() {
    let h = Home::new();
    let old = with_local_ssh(&h);
    let r = Remote { user: "root".into(), ..remote() };
    assert!(connect(&r).unwrap_err().contains("never works as root"));
    assert!(switch_user(&remote(), "Root", None).unwrap_err().contains("never works as root"));
    h.tool("ssh", "#!/bin/sh\nprintf '0\\n/root\\n'\n");   // a login that turns out to be uid 0
    assert!(connect(&remote()).unwrap_err().contains("root rights"));
    std::env::set_var("PATH", old);
}

#[test]
fn lists_and_copies_a_folder_between_servers_with_progress_and_stop() {
    let h = Home::new();
    let old = with_local_ssh(&h);
    let (a, b) = (h.join("server-a"), h.join("server-b"));
    std::fs::create_dir_all(a.join("site/css")).unwrap();
    std::fs::create_dir_all(&b).unwrap();
    std::fs::write(a.join("site/index.html"), "<h1>hi</h1>").unwrap();
    std::fs::write(a.join("site/css/app.css"), vec![b'x'; 300_000]).unwrap();
    std::fs::write(a.join("notes it's.txt"), "n").unwrap();
    let r = remote();
    let entries = list(&r, &a.to_string_lossy()).unwrap();
    assert_eq!(entries.iter().map(|e| (e.name.as_str(), e.is_dir)).collect::<Vec<_>>(), vec![("site", true), ("notes it's.txt", false)]);
    assert_eq!(connect(&r).map(|h| h.starts_with('/')), Ok(true));
    let names = vec!["site".to_string(), "notes it's.txt".to_string()];
    assert_eq!(existing(&r, &b.to_string_lossy(), &names).unwrap(), Vec::<String>::new());
    let last = AtomicU64::new(0);
    let cancel = AtomicBool::new(false);
    copy(&r, &a.to_string_lossy(), &names, &r, &b.to_string_lossy(), &|d, t| { assert!(d <= t.max(d)); last.store(d, Ordering::Relaxed); }, &cancel).unwrap();
    assert!(last.load(Ordering::Relaxed) > 300_000);
    assert_eq!(std::fs::read(b.join("site/css/app.css")).unwrap().len(), 300_000);
    assert_eq!(std::fs::read_to_string(b.join("notes it's.txt")).unwrap(), "n");
    assert_eq!(existing(&r, &b.to_string_lossy(), &names).unwrap(), names, "a second copy would replace these, so the window asks first");
    // Stop: nothing more is written once the flag is set
    let c = tempfile::tempdir().unwrap();
    let stop = AtomicBool::new(true);
    let err = copy(&r, &a.to_string_lossy(), &names, &r, &c.path().to_string_lossy(), &|_, _| {}, &stop).unwrap_err();
    assert_eq!(err, "Stopped.");
    // a failing source is reported as the source's problem
    let err = copy(&r, "/no/such/folder", &["x".to_string()], &r, &c.path().to_string_lossy(), &|_, _| {}, &cancel).unwrap_err();
    assert!(err.contains("first server"), "{err}");
    std::env::set_var("PATH", old);
}
