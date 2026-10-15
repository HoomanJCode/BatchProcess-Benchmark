"""Run local Python batch benchmarks for the Unity Simple Iteration example.

The calculation is the same simple loop as Unity. Python's built-in float is
64-bit, while the Unity example stores 32-bit floats, so checksums can differ.

Run from the project root:
    py Tools/PythonBatchBenchmark/benchmark.py

The JSON report is written under Library/ and can be loaded in the dashboard.
"""

import argparse
import concurrent.futures
import json
import math
import os
import platform
import statistics
import time
from pathlib import Path

try:
    import numpy
except ImportError:
    numpy = None


# region Shared batch code

def process_batch(values, start, end, iterations):
    """Run the same multiply, add, and wrap calculation for one batch."""
    # Formula per iteration: value = value * 1.00001 + 0.00002; wrap by subtracting 1 at 1.
    # Complexity: O(N * I) calculation work, where N is the batch item count and I the loops.
    for index in range(start, end):
        value = values[index]
        for step in range(iterations):
            value = value * 1.00001 + 0.00002
            if value >= 1.0:
                value -= 1.0
        values[index] = value


def process_worker(batch):
    """ProcessPoolExecutor needs a top-level function it can send to a process."""
    start, values, iterations = batch
    process_batch(values, 0, len(values), iterations)
    return start, values


# endregion


# region Benchmark setup and methods

