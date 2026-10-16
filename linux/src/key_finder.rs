//! Looks for SSH private keys (.pem, id_rsa ...) in the usual places and, on request, in any folder the person picks.
//! Only the first few hundred bytes of a candidate are read to confirm it is a key. Nothing is copied or uploaded.
//!
//! Keys are also recognised by content: AWS shows a fingerprint for every key pair, and for an unencrypted RSA key the same
//! fingerprint can be worked out locally (see `fingerprints`), so a file is matched to its server whatever it is called.

use crate::{paths, util};
use base64::Engine;
use md5::Md5;
use sha1::{Digest, Sha1};
use std::collections::{BTreeMap, HashSet};
use std::fs;
use std::io::Read;
use std::path::Path;
use std::time::{Duration, Instant};

const SKIP: [&str; 20] = ["node_modules", ".git", ".cache", ".cargo", ".rustup", ".npm", ".gradle", ".m2", ".var", "snap", "flatpak", ".local",
    ".mozilla", ".config", "proc", "sys", "dev", "__pycache__", "venv", ".venv"];

#[derive(Clone, Debug, PartialEq)]
pub struct KeyFile { pub path: String, pub format: String, pub fingerprints: HashSet<String> }

impl KeyFile {
    pub fn stem(&self) -> String { Path::new(&self.path).file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default() }
    pub fn display(&self) -> String {
        let p = Path::new(&self.path);
        format!("{}   ({})", p.file_name().map(|s| s.to_string_lossy()).unwrap_or_default(), p.parent().map(|s| s.display().to_string()).unwrap_or_default())
    }
}

pub fn default_folders() -> Vec<String> {
    let home = paths::home();
    let mut v: Vec<std::path::PathBuf> = [".ssh", ".aws", "Downloads", "Desktop", "Documents", "Dropbox", "Google Drive", "keys"].iter().map(|n| home.join(n)).collect();
    for var in ["XDG_DOWNLOAD_DIR", "XDG_DOCUMENTS_DIR", "XDG_DESKTOP_DIR"] { if let Some(p) = std::env::var_os(var) { v.push(p.into()); } }
    let mut seen = HashSet::new();
    v.into_iter().filter(|p| p.is_dir() && seen.insert(p.clone())).map(|p| p.to_string_lossy().into_owned()).collect()
}

pub fn scan_standard(limit_s: u64, cancelled: &dyn Fn() -> bool) -> Vec<KeyFile> {
    let mut found = BTreeMap::new();
    let stop = Instant::now() + Duration::from_secs(limit_s);
    for f in default_folders() { walk(Path::new(&f), 5, &mut found, stop, cancelled); }
    sorted(found)
}

pub fn scan_folder(folder: &str, limit_s: u64, cancelled: &dyn Fn() -> bool) -> Vec<KeyFile> {
    let mut found = BTreeMap::new();
    walk(Path::new(folder), 10, &mut found, Instant::now() + Duration::from_secs(limit_s), cancelled);
    sorted(found)
}

fn sorted(m: BTreeMap<String, KeyFile>) -> Vec<KeyFile> {
    let mut v: Vec<KeyFile> = m.into_values().collect();
    v.sort_by_key(|k| (k.stem().to_lowercase(), k.path.to_lowercase()));
    v
}

fn walk(dir: &Path, depth: i32, found: &mut BTreeMap<String, KeyFile>, stop: Instant, cancelled: &dyn Fn() -> bool) {
    if depth < 0 || Instant::now() > stop || cancelled() { return; }
    let Ok(rd) = fs::read_dir(dir) else { return };
    let mut entries: Vec<_> = rd.flatten().collect();
    entries.sort_by_key(|e| e.file_name());
    for e in entries {
        let Ok(ft) = e.file_type() else { continue };
        if ft.is_symlink() { continue; }
        let path = e.path();
        if ft.is_file() {
            let p = path.to_string_lossy().into_owned();
            if let Some(fmt) = classify(&path) {
                found.entry(p.clone()).or_insert_with(|| KeyFile { fingerprints: if fmt == "pem" { fingerprints(&path) } else { HashSet::new() }, path: p, format: fmt });
            }
        } else if ft.is_dir() && !SKIP.contains(&e.file_name().to_string_lossy().as_ref()) {
            walk(&path, depth - 1, found, stop, cancelled);
        }
    }
}

/// "pem" or "openssh" when the file really is a private key, otherwise None. PuTTY .ppk files are not used on Linux.
pub fn classify(file: &Path) -> Option<String> {
    let name = file.file_name()?.to_string_lossy().into_owned();
    let ext = file.extension().map(|e| format!(".{}", e.to_string_lossy().to_lowercase())).unwrap_or_default();
    if !(matches!(ext.as_str(), ".pem" | ".key" | ".cer") || (ext.is_empty() && name.starts_with("id_"))) { return None; }
    let size = fs::metadata(file).ok()?.len();
    if !(100..=32 * 1024).contains(&size) { return None; }
    let mut buf = [0u8; 256];
    let n = fs::File::open(file).ok()?.read(&mut buf).ok()?;
    let head = String::from_utf8_lossy(&buf[..n]);
    if head.contains("BEGIN OPENSSH PRIVATE KEY") { Some("openssh".into()) } else if head.contains("PRIVATE KEY-----") { Some("pem".into()) } else { None }
}

