//! The operating-system logos shown on server cards (PNG files in data/os, built into the program).

/// Picks the logo for the operating-system name SSM reports ("Ubuntu", "Amazon Linux", "Microsoft Windows Server 2022"...).
pub fn key_for(os_name: &str, windows: bool) -> &'static str {
    let n = os_name.to_lowercase();
    if n.contains("windows") || (windows && n.is_empty()) { return "windows"; }
    for (needle, key) in [("ubuntu", "ubuntu"), ("debian", "debian"), ("red hat", "rhel"), ("rhel", "rhel"), ("amazon", "amazon-linux"), ("centos", "centos"),
                          ("rocky", "rocky"), ("alma", "almalinux"), ("fedora", "fedora"), ("suse", "suse"), ("sles", "suse"), ("alpine", "alpine")] {
        if n.contains(needle) { return key; }
    }
    if windows { "windows" } else { "linux" }
}

pub fn png(key: &str) -> &'static [u8] {
    match key {
        "windows" => include_bytes!("../data/os/windows.png"),
        "ubuntu" => include_bytes!("../data/os/ubuntu.png"),
        "debian" => include_bytes!("../data/os/debian.png"),
        "rhel" => include_bytes!("../data/os/rhel.png"),
        "amazon-linux" => include_bytes!("../data/os/amazon-linux.png"),
        "centos" => include_bytes!("../data/os/centos.png"),
        "rocky" => include_bytes!("../data/os/rocky.png"),
        "almalinux" => include_bytes!("../data/os/almalinux.png"),
        "fedora" => include_bytes!("../data/os/fedora.png"),
        "suse" => include_bytes!("../data/os/suse.png"),
        "alpine" => include_bytes!("../data/os/alpine.png"),
        _ => include_bytes!("../data/os/linux.png"),
    }
}
