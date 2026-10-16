//! The native window: GTK 4 + libadwaita.

pub mod app;
pub mod accounts;
pub mod common;
pub mod window;
pub mod wizard;

pub fn run(background: bool) -> i32 { app::run(background) }
