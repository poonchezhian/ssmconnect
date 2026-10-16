# Built by packaging/build-rpm.sh (rpmbuild). Version is passed in with --define "ver X.Y.Z".
Name:           ssmconnect
Version:        %{ver}
Release:        1%{?dist}
Summary:        SSH and Remote Desktop to AWS servers over Session Manager
License:        Apache-2.0
URL:            https://github.com/poonchezhian/ssmconnect
Source0:        ssmconnect-%{version}-linux.tar.gz
BuildArch:      noarch
BuildRequires:  python3-devel
Requires:       python3 >= 3.10
Requires:       python3-gobject
Requires:       gtk4
Requires:       libadwaita
Requires:       openssh-clients
Recommends:     freerdp
Recommends:     remmina

%description
SSMConnect finds your AWS servers, opens secure tunnels through AWS Systems Manager (port 22 stays closed)
and configures OpenSSH so each server is one short command away. Windows servers open in Remote Desktop.

%prep
%setup -q -n ssmconnect-%{version}-linux

%build

%install
packaging/stage.sh %{buildroot} %{_prefix} %{python3_sitelib}

%files
%license %{_licensedir}/%{name}/LICENSE
%license %{_licensedir}/%{name}/NOTICE
%doc %{_docdir}/ssmconnect
%{_bindir}/ssmconnect
%{python3_sitelib}/ssmconnect
%{_datadir}/applications/io.github.poonchezhian.SSMConnect.desktop
%{_datadir}/metainfo/io.github.poonchezhian.SSMConnect.metainfo.xml
%{_datadir}/icons/hicolor/scalable/apps/io.github.poonchezhian.SSMConnect.svg

%changelog
* Fri Oct 16 2026 Poonchezhian Periasamy <poonchezhian@gmail.com> - %{version}-1
- See CHANGELOG.md
