"""Bounded, order-preserving parallel map used by the Pass 1 and Pass 2 stages."""

from __future__ import annotations

import contextvars
import threading
from collections.abc import Callable, Iterable
from concurrent.futures import Future, ThreadPoolExecutor, wait
from typing import TypeVar

T = TypeVar("T")
R = TypeVar("R")


class _AbortedError(RuntimeError):
    """Internal marker for tasks skipped because a sibling task already failed."""


def ordered_parallel_map(  # noqa: UP047 - keep Python 3.11 compatible TypeVar generics
    fn: Callable[[T], R],
    items: Iterable[T],
    max_workers: int,
    before_task: Callable[[], None] | None = None,
) -> list[R]:
    """Apply ``fn`` to every item with at most ``max_workers`` threads; results keep item order.

    With ``max_workers <= 1`` (or a single item) the items run sequentially on the calling
    thread, which reproduces the legacy sequential behavior exactly. When a task fails, tasks
    that have not started are skipped and the earliest failure by item order is raised, so the
    outcome is deterministic. ``before_task`` runs at the start of each task (used for
    cooperative cancellation checks) and may raise.

    Each task runs in a copy of the caller's ``contextvars`` context, so job-scoped state (such
    as the Gemini job scope) is visible to worker threads.
    """
    materialized = list(items)
    if max_workers <= 1 or len(materialized) <= 1:
        return _run_sequentially(fn, materialized, before_task)

    abort = threading.Event()

    def guarded(item: T) -> R:
        if abort.is_set():
            raise _AbortedError("Skipped because another task failed.")
        try:
            if before_task is not None:
                before_task()
            return fn(item)
        except BaseException:
            # Signal siblings immediately instead of waiting for the done-callback.
            abort.set()
            raise

    futures: list[Future[R]] = []
    with ThreadPoolExecutor(
        max_workers=min(max_workers, len(materialized)),
        thread_name_prefix="va-parallel",
    ) as pool:
        for item in materialized:
            futures.append(pool.submit(contextvars.copy_context().run, guarded, item))
        wait(futures)

    return _collect_results(futures)


def _collect_results(futures: list[Future[R]]) -> list[R]:  # noqa: UP047
    """Return results in item order, raising the earliest real (non-skip) failure."""
    for future in futures:
        error = None if future.cancelled() else future.exception()
        if error is not None and not isinstance(error, _AbortedError):
            raise error
    return [future.result() for future in futures]


def _run_sequentially(  # noqa: UP047
    fn: Callable[[T], R],
    items: list[T],
    before_task: Callable[[], None] | None,
) -> list[R]:
    results: list[R] = []
    for item in items:
        if before_task is not None:
            before_task()
        results.append(fn(item))
    return results
