import json
import subprocess
import unittest

from base import IsolatedHome

from ssmconnect import aws_cli, aws_profiles as ap, classifier, config_store, key_finder, ssh_config, validator
from ssmconnect.models import AppConfig, DiscoveredServer, TunnelConfig


class ClassifierTests(unittest.TestCase):
    def test_tags_beat_names(self):
        self.assertEqual(classifier.classify([("Environment", "prd")], "dev-box"), "Production")
        self.assertEqual(classifier.classify([("Env", "uat2")], "x"), "UAT")
        self.assertEqual(classifier.classify([("app:environment", "Stage")], "x"), "Staging")

    def test_never_prod_by_accident(self):
        for v in ("non-prod", "pre-prod", "NonProduction"):
            self.assertFalse(classifier.is_prod(v), v)
        self.assertEqual(classifier.classify([], "product-api"), "")
        self.assertTrue(classifier.is_prod("live"))
        self.assertEqual(classifier.classify([], "web-prod-1"), "Production")


class ValidatorTests(unittest.TestCase):
    def good(self, **kw):
        t = TunnelConfig(name="a", instance_id="i-0123456789abcdef0", profile="p", region="ap-south-1", local_port=2201)
        for k, v in kw.items():
            setattr(t, k, v)
        return t

    def test_ok_and_bad(self):
        self.assertIsNone(validator.validate_tunnel(self.good(), [self.good()]))
        self.assertIn("instance", validator.validate_tunnel(self.good(instance_id="x; rm -rf"), []))
        self.assertIn("port", validator.validate_tunnel(self.good(local_port=80), []))
        self.assertIn("region", validator.validate_tunnel(self.good(region="mars"), []))
        self.assertIn("user name", validator.validate_tunnel(self.good(user_name="a b"), []))
        a, b = self.good(), self.good(instance_id="i-0123456789abcdef1")
        self.assertIn("more than one", validator.validate_tunnel(a, [a, b]))


class ProfileTests(IsolatedHome):
    def test_sso_roundtrip_and_preservation(self):
        cfg = self.home / ".aws" / "config"
        cfg.parent.mkdir()
        cfg.write_text("[default]\nregion = eu-west-1\n\n[profile mine]\noutput = text\n")
        ap.save_sso_session("acme", "https://acme.awsapps.com/start", "us-east-1")
        ap.save_sso_profile("prod-1111", "acme", "111111111111", "Admin")
        ap.save_sso_profile("dev-2222", "acme", "222222222222", "Admin")
        profs = {p.name: p for p in ap.list_profiles()}
        self.assertEqual(set(profs), {"default", "mine", "prod-1111", "dev-2222"})
        self.assertEqual(profs["prod-1111"].kind, ap.SSO)
        self.assertEqual(profs["prod-1111"].start_url, "https://acme.awsapps.com/start")
        self.assertEqual(profs["mine"].kind, ap.OTHER)
        self.assertEqual(ap.primary_key(profs["prod-1111"]), "sso:acme.awsapps.com")
        self.assertEqual(len(ap.same_sign_in(profs["dev-2222"], list(profs.values()))), 2)
        self.assertIn("output = text", cfg.read_text())
        ap.remove_profile("dev-2222")
        self.assertNotIn("dev-2222", cfg.read_text())
        self.assertIn("[profile mine]", cfg.read_text())
        self.assertTrue(cfg.with_name("config.ssmconnect.bak").exists())

    def test_access_keys_private_and_validated(self):
        with self.assertRaises(ValueError):
            ap.save_access_keys("k", "nope", "x")
        ap.save_access_keys("k", "AKIAABCDEFGHIJKLMNOP", "a" * 40)
        cred = self.home / ".aws" / "credentials"
        self.assertEqual(oct(cred.stat().st_mode & 0o777), "0o600")
        self.assertEqual(ap.find("k").kind, ap.KEYS)

    def test_alias_and_name_helpers(self):
        p = ap.ProfileInfo("x", ap.SSO, sso_session="Acme", start_url="https://Acme.awsapps.com/start/")
        self.assertEqual(ap.primary_label(AppConfig(aliases={"sso:acme.awsapps.com": "Work"}), p, [p]), "Work")
        self.assertEqual(ap.primary_name(p), "Acme")
        self.assertEqual(ap.profile_name_for("Prod Acct!", "123456789012", {"Prod-Acct"}), "Prod-Acct-9012")


class ConfigTests(IsolatedHome):
    def test_roundtrip_and_recovery(self):
        cfg = AppConfig(profile="p", region="ap-south-1", tunnels=[TunnelConfig(name="w", instance_id="i-1", local_port=2201)])
        config_store.save(cfg)
        back = config_store.load()
        self.assertEqual(back.tunnels[0].name, "w")
        self.assertEqual(back.tunnels[0].profile, "p")           # inherited from the old single-account setting
        (self.home / ".config/ssmconnect/config.json").write_text("{broken")
        got, warning = config_store.load_or_recover()
        self.assertEqual(got.tunnels, [])
        self.assertIn("damaged file was kept", warning)


