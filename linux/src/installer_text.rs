//! The installer in a terminal (no display, or `--text`): the same steps as the window, as questions.

use crate::installer::{self, Elevation};
use crate::VERSION;
use std::io::{BufRead, Write};
use std::path::PathBuf;
use std::process::ExitCode;

pub struct Options { pub yes: bool, pub system: bool, pub prefix: Option<PathBuf>, pub source: Option<PathBuf> }

fn ask(q: &str, default_yes: bool, yes: bool) -> bool {
    if yes { println!("{q} -> yes"); return true; }
    print!("{q} [{}] ", if default_yes { "Y/n" } else { "y/N" });
    let _ = std::io::stdout().flush();
    let mut line = String::new();
    if std::io::stdin().lock().read_line(&mut line).unwrap_or(0) == 0 { return default_yes; }
    match line.trim().to_lowercase().as_str() { "" => default_yes, "y" | "yes" => true, _ => false }
}

pub fn run(o: Options) -> ExitCode {
    println!("SSMConnect {VERSION} installer\n\nSSMConnect is open source (Apache License 2.0). It bundles no third-party tools: what it needs is fetched from its\nsource first, then SSMConnect itself is installed.\n");
    let deps = installer::dependencies();
    println!("What SSMConnect needs:");
    for d in &deps {
        println!("  [{}] {:<34} {}{}", if d.found.is_some() { "x" } else { " " }, d.name, if d.required { "required" } else { "optional" },
                 if d.found.is_none() { match d.source { installer::Source::Amazon => "  (from Amazon, signature checked)", _ => "  (from your distribution)" } } else { "" });
    }
    let mut chosen: Vec<&str> = vec![];
    for d in deps.iter().filter(|d| d.found.is_none()) {
        // --yes accepts the defaults: required tools yes, optional extras no
        if d.required || (!o.yes && ask(&format!("Also install the optional {}?", d.name), false, false)) { chosen.push(d.id); }
    }
    if !chosen.is_empty() {
        let names: Vec<&str> = deps.iter().filter(|d| chosen.contains(&d.id)).map(|d| d.name).collect();
        if !ask(&format!("\nInstall {} now?", names.join(", ")), true, o.yes) { println!("Nothing was installed."); return ExitCode::from(1); }
        if let Err(e) = installer::install_dependencies(&chosen, Elevation::Terminal, &|m| println!("  {m}")) { eprintln!("\n{e}"); return ExitCode::from(1); }
    }
    let still = installer::missing_required();
    if !still.is_empty() {
        eprintln!("\nStill missing: {}. Install them, then run this installer again.", still.iter().map(|d| d.name).collect::<Vec<_>>().join(", "));
        return ExitCode::from(1);
    }

    let prefix = o.prefix.clone().unwrap_or_else(|| if o.system { installer::system_prefix() } else { installer::user_prefix() });
    let source = o.source.clone().or_else(|| std::env::current_exe().ok()).unwrap_or_default();
    println!("\nInstalling SSMConnect to {} ...", prefix.display());
    let writable = std::fs::create_dir_all(&prefix).is_ok() && std::fs::metadata(&prefix).map(|m| !m.permissions().readonly()).unwrap_or(false);
    let res = if writable { installer::install_files(&prefix, std::path::Path::new(""), &source, &|m| println!("  {m}")).map(|_| ()) }
              else { installer::install_files_elevated(&prefix, &source, Elevation::Terminal, &|m| println!("  {m}")) };
    if let Err(e) = res { eprintln!("\n{e}"); return ExitCode::from(1); }
    let bin = prefix.join("bin");
    println!("\nDone. Start SSMConnect from your application menu, or run: {}", bin.join("ssmconnect").display());
    if !installer::on_path(&bin) { println!("Note: {} is not on your PATH yet; add it to use the plain `ssmconnect` command.", bin.display()); }
    println!("To remove it later: ssmconnect uninstall --prefix {}", prefix.display());
    ExitCode::SUCCESS
}
