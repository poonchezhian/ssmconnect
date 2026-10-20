# Built by packaging/build-rpm.sh (rpmbuild) from the sources in the tarball. Version is passed in with --define "ver X.Y.Z".
%global debug_package %{nil}
Name:           ssmconnect
Version:        %{ver}
Release:        1%{?dist}
Summary:        SSH and Remote Desktop to AWS servers over Session Manager
License:        Apache-2.0
URL:            https://github.com/poonchezhian/ssmconnect
Source0:        ssmconnect-%{version}-src.tar.gz
BuildRequires:  cargo
BuildRequires:  rust
BuildRequires:  gcc
BuildRequires:  pkgconfig(gtk4) >= 4.12
BuildRequires:  pkgconfig(libadwaita-1) >= 1.5
Requires:       gtk4 >= 4.12
Requires:       libadwaita >= 1.5
Requires:       openssh-clients
Recommends:     freerdp
Recommends:     remmina

%description
SSMConnect finds your AWS servers, opens secure tunnels through AWS Systems Manager (port 22 stays closed)
and configures OpenSSH so each server is one short command away. Windows servers open in Remote Desktop.
A native GTK 4 application written in Rust.

%prep
%setup -q -n ssmconnect-%{version}-src

%build
cargo build --release --locked

%install
target/release/ssmconnect --install-files --prefix %{_prefix} --destdir %{buildroot}
# the update repository: new releases arrive with the system's normal updates (dnf upgrade, GNOME Software, Discover)
install -Dpm644 packaging/repo/ssmconnect.repo %{buildroot}%{_sysconfdir}/yum.repos.d/ssmconnect.repo
install -Dpm644 SIGNING-KEY.asc %{buildroot}%{_sysconfdir}/pki/rpm-gpg/RPM-GPG-KEY-ssmconnect

%files
%license %{_licensedir}/%{name}/LICENSE
%license %{_licensedir}/%{name}/NOTICE
%doc %{_docdir}/ssmconnect
%{_bindir}/ssmconnect
%config(noreplace) %{_sysconfdir}/yum.repos.d/ssmconnect.repo
%{_sysconfdir}/pki/rpm-gpg/RPM-GPG-KEY-ssmconnect
%{_datadir}/applications/io.github.poonchezhian.SSMConnect.desktop
%{_datadir}/metainfo/io.github.poonchezhian.SSMConnect.metainfo.xml
%{_datadir}/icons/hicolor/*/apps/io.github.poonchezhian.SSMConnect.png

%changelog
* Fri Oct 16 2026 Poonchezhian Periasamy <poonchezhian@gmail.com> - %{version}-1
- See CHANGELOG.md
