"""Logging helpers and sensitive-credential redaction filters."""

from __future__ import annotations

import logging
import re

_API_KEY_QUERY_RE = re.compile(r"([?&]api_key=)[^&\s\"'<>]+", re.IGNORECASE)


def redact_sensitive_text(text: str) -> str:
    """Redact API key query parameters from URLs, logs, and error messages."""
    return _API_KEY_QUERY_RE.sub(r"\1[REDACTED]", text)


class SensitiveQueryParamFilter(logging.Filter):
    """Logging filter masking sensitive query parameters such as api_key."""

    def filter(self, record: logging.LogRecord) -> bool:
        try:
            rendered = record.getMessage()
        except Exception:
            # Unformattable records are passed through untouched.
            rendered = None
        if rendered is not None:
            redacted = redact_sensitive_text(rendered)
            if redacted != rendered:
                record.msg = redacted
                record.args = ()
        return True


def configure_safe_http_logging() -> None:
    """Suppress verbose httpx INFO URL logs and redact api_key query params if enabled."""
    redaction_filter = SensitiveQueryParamFilter()
    for logger_name in ("httpx", "httpcore"):
        http_logger = logging.getLogger(logger_name)
        if not any(isinstance(f, SensitiveQueryParamFilter) for f in http_logger.filters):
            http_logger.addFilter(redaction_filter)
        if http_logger.level == logging.NOTSET or http_logger.level < logging.WARNING:
            http_logger.setLevel(logging.WARNING)
