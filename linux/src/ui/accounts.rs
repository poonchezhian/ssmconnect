use super::app::App;
use super::common::message;
use std::rc::Rc;

pub fn show(_app: &Rc<App>, parent: &adw::ApplicationWindow) { message(parent, "Coming soon", "Account management is being ported to Rust."); }
