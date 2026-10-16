use super::app::App;
use super::common::message;
use adw::prelude::*;
use std::rc::Rc;

pub struct Wizard { win: adw::Window }

impl Wizard {
    pub fn new(_app: &Rc<App>, parent: &adw::ApplicationWindow, _add_account: bool) -> Wizard {
        let win = adw::Window::builder().transient_for(parent).modal(true).build();
        message(parent, "Setup arrives in the next release", "The guided setup is being ported to Rust. Use the 0.0.x Python edition, or edit ~/.config/ssmconnect/config.json, meanwhile.");
        Wizard { win }
    }
    pub fn present(&self) { self.win.close(); }
    pub fn window(&self) -> &adw::Window { &self.win }
}