def main():
    project_root = Path(__file__).resolve().parents[2]
    default_output = project_root / "Library" / "PythonBatchBenchmark" / "results.json"

    parser = argparse.ArgumentParser(description="Compare Python methods for the Unity Simple Iteration workload.")
    parser.add_argument("--items", type=int, default=100000, help="Number of values (default: 100000).")
    parser.add_argument("--iterations", type=int, default=100, help="Loops per value (default: 100).")
    parser.add_argument("--batch-size", type=int, default=1000, help="Items in each task (default: 1000).")
    parser.add_argument("--runs", type=int, default=5, help="Measured runs after one warm-up (default: 5).")
    parser.add_argument("--workers", type=int, default=0, help="Thread/process workers; 0 uses up to 8 CPUs.")
    parser.add_argument("--output", type=Path, default=default_output, help="JSON report path.")
    options = parser.parse_args()

    if min(options.items, options.iterations, options.batch_size, options.runs) < 1:
        parser.error("items, iterations, batch-size, and runs must all be at least 1")
    if options.workers < 0:
        parser.error("workers cannot be negative")

    # Use no more workers than there are batches, and avoid launching too many processes.
    worker_count = options.workers or min(8, max(1, os.cpu_count() or 1))
    worker_count = min(worker_count, math.ceil(options.items / options.batch_size))

    methods = [
        ("Python single threaded", "single"),
        ("Python threads", "threads"),
        ("Python processes", "processes"),
    ]
    if numpy is not None:
        methods.append(("Python NumPy vectorized", "numpy"))

    # NumPy arrays and the reusable mask are created before timing begins.
    if numpy is not None:
        numpy_initial = numpy.fromiter(
            (((index * 104729) % 1000003) / 1000003.0 for index in range(options.items)),
            dtype=numpy.float32,
            count=options.items,
        )
        numpy_values = numpy.empty_like(numpy_initial)
        numpy_masks = numpy.empty(options.items, dtype=numpy.bool_)
        numpy_one = numpy.float32(1.0)
        numpy_multiplier = numpy.float32(1.00001)
        numpy_addition = numpy.float32(0.00002)

    print("STATUS|Python environment ready", flush=True)

    all_results = []

    # Keep workers alive for warm-up and measured runs; their startup is not timed.
    with concurrent.futures.ThreadPoolExecutor(max_workers=worker_count) as thread_pool:
        with concurrent.futures.ProcessPoolExecutor(max_workers=worker_count) as process_pool:
            for method_name, method_kind in methods:
                measured_compute_times = []
                measured_wall_times = []
                checksum = 0.0

                # One pass warms up the method. It is left out of the measured averages.
                for pass_index in range(options.runs + 1):
                    if method_kind == "numpy":
                        numpy.copyto(numpy_values, numpy_initial)
                    else:
                        values = [((index * 104729) % 1000003) / 1000003.0
                                  for index in range(options.items)]

                    started_at = time.perf_counter()

                    if method_kind == "single":
                        # Work through each batch in sequence on this Python thread.
                        for batch_start in range(0, options.items, options.batch_size):
                            batch_end = min(batch_start + options.batch_size, options.items)
                            process_batch(values, batch_start, batch_end, options.iterations)

                    elif method_kind == "threads":
                        # Start one thread task for every batch. CPU-bound Python work
                        # usually cannot run in parallel under the standard GIL build.
                        futures = []
                        for batch_start in range(0, options.items, options.batch_size):
                            batch_end = min(batch_start + options.batch_size, options.items)
                            futures.append(thread_pool.submit(process_batch, values,
                                batch_start, batch_end, options.iterations))
                        for future in futures:
                            future.result()

                    elif method_kind == "processes":
                        # Processes can use multiple CPU cores. Batch values are copied
                        # to and from workers, so that transfer cost is timed too.
                        futures = []
                        for batch_start in range(0, options.items, options.batch_size):
                            batch_end = min(batch_start + options.batch_size, options.items)
                            batch_values = values[batch_start:batch_end]
                            futures.append(process_pool.submit(process_worker,
                                (batch_start, batch_values, options.iterations)))
                        for future in futures:
                            batch_start, finished_values = future.result()
                            values[batch_start:batch_start + len(finished_values)] = finished_values

                    else:
                        # NumPy runs native float32 array operations and reuses its arrays.
                        for batch_start in range(0, options.items, options.batch_size):
                            batch_end = min(batch_start + options.batch_size, options.items)
                            batch_values = numpy_values[batch_start:batch_end]
                            batch_mask = numpy_masks[batch_start:batch_end]
                            for step in range(options.iterations):
                                numpy.multiply(batch_values, numpy_multiplier, out=batch_values)
                                numpy.add(batch_values, numpy_addition, out=batch_values)
                                numpy.greater_equal(batch_values, numpy_one, out=batch_mask)
                                numpy.subtract(batch_values, numpy_one, out=batch_values, where=batch_mask)

                    compute_ms = (time.perf_counter() - started_at) * 1000.0

                    # Match Unity's example: calculate the checksum after the timer stops.
                    if method_kind == "numpy":
                        checksum = float(numpy.sum(numpy_values, dtype=numpy.float64))
                    else:
                        checksum = sum(values)
                    wall_ms = (time.perf_counter() - started_at) * 1000.0
                    if pass_index > 0:
                        measured_compute_times.append(compute_ms)
                        measured_wall_times.append(wall_ms)

                sorted_compute_times = sorted(measured_compute_times)
                sorted_wall_times = sorted(measured_wall_times)
                average_compute_ms = statistics.fmean(measured_compute_times)
                average_wall_ms = statistics.fmean(measured_wall_times)
                p95_index = max(0, math.ceil(len(sorted_compute_times) * 0.95) - 1)
                p95_compute_ms = sorted_compute_times[p95_index]
                p95_wall_ms = sorted_wall_times[p95_index]
                all_results.append({
                    "strategy": method_name,
                    "source": "Python",
                    "workItems": options.items,
                    "iterations": options.iterations,
                    "batchSize": options.batch_size,
                    "sampleCount": len(measured_compute_times),
                    "averageProfileMs": average_compute_ms,
                    "medianProfileMs": statistics.median(measured_compute_times),
                    "p95ProfileMs": p95_compute_ms,
                    # Python has no Unity frames; wall time includes checksum work.
                    "averageFrameMs": average_wall_ms,
                    "p95FrameMs": p95_wall_ms,
                    "checksum": checksum,
                })
                print("{0}: {1:.3f} ms average ({2} measured runs)".format(
                    method_name, average_compute_ms, len(measured_compute_times)))
                print("METHOD_DONE|{0}".format(method_name), flush=True)

    # Save a small report which the local dashboard can load from the Simple Iteration tab.
    report = {
        "schemaVersion": 1,
        "workload": "simple-iteration",
        "runtime": {
            "language": "Python",
            "version": platform.python_version(),
            "implementation": platform.python_implementation(),
            "numpyVersion": numpy.__version__ if numpy is not None else "not installed",
            "workerCount": worker_count,
        },
        "settings": {
            "workItems": options.items,
            "iterations": options.iterations,
            "batchSize": options.batch_size,
            "measuredRuns": options.runs,
        },
        "results": all_results,
    }
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("Report saved to: {0}".format(options.output.resolve()))
    print("Unity starts this runner automatically and reads the report when the comparison finishes.")


# endregion


if __name__ == "__main__":
    main()
