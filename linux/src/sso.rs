//! Guided company sign-in (IAM Identity Center): sign in, then list the accounts and roles the person may use.

use crate::{applog, aws_cli, aws_profiles as ap, paths, util};
use serde_json::Value;
use sha1::{Digest, Sha1};
use std::collections::HashSet;

#[derive(Clone, Debug, PartialEq)]
pub struct SsoAccount { pub id: String, pub name: String }

/// Opens the browser sign-in for a named sso-session.
pub fn login(session: &str) -> aws_cli::CliResult { aws_cli::sign_in(&["sso", "login", "--sso-session", session], "", session) }

/// The access token the AWS CLI cached after sign-in (file name = SHA-1 of the session name).
pub fn read_access_token(session: &str) -> Option<String> {
    let name = util::hex(&Sha1::digest(session.as_bytes()));
    let text = std::fs::read_to_string(paths::aws_dir().join("sso/cache").join(format!("{name}.json"))).ok()?;
    let v: Value = serde_json::from_str(&text).ok()?;
    if let Some(exp) = v.get("expiresAt").and_then(Value::as_str) {
        if util::parse_iso_utc(exp).is_some_and(|t| t < util::now()) { return None; }
    }
    v.get("accessToken").and_then(Value::as_str).filter(|s| !s.is_empty()).map(String::from)
}

pub fn list_accounts(token: &str, sso_region: &str) -> Result<Vec<SsoAccount>, String> {
    let r = aws_cli::run(&["sso", "list-accounts", "--access-token", token, "--region", sso_region, "--query", "accountList[].[accountId,accountName]", "--output", "json"], "", "", 60);
    if !r.ok() { return Err(aws_cli::friendly_error(&r.error, None)); }
    let rows: Vec<Value> = serde_json::from_str(if r.output.is_empty() { "[]" } else { &r.output }).unwrap_or_default();
    let mut v: Vec<SsoAccount> = rows.iter().map(|row| SsoAccount {
        id: row.get(0).and_then(Value::as_str).unwrap_or("").into(), name: row.get(1).and_then(Value::as_str).unwrap_or("").into() }).collect();
    v.sort_by_key(|a| a.name.to_lowercase());
    Ok(v)
}

pub fn list_roles(token: &str, sso_region: &str, account_id: &str) -> Result<Vec<String>, String> {
    let r = aws_cli::run(&["sso", "list-account-roles", "--access-token", token, "--region", sso_region, "--account-id", account_id, "--query", "roleList[].roleName", "--output", "json"], "", "", 60);
    if !r.ok() { return Err(aws_cli::friendly_error(&r.error, None)); }
    let mut v: Vec<String> = serde_json::from_str::<Vec<String>>(if r.output.is_empty() { "[]" } else { &r.output }).unwrap_or_default().into_iter().filter(|s| !s.is_empty()).collect();
    v.sort();
    Ok(v)
}

/// Saves one profile for every account the person can use under this sign-in, so all can be searched for servers.
/// Accounts that already have a profile are reused; the same role is used wherever it exists, else the account's first role.
pub fn add_all_accounts(session: &str, token: &str, sso_region: &str, preferred_role: &str, accounts: &[SsoAccount],
                        progress: &dyn Fn(String)) -> Vec<(String, SsoAccount)> {
    let everyone = ap::list_profiles();
    let (start_url, _) = ap::session_info(session);
    let key = format!("sso:{}", ap::normalise_url(start_url.as_deref().unwrap_or("")));
    let existing: Vec<&ap::ProfileInfo> = everyone.iter().filter(|p| p.kind == ap::Kind::Sso && p.account_id.is_some()
        && (p.sso_session.as_deref() == Some(session) || (start_url.is_some() && ap::primary_key(p) == key))).collect();
    let mut taken: HashSet<String> = everyone.iter().map(|p| p.name.clone()).collect();
    let mut result = vec![];
    for (i, a) in accounts.iter().enumerate() {
        progress(format!("Setting up account {} of {}: {}...", i + 1, accounts.len(), if a.name.is_empty() { &a.id } else { &a.name }));
        if let Some(have) = existing.iter().find(|p| p.account_id.as_deref() == Some(&a.id)) { result.push((have.name.clone(), a.clone())); continue; }
        let Ok(roles) = list_roles(token, sso_region, &a.id) else { continue };   // no role in this account for this person: skip it
        if roles.is_empty() { continue; }
        let role = if roles.iter().any(|r| r == preferred_role) { preferred_role.to_string() } else { roles[0].clone() };
        let name = ap::profile_name_for(&a.name, &a.id, &taken);
        if ap::save_sso_profile(&name, session, &a.id, &role).is_err() { continue; }
        taken.insert(name.clone());
        result.push((name, a.clone()));
    }
    result
}

#[derive(Default, Debug)]
pub struct SyncResult { pub added: Vec<String>, pub need_sign_in: Vec<String> }

/// Looks, for every company sign-in that is still signed in, for AWS accounts gained since last time and saves a profile
/// for each. Never opens a browser: an expired sign-in is reported in `need_sign_in` instead.
pub fn sync_members() -> SyncResult {
    let mut res = SyncResult::default();
    let everyone = ap::list_profiles();
    let mut groups: Vec<(String, Vec<&ap::ProfileInfo>)> = vec![];
    for p in everyone.iter().filter(|p| p.kind == ap::Kind::Sso && p.sso_session.is_some()) {
        let k = ap::primary_key(p);
        match groups.iter_mut().find(|(gk, _)| *gk == k) { Some((_, v)) => v.push(p), None => groups.push((k, vec![p])) }
    }
    for (_, group) in groups {
        let mut sessions: Vec<&str> = vec![];
        for p in &group { let s = p.sso_session.as_deref().unwrap(); if !sessions.contains(&s) { sessions.push(s); } }
        let found = sessions.iter().find_map(|s| read_access_token(s).map(|t| (s.to_string(), t)));
        let label = ap::primary_name(group[0]);
        let Some((session, token)) = found else { res.need_sign_in.push(label); continue };
        let Some(region) = ap::session_info(&session).1 else { continue };
        let known: HashSet<&str> = group.iter().filter_map(|p| p.account_id.as_deref()).collect();
        match list_accounts(&token, &region) {
            Ok(all) => {
                let fresh: Vec<SsoAccount> = all.into_iter().filter(|a| !known.contains(a.id.as_str())).collect();
                if fresh.is_empty() { continue; }
                let role = group.iter().find_map(|p| p.role_name.clone()).unwrap_or_default();
                for (_, a) in add_all_accounts(&session, &token, &region, &role, &fresh, &|_| {}) { res.added.push(format!("{} ({})", a.name, a.id)); }
            }
            Err(e) => applog::warn(&format!("Could not look for new AWS accounts under {label}: {e}")),
        }
    }
    res
}