class SshConfigTests(IsolatedHome):
    def test_apply_remove_keeps_user_hosts(self):
        ssh = self.home / ".ssh"
        ssh.mkdir()
        (ssh / "config").write_text("Host mine\n    HostName example.com\n")
        ts = [TunnelConfig(name="Web Server 1", instance_id="i-aaaaaaaa", local_port=2201, user_name="ubuntu", key_file="/k/my key.pem"),
              TunnelConfig(name="web server 1", instance_id="i-bbbbbbbb", local_port=2202),
              TunnelConfig(name="Win", instance_id="i-cccccccc", local_port=2203, protocol="rdp")]
        ssh_config.apply(ts)
        ssh_config.apply(ts)    # idempotent
        text = (ssh / "config").read_text()
        self.assertEqual(text.count(ssh_config.INCLUDE_LINE), 1)
        self.assertTrue(text.startswith("# SSMConnect"))
        conf = (ssh / "ssmconnect.conf").read_text()
        self.assertIn("Host ssc-web-server-1\n", conf)
        self.assertIn('IdentityFile "/k/my key.pem"', conf)
        self.assertNotIn("Win", conf)
        self.assertEqual(ssh_config.alias_of(ts[1], ts), "ssc-web-server-1-bbbbbb")
        ssh_config.remove()
        self.assertEqual((ssh / "config").read_text(), "Host mine\n    HostName example.com\n")
        self.assertFalse((ssh / "ssmconnect.conf").exists())
        if subprocess.run(["which", "ssh"], capture_output=True).returncode == 0:
            ssh_config.apply(ts)
            out = subprocess.run(["ssh", "-F", str(ssh / "config"), "-G", "ssc-web-server-1"], capture_output=True, text=True).stdout
            self.assertIn("\nport 2201\n", out)
            self.assertIn("\nuser ubuntu\n", out)


class KeyTests(IsolatedHome):
    def test_fingerprint_matches_openssl(self):
        key = self.home / "k.pem"
        subprocess.run(["openssl", "genrsa", "-traditional", "-out", str(key), "2048"], check=True, capture_output=True)
        fps = key_finder.fingerprints(str(key))
        der = subprocess.run(["openssl", "pkcs8", "-topk8", "-nocrypt", "-in", str(key), "-outform", "DER"], capture_output=True, check=True).stdout
        sha1 = subprocess.run(["openssl", "sha1", "-c"], input=der, capture_output=True, check=True).stdout.decode().split("= ")[1].strip()
        self.assertIn(sha1, fps)
        pub = subprocess.run(["openssl", "rsa", "-in", str(key), "-pubout", "-outform", "DER"], capture_output=True, check=True).stdout
        md5 = subprocess.run(["openssl", "md5", "-c"], input=pub, capture_output=True, check=True).stdout.decode().split("= ")[1].strip()
        self.assertIn(md5, fps)
        # PKCS#8 form of the same key gives the same fingerprints
        p8 = self.home / "k8.pem"
        subprocess.run(["openssl", "pkcs8", "-topk8", "-nocrypt", "-in", str(key), "-out", str(p8)], check=True)
        self.assertEqual(key_finder.fingerprints(str(p8)), fps)

    def test_find_by_fingerprint_then_name(self):
        a = key_finder.KeyFile("/x/a.pem", "pem", {"aa:bb"})
        b = key_finder.KeyFile("/x/prod-key.pem", "pem")
        c = key_finder.KeyFile("/x/prod-key (1).pem", "pem")
        self.assertEqual(key_finder.find([a, b], "other", "aa:bb")[0], a)
        self.assertEqual(key_finder.find([b], "prod-key", None)[1], "matched by name")
        self.assertEqual(key_finder.find([c], "prod-key", None)[1], "similar name - please check")
        self.assertIsNone(key_finder.find([a], "zzz", "ff")[0])

    def test_classify(self):
        pem = self.home / "a.pem"
        pem.write_text("-----BEGIN RSA PRIVATE KEY-----\n" + "A" * 200 + "\n-----END RSA PRIVATE KEY-----\n")
        self.assertEqual(key_finder.classify(str(pem)), "pem")
        txt = self.home / "a.txt"
        txt.write_text(pem.read_text())
        self.assertIsNone(key_finder.classify(str(txt)))


class MiscTests(unittest.TestCase):
    def test_friendly_errors(self):
        self.assertIn("offline in SSM", aws_cli.friendly_error("An error occurred (TargetNotConnected)"))
        self.assertIn("expired", aws_cli.friendly_error("Token has expired and refresh failed"))
        self.assertIn("2201", aws_cli.friendly_error("bind: address already in use", 2201))

    def test_server_user_suggestion(self):
        self.assertEqual(DiscoveredServer(os_name="Ubuntu").suggested_user, "ubuntu")
        self.assertEqual(DiscoveredServer(platform="Windows").suggested_user, "Administrator")
        self.assertEqual(DiscoveredServer(os_name="Amazon Linux").suggested_user, "ec2-user")


if __name__ == "__main__":
    unittest.main()
