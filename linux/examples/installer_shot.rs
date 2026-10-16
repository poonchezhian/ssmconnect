//! Developer tool: renders the installer window to PNGs, one per step.  cargo run --example installer_shot -- OUTDIR [light|dark]
use adw::prelude::*;
use gtk::graphene;

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let dir = args.first().cloned().expect("output dir");
    let dark = args.get(1).is_some_and(|s| s == "dark");
    let home = std::env::temp_dir().join(format!("ssmconnect-inst-{}", std::process::id()));
    std::fs::create_dir_all(&home).unwrap();
    std::env::set_var("HOME", &home);
    let hook: Box<dyn Fn(&adw::ApplicationWindow, &dyn Fn(usize))> = Box::new(move |win, goto| {
        adw::StyleManager::default().set_color_scheme(if dark { adw::ColorScheme::ForceDark } else { adw::ColorScheme::ForceLight });
        for step in 0..3 {
            goto(step);
            let ctx = gtk::glib::MainContext::default();
            let until = std::time::Instant::now() + std::time::Duration::from_millis(700);
            while std::time::Instant::now() < until { ctx.iteration(false); }
            let (w, h) = (win.width() as f32, win.height() as f32);
            let snap = gtk::Snapshot::new();
            gtk::WidgetPaintable::new(Some(win)).snapshot(&snap, w as f64, h as f64);
            let tex = win.native().unwrap().renderer().unwrap().render_texture(&snap.to_node().unwrap(), Some(&graphene::Rect::new(0.0, 0.0, w, h)));
            let out = format!("{dir}/installer-{}.png", step + 1);
            tex.save_to_png(&out).unwrap();
            println!("saved {out}");
        }
        win.application().unwrap().quit();
    });
    ssmconnect::ui::installer::run_with(None, None, Some(hook));
}
