//! The native window: GTK 4 + libadwaita.

pub mod app;
pub mod accounts;
pub mod common;
pub mod installer;
pub mod window;
pub mod wizard;

pub fn run(background: bool) -> i32 { app::run(background) }

/// The installer window (needs a display). Fetches what SSMConnect needs, then installs SSMConnect.
pub fn run_installer(prefix: Option<std::path::PathBuf>, source: Option<std::path::PathBuf>) -> i32 { installer::run(prefix, source) }
