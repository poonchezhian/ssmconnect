# Copyright 2026 Poonchezhian Periasamy <poonchezhian@gmail.com>
# Licensed under the Apache License, Version 2.0 (see LICENSE).
"""SSMConnect for Linux: SSH and RDP to AWS servers over Session Manager, with port 22 closed."""

from ._version import __version__

APP_ID = "io.github.poonchezhian.SSMConnect"
APP_NAME = "SSMConnect"

__all__ = ["__version__", "APP_ID", "APP_NAME"]
