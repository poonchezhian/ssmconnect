// Copyright 2026 Poonchezhian Periasamy <poonchezhian@gmail.com>
// Licensed under the Apache License, Version 2.0 (see LICENSE).
//! SSMConnect for Linux: SSH and Remote Desktop to AWS servers over Session Manager, with port 22 closed.

pub mod applog;
pub mod aws_cli;
pub mod aws_profiles;
pub mod classifier;
pub mod cli;
pub mod config_store;
pub mod desktop;
pub mod discovery;
pub mod installer;
pub mod installer_text;
pub mod key_finder;
pub mod known_hosts;
pub mod launchers;
pub mod models;
pub mod netcheck;
pub mod paths;
pub mod prereq_installer;
pub mod sso;
pub mod sso_discovery;
pub mod signature;
pub mod ssh_config;
pub mod status_store;
pub mod tunnel_manager;
pub mod ui;
pub mod update_checker;
pub mod util;
pub mod validator;

pub const VERSION: &str = env!("CARGO_PKG_VERSION");
pub const APP_ID: &str = "io.github.poonchezhian.SSMConnect";
pub const APP_NAME: &str = "SSMConnect";
