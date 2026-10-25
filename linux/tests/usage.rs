mod common;
use common::Home;
use ssmconnect::models::*;
use ssmconnect::usage;
use std::collections::HashSet;

#[test]
fn parses_the_scripts_answer() {
    let u = usage::parse("noise\nSSMUSAGE 4 8000000 2000000 52428800 26214400 17\n").unwrap();
    assert_eq!((u.cpus, u.mem_total_mb, u.disk_total_gb, u.cpu_pct, u.mem_pct, u.disk_pct), (4, 7812, 50, 17, 25, 50));
    assert!(usage::parse("SSMUSAGE 4 x").is_none() && usage::parse("nothing").is_none());
    assert_eq!((usage::level(59), usage::level(60), usage::level(84), usage::level(85)), ("ok", "warn", "warn", "bad"));
    assert_eq!((usage::size_text(512), usage::size_text(7812)), ("512 MB".to_string(), "8 GB".to_string()));
}

const FAKE: &str = r#"#!/bin/sh
case "$*" in
  *send-command*) case "$*" in *i-0denied*) echo 'An error occurred (AccessDeniedException) when calling the SendCommand operation: User is not authorized to perform: ssm:SendCommand' >&2; exit 254;; esac; echo cmd-1;;
  *get-command-invocation*) echo '{"Status":"Success","StandardOutputContent":"SSMUSAGE 2 4194304 1048576 20971520 2097152 9\n"}';;
esac
"#;

#[test]
fn reads_connected_servers_and_backs_off_when_refused() {
    let h = Home::new();
    h.tool("aws", FAKE);
    let mk = |id: &str, env: &str| TunnelConfig { name: id.into(), instance_id: id.into(), profile: "p".into(), region: "eu-west-1".into(), local_port: 2201, environment: env.into(), ..Default::default() };
    let ok = mk("i-0ok", "dev");
    let prod = mk("i-0prod", "Production");
    let all = vec![ok.clone(), prod.clone()];
    let up: HashSet<String> = all.iter().map(|t| t.instance_id.clone()).collect();
    let cfg = AppConfig::default();
    assert_eq!(usage::refresh(&all, &up, &cfg, false), 1, "production is skipped by default");
    let c = usage::load();
    assert_eq!(c.servers["i-0ok"].cpus, 2);
    assert_eq!(c.servers["i-0ok"].mem_pct, 25);
    assert!(!c.servers.contains_key("i-0prod"));
    assert_eq!(usage::refresh(&all, &up, &cfg, false), 0, "a fresh reading is not repeated");
    assert_eq!(usage::refresh(&all, &up, &cfg, true), 2, "Refresh usage reads everything");
    // an account that may not run commands is left alone for an hour
    let denied = mk("i-0denied", "dev");
    let only: HashSet<String> = ["i-0denied".to_string()].into();
    assert_eq!(usage::refresh(&[denied.clone()], &only, &cfg, false), 0);
    assert!(usage::load().denied_until.get("p").is_some_and(|t| *t > ssmconnect::util::now() + 3000.0));
}
