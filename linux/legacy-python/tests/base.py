import os
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))


class IsolatedHome(unittest.TestCase):
    """Every test gets its own HOME so nothing touches the real ~/.aws, ~/.ssh or ~/.config."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.home = Path(self._tmp.name)
        self._saved = dict(os.environ)
        os.environ["HOME"] = str(self.home)
        for v in ("XDG_CONFIG_HOME", "XDG_STATE_HOME", "XDG_DATA_HOME", "AWS_CONFIG_FILE", "AWS_SHARED_CREDENTIALS_FILE"):
            os.environ.pop(v, None)

    def tearDown(self):
        os.environ.clear()
        os.environ.update(self._saved)
        self._tmp.cleanup()
