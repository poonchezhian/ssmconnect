//! Works out what kind of server something is (Production, Staging ...) from its AWS tags first; the name is only a guess.

use regex::Regex;
use std::collections::HashSet;
use std::sync::OnceLock;

const KEY_PRIORITY: [&str; 10] = ["environment", "env", "stage", "tier", "appenv", "app-env", "deployment", "lifecycle", "landscape", "purpose"];

fn tokens(s: &str) -> Vec<String> {
    s.to_lowercase().split(|c: char| !c.is_ascii_alphanumeric()).filter(|t| !t.is_empty()).map(String::from).collect()
}

/// The environment tag (any letter case, also names like "app:environment"). None when there is none.
pub fn from_tags(tags: &[(String, String)]) -> Option<String> {
    let usable: Vec<&(String, String)> = tags.iter().filter(|(k, v)| !v.trim().is_empty() && !k.to_lowercase().starts_with("aws:")).collect();
    for want in KEY_PRIORITY {
        if let Some((_, v)) = usable.iter().find(|(k, _)| k.trim().to_lowercase() == want) {
            return Some(normalise(v));
        }
    }
    for (k, v) in &usable {
        let kl = k.trim().to_lowercase();
        if [":environment", "-environment", "_environment", ":env", "-env", "_env"].iter().any(|s| kl.ends_with(s)) {
            return Some(normalise(v));
        }
    }
    None
}

/// Tag first, then a careful guess from the name, otherwise empty.
pub fn classify(tags: &[(String, String)], name: &str) -> String {
    from_tags(tags).or_else(|| guess_from_name(name)).unwrap_or_default()
}

/// True only for real production. "non-prod", "pre-prod" and names that merely contain "prod" are not.
pub fn is_prod(label: &str) -> bool { normalise(label) == "Production" }

/// Maps free text such as "prod", "PRD", "non-production" or "uat2" to a short standard label.
pub fn normalise(value: &str) -> String {
    static DIGITS: OnceLock<Regex> = OnceLock::new();
    let digits = DIGITS.get_or_init(|| Regex::new(r"\d+$").unwrap());
    let v = value.trim();
    if v.is_empty() { return String::new(); }
    // "uat2" and "prod01" mean "uat" and "prod"
    let t: Vec<String> = tokens(v).into_iter().map(|k| { let s = digits.replace(&k, "").to_string(); if s.is_empty() { k } else { s } }).collect();
    let joined = t.concat();
    let has = |words: &[&str]| t.iter().any(|k| words.contains(&k.as_str())) || words.contains(&joined.as_str());
    let t_has = |w: &str| t.iter().any(|k| k == w);

    if has(&["nonprod", "nonproduction", "preprod", "preproduction"]) || (t_has("non") && has(&["prod", "production"])) || (t_has("pre") && has(&["prod", "production"])) {
        return "Pre-production".into();
    }
    if has(&["prod", "production", "prd", "live"]) { return "Production".into(); }
    if has(&["uat"]) { return "UAT".into(); }
    if has(&["stg", "stage", "staging"]) { return "Staging".into(); }
    if has(&["qa", "test", "testing", "tst", "sit"]) { return "Test".into(); }
    if has(&["dev", "development"]) { return "Development".into(); }
    if has(&["dr", "disasterrecovery"]) { return "DR".into(); }
    if has(&["sandbox", "sbx", "demo", "poc"]) { return "Sandbox".into(); }
    if has(&["internal"]) { return "Internal".into(); }
    if v.chars().count() > 24 { v.chars().take(24).collect() } else {
        let mut c = v.chars();
        c.next().map(|f| f.to_uppercase().collect::<String>() + c.as_str()).unwrap_or_default()
    }
}

fn guess_from_name(name: &str) -> Option<String> {
    let t: HashSet<String> = tokens(name).into_iter().collect();
    if t.is_empty() { return None; }
    let any = |ws: &[&str]| ws.iter().any(|w| t.contains(*w));
    Some(if any(&["nonprod", "preprod"]) { "Pre-production" }
        else if any(&["prod", "production", "prd"]) { "Production" }
        else if any(&["uat"]) { "UAT" }
        else if any(&["staging", "stg"]) { "Staging" }
        else if any(&["qa", "test"]) { "Test" }
        else if any(&["dev", "development"]) { "Development" }
        else { return None }.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    fn tag(k: &str, v: &str) -> Vec<(String, String)> { vec![(k.into(), v.into())] }

    #[test]
    fn tags_beat_names() {
        assert_eq!(classify(&tag("Environment", "prd"), "dev-box"), "Production");
        assert_eq!(classify(&tag("Env", "uat2"), "x"), "UAT");
        assert_eq!(classify(&tag("app:environment", "Stage"), "x"), "Staging");
    }

    #[test]
    fn never_prod_by_accident() {
        for v in ["non-prod", "pre-prod", "NonProduction"] { assert!(!is_prod(v), "{v}"); }
        assert_eq!(classify(&[], "product-api"), "");
        assert!(is_prod("live"));
        assert_eq!(classify(&[], "web-prod-1"), "Production");
    }
}