/// Finds the key file for a server. Most reliable first: the file's fingerprint equals the server's key-pair fingerprint;
/// then a file named exactly like the key pair; then a similar name. Also returns how it matched, in plain words.
pub fn find<'a>(keys: &'a [KeyFile], key_name: &str, fingerprint: &str) -> (Option<&'a KeyFile>, &'static str) {
    let rank = |k: &&KeyFile| if k.format == "pem" { 0 } else { 1 };
    if !fingerprint.is_empty() {
        if let Some(k) = keys.iter().filter(|k| k.fingerprints.contains(&fingerprint.to_lowercase())).min_by_key(rank) { return (Some(k), "verified: this key belongs to the server"); }
    }
    if key_name.trim().is_empty() { return (None, ""); }
    if let Some(k) = keys.iter().filter(|k| k.stem().to_lowercase() == key_name.to_lowercase()).min_by_key(rank) { return (Some(k), "matched by name"); }
    let norm = |s: &str| { let t: String = s.to_lowercase().chars().filter(|c| c.is_alphanumeric()).collect(); t.strip_suffix("pem").map(String::from).unwrap_or(t) };
    let want = norm(key_name);
    if want.len() >= 4 {
        if let Some(k) = keys.iter().filter(|k| { let n = norm(&k.stem()); n == want || (n.len() >= 4 && (n.starts_with(&want) || want.starts_with(&n))) }).min_by_key(rank) {
            return (Some(k), "similar name - please check");
        }
    }
    (None, "")
}

// ---- fingerprints -----------------------------------------------------------------------------
// AWS shows the SHA-1 of the PKCS#8 private key for key pairs it created, and the MD5 of the public key for imported ones.
// Both are computed here with a tiny DER reader, so no extra libraries or openssl binary are needed.
const RSA_ALGO: [u8; 15] = [0x30, 0x0d, 0x06, 0x09, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01, 0x05, 0x00];

fn tlv(tag: u8, body: &[u8]) -> Vec<u8> {
    let n = body.len();
    let mut v = vec![tag];
    if n < 0x80 { v.push(n as u8); } else {
        let raw: Vec<u8> = n.to_be_bytes().iter().copied().skip_while(|b| *b == 0).collect();
        v.push(0x80 | raw.len() as u8);
        v.extend(raw);
    }
    v.extend_from_slice(body);
    v
}

/// (tag, body, next position)
fn read(buf: &[u8], pos: usize) -> Option<(u8, &[u8], usize)> {
    let tag = *buf.get(pos)?;
    let mut ln = *buf.get(pos + 1)? as usize;
    let mut p = pos + 2;
    if ln & 0x80 != 0 {
        let k = ln & 0x7f;
        ln = buf.get(p..p + k)?.iter().fold(0usize, |a, b| (a << 8) | *b as usize);
        p += k;
    }
    Some((tag, buf.get(p..p + ln)?, p + ln))
}

fn children(body: &[u8]) -> Vec<(u8, &[u8])> {
    let (mut out, mut pos) = (vec![], 0);
    while pos < body.len() { match read(body, pos) { Some((t, v, n)) => { out.push((t, v)); pos = n; } None => break } }
    out
}

/// Fingerprints AWS would display for this key pair, or an empty set (passphrase-protected, not RSA, unreadable).
pub fn fingerprints(file: &Path) -> HashSet<String> { try_fingerprints(file).unwrap_or_default() }

fn try_fingerprints(file: &Path) -> Option<HashSet<String>> {
    let text = String::from_utf8(fs::read(file).ok()?).ok()?;
    if text.contains("ENCRYPTED") { return None; }
    let begin = text.find("-----BEGIN ")?;
    let rest = &text[begin + 11..];
    let label_end = rest.find("PRIVATE KEY-----")?;
    let label = rest[..label_end].trim().to_string();
    let body_start = label_end + "PRIVATE KEY-----".len();
    let body_end = rest.find("-----END")?;
    let b64: String = rest[body_start..body_end].split_whitespace().collect();
    let der = base64::engine::general_purpose::STANDARD.decode(b64).ok()?;
    let (pkcs1, pkcs8): (Vec<u8>, Vec<u8>) = match label.as_str() {
        "RSA" => (der.clone(), tlv(0x30, &[tlv(0x02, &[0]), RSA_ALGO.to_vec(), tlv(0x04, &der)].concat())),
        "" => {
            let (_, seq, _) = read(&der, 0)?;
            let kids = children(seq);
            if kids.len() < 3 || kids[1].1 != &RSA_ALGO[2..] { return None; }
            (kids[2].1.to_vec(), der.clone())
        }
        _ => return None,
    };
    let (_, seq, _) = read(&pkcs1, 0)?;
    let k = children(seq);
    let (n, e) = (k.get(1)?.1, k.get(2)?.1);
    let pub_pkcs1 = tlv(0x30, &[tlv(0x02, n), tlv(0x02, e)].concat());
    let spki = tlv(0x30, &[RSA_ALGO.to_vec(), tlv(0x03, &[&[0u8][..], &pub_pkcs1].concat())].concat());
    Some([util::colon_hex(&Sha1::digest(&pkcs8)), util::colon_hex(&Md5::digest(&spki))].into_iter().collect())
}
