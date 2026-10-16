"""Compare local Python processing methods with every Unity example workload.

The Unity dashboard starts this script after measuring Unity, so the Python and
Unity runs do not compete for CPU at the same time. Each workload keeps the
same inputs and math across Python single-thread, threads, processes, and NumPy.
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


# region Repeatable inputs and shared helpers

def stable_value(value):
    """Match the deterministic 32-bit hash used by the Unity optimization examples."""
    number = value & 0xFFFFFFFF
    number ^= number >> 16
    number = (number * 0x7FEB352D) & 0xFFFFFFFF
    number ^= number >> 15
    number = (number * 0x846CA68B) & 0xFFFFFFFF
    number ^= number >> 16
    return (number & 0x00FFFFFF) / 16777216.0


def to_range(value):
    return value * 10.0 - 5.0


def chunks(item_count, batch_size):
    return [(start, min(start + batch_size, item_count))
            for start in range(0, item_count, batch_size)]


def make_executor(method, worker_count):
    if method == "threads":
        return concurrent.futures.ThreadPoolExecutor(max_workers=worker_count)
    if method == "processes":
        return concurrent.futures.ProcessPoolExecutor(max_workers=worker_count)
    return None


def run_chunks(method, pool, ranges, worker, shared_data):
    """Run one batch at a time, or schedule the same batches on a worker pool."""
    if method == "single":
        return [worker(start, end, shared_data) for start, end in ranges]
    futures = [pool.submit(worker, start, end, shared_data) for start, end in ranges]
    return [future.result() for future in futures]


def summarize(values):
    ordered = sorted(values)
    p95_index = max(0, math.ceil(len(ordered) * 0.95) - 1)
    return {
        "average": statistics.fmean(values),
        "median": statistics.median(values),
        "p95": ordered[p95_index],
    }


# endregion


# region Simple iteration workload

def simple_batch(start, end, data):
    values, iterations = data
    for index in range(start, end):
        value = values[index]
        for _ in range(iterations):
            value = value * 1.00001 + 0.00002
            if value >= 1.0:
                value -= 1.0
        values[index] = value
    return start, end


def simple_process_batch(batch):
    """Process workers receive only their own small item slice."""
    start, values, iterations = batch
    for index in range(len(values)):
        value = values[index]
        for _ in range(iterations):
            value = value * 1.00001 + 0.00002
            if value >= 1.0:
                value -= 1.0
        values[index] = value
    return start, values


def simple_numpy(values, masks, options):
    one = numpy.float32(1.0)
    multiplier = numpy.float32(1.00001)
    addition = numpy.float32(0.00002)
    for start, end in chunks(options.items, options.batch_size):
        batch_values = values[start:end]
        batch_mask = masks[start:end]
        for _ in range(options.iterations):
            numpy.multiply(batch_values, multiplier, out=batch_values)
            numpy.add(batch_values, addition, out=batch_values)
            numpy.greater_equal(batch_values, one, out=batch_mask)
            numpy.subtract(batch_values, one, out=batch_values, where=batch_mask)
    return float(numpy.sum(values, dtype=numpy.float64))


def initial_simple_values(item_count):
    return [((index * 104729) % 1000003) / 1000003.0 for index in range(item_count)]


# endregion


# region Gravity workload

def make_gravity_inputs(ball_count, dimension):
    positions = []
    masses = []
    random_state = 0xC0FFEE
    for index in range(ball_count):
        y = 1.0 - (index / max(1, ball_count - 1)) * 2.0
        radius = math.sqrt(max(0.0, 1.0 - y * y))
        angle = index * 2.39996323
        if dimension == "2D":
            direction = (math.cos(angle), math.sin(angle), 0.0)
        else:
            direction = (math.cos(angle) * radius, y, math.sin(angle) * radius)
        random_state = (random_state * 1664525 + 1013904223) & 0xFFFFFFFF
        random_radius = (random_state & 0x00FFFFFF) / 16777216.0
        distance = 2.0 + random_radius * 4.5
        positions.append(tuple(component * distance for component in direction))
        masses.append(0.7 + (index % 5) * 0.15)
    return positions, masses


def gravity_batch(start, end, data):
    # Formula: a_i = G * sum(m_j * (p_j - p_i) / (|p_j - p_i|^2 + epsilon^2)^(3/2)).
    # Every ball checks every other ball, so each step is O(N^2) work and O(N) stored positions.
    positions, masses, gravity, softening, is_2d = data
    accelerations = []
    for target in range(start, end):
        x_total = 0.0
        y_total = 0.0
        z_total = 0.0
        target_x, target_y, target_z = positions[target]
        for source in range(len(positions)):
            if source == target:
                continue
            offset_x = positions[source][0] - target_x
            offset_y = positions[source][1] - target_y
            offset_z = 0.0 if is_2d else positions[source][2] - target_z
            distance_squared = offset_x * offset_x + offset_y * offset_y + offset_z * offset_z + softening * softening
            inverse_distance = 1.0 / math.sqrt(distance_squared)
            strength = gravity * masses[source] * inverse_distance * inverse_distance * inverse_distance
            x_total += offset_x * strength
            y_total += offset_y * strength
            z_total += offset_z * strength
        accelerations.append((x_total, y_total, z_total))
    return start, accelerations


def gravity_numpy(positions, masses, options):
    position_array = numpy.asarray(positions, dtype=numpy.float32)[:, :options.dimension_count]
    mass_array = numpy.asarray(masses, dtype=numpy.float32)
    output = numpy.zeros_like(position_array)
    # Reuse every temporary array so NumPy does not create an N x N matrix for each axis.
    offsets = numpy.empty((options.batch_size, options.dimension_count), dtype=numpy.float32)
    squared_offsets = numpy.empty_like(offsets)
    distances = numpy.empty(options.batch_size, dtype=numpy.float32)
    inverse_cubed = numpy.empty(options.batch_size, dtype=numpy.float32)
    axis_offsets = numpy.empty(options.batch_size, dtype=numpy.float32)
    weighted_offsets = numpy.empty(options.batch_size, dtype=numpy.float32)
    strengths = numpy.empty((options.batch_size, options.balls), dtype=numpy.float32)
    for start, end in chunks(options.balls, options.batch_size):
        count = end - start
        target_positions = position_array[start:end]
        target_offsets = offsets[:count]
        target_squares = squared_offsets[:count]
        target_distances = distances[:count]
        target_inverse_cubed = inverse_cubed[:count]
        target_axis_offsets = axis_offsets[:count]
        target_weighted_offsets = weighted_offsets[:count]
        target_strengths = strengths[:count]
        for target in range(options.balls):
            numpy.subtract(position_array[target], target_positions, out=target_offsets)
            numpy.multiply(target_offsets, target_offsets, out=target_squares)
            numpy.sum(target_squares, axis=1, out=target_distances)
            numpy.add(target_distances, numpy.float32(options.softening * options.softening), out=target_distances)
            # Change distance squared into inverse distance, then raise it to the third power.
            numpy.sqrt(target_distances, out=target_distances)
            numpy.reciprocal(target_distances, out=target_distances)
            numpy.square(target_distances, out=target_inverse_cubed)
            numpy.multiply(target_inverse_cubed, target_distances, out=target_inverse_cubed)
            numpy.multiply(target_inverse_cubed, mass_array[target] * options.gravity, out=target_inverse_cubed)
            target_strengths[:, target] = target_inverse_cubed
            if start <= target < end:
                target_strengths[target - start, target] = 0.0
        for source in range(options.balls):
            for axis in range(options.dimension_count):
                numpy.subtract(position_array[source, axis], target_positions[:, axis], out=target_axis_offsets)
                numpy.multiply(target_axis_offsets, target_strengths[:, source], out=target_weighted_offsets)
                numpy.add(output[start:end, axis], target_weighted_offsets, out=output[start:end, axis])
    return output


# endregion


# region Random Search workload

def random_search_batch(start, end, data):
    # Sphere objective: score = x^2 + y^2. The known minimum is (0, 0), with score 0.
    trial, seed = data
    best_score = float("inf")
    best_x = 0.0
    best_y = 0.0
    for index in range(start, end):
        x = to_range(stable_value(index * 2 + trial * 100003 + seed))
        y = to_range(stable_value(index * 2 + 1 + trial * 100003 + seed))
        score = x * x + y * y
        if score < best_score:
            best_score = score
            best_x = x
            best_y = y
    return best_score, best_x, best_y


def random_search_numpy(options, trial):
    index = numpy.arange(options.evaluations, dtype=numpy.uint32)
    mask = numpy.uint32(0xFFFFFFFF)
    first = (index * numpy.uint32(2) + numpy.uint32(trial * 100003 + options.seed)) & mask
    second = first + numpy.uint32(1)
    first ^= first >> numpy.uint32(16)
    first = (first * numpy.uint32(0x7FEB352D)) & mask
    first ^= first >> numpy.uint32(15)
    first = (first * numpy.uint32(0x846CA68B)) & mask
    first ^= first >> numpy.uint32(16)
    second ^= second >> numpy.uint32(16)
    second = (second * numpy.uint32(0x7FEB352D)) & mask
    second ^= second >> numpy.uint32(15)
    second = (second * numpy.uint32(0x846CA68B)) & mask
    second ^= second >> numpy.uint32(16)
    x = ((first & numpy.uint32(0x00FFFFFF)).astype(numpy.float32) / numpy.float32(16777216.0)) * 10.0 - 5.0
    y = ((second & numpy.uint32(0x00FFFFFF)).astype(numpy.float32) / numpy.float32(16777216.0)) * 10.0 - 5.0
    scores = x * x + y * y
    best = int(numpy.argmin(scores))
    return float(scores[best]), float(x[best]), float(y[best])


# endregion


# region Particle Swarm workload

def pso_batch(start, end, data):
    # Velocity: v = 0.7v + 1.4r1(pBest - x) + 1.4r2(gBest - x); then x = clamp(x + v).
    # One iteration checks O(P) particles; all iteration work is O(P * I).
    arrays, controls = data
    x, y, vx, vy, personal_x, personal_y = arrays
    iteration, trial, seed, global_x, global_y = controls
    next_values = []
    for particle in range(start, end):
        r1 = stable_value(particle * 4 + iteration * 7919 + trial * 100003 + seed)
        r2 = stable_value(particle * 4 + 1 + iteration * 7919 + trial * 100003 + seed)
        r3 = stable_value(particle * 4 + 2 + iteration * 7919 + trial * 100003 + seed)
        r4 = stable_value(particle * 4 + 3 + iteration * 7919 + trial * 100003 + seed)
        vx[particle] = 0.7 * vx[particle] + 1.4 * r1 * (personal_x[particle] - x[particle]) + 1.4 * r2 * (global_x - x[particle])
        vy[particle] = 0.7 * vy[particle] + 1.4 * r3 * (personal_y[particle] - y[particle]) + 1.4 * r4 * (global_y - y[particle])
        x[particle] = min(5.0, max(-5.0, x[particle] + vx[particle]))
        y[particle] = min(5.0, max(-5.0, y[particle] + vy[particle]))
        next_values.append((x[particle], y[particle], vx[particle], vy[particle],
                            x[particle] * x[particle] + y[particle] * y[particle]))
    return start, next_values


def pso_numpy(options, trial):
    count = options.particles
    indexes = numpy.arange(count, dtype=numpy.uint32)
    x = numpy.empty(count, dtype=numpy.float32)
    y = numpy.empty(count, dtype=numpy.float32)
    vx = numpy.empty(count, dtype=numpy.float32)
    vy = numpy.empty(count, dtype=numpy.float32)
    personal_x = numpy.empty(count, dtype=numpy.float32)
    personal_y = numpy.empty(count, dtype=numpy.float32)
    personal_score = numpy.empty(count, dtype=numpy.float32)
    seed_base = numpy.uint32(trial * 100003 + options.seed)

    def values_for(offset):
        value = (indexes * numpy.uint32(4) + numpy.uint32(offset) + seed_base) & numpy.uint32(0xFFFFFFFF)
        value ^= value >> numpy.uint32(16)
        value = (value * numpy.uint32(0x7FEB352D)) & numpy.uint32(0xFFFFFFFF)
        value ^= value >> numpy.uint32(15)
        value = (value * numpy.uint32(0x846CA68B)) & numpy.uint32(0xFFFFFFFF)
        value ^= value >> numpy.uint32(16)
        return (value & numpy.uint32(0x00FFFFFF)).astype(numpy.float32) / numpy.float32(16777216.0)

    x[:] = values_for(0) * 10.0 - 5.0
    y[:] = values_for(1) * 10.0 - 5.0
    vx[:] = values_for(2) - 0.5
    vy[:] = values_for(3) - 0.5
    personal_x[:] = x
    personal_y[:] = y
    numpy.multiply(x, x, out=personal_score)
    personal_score += y * y
    best_index = int(numpy.argmin(personal_score))
    global_x = float(x[best_index])
    global_y = float(y[best_index])
    global_score = float(personal_score[best_index])

    for iteration in range(options.iterations):
        pull1 = values_for(iteration * 7919)
        pull2 = values_for(iteration * 7919 + 1)
        pull3 = values_for(iteration * 7919 + 2)
        pull4 = values_for(iteration * 7919 + 3)
        vx[:] = 0.7 * vx + 1.4 * pull1 * (personal_x - x) + 1.4 * pull2 * (global_x - x)
        vy[:] = 0.7 * vy + 1.4 * pull3 * (personal_y - y) + 1.4 * pull4 * (global_y - y)
        numpy.clip(x + vx, -5.0, 5.0, out=x)
        numpy.clip(y + vy, -5.0, 5.0, out=y)
        scores = x * x + y * y
        improved = scores < personal_score
        personal_score[improved] = scores[improved]
        personal_x[improved] = x[improved]
        personal_y[improved] = y[improved]
        candidate = int(numpy.argmin(scores))
        if scores[candidate] < global_score:
            global_score = float(scores[candidate])
            global_x = float(x[candidate])
            global_y = float(y[candidate])
    return global_score, global_x, global_y


# endregion


# region One workload benchmark

def execute_simple(method, pool, options):
    values = initial_simple_values(options.items)
    if method == "numpy":
        array = numpy.asarray(values, dtype=numpy.float32)
        masks = numpy.empty(options.items, dtype=numpy.bool_)
        simple_numpy(array, masks, options)
        return array
    jobs = chunks(options.items, options.batch_size)
    if method == "processes":
        futures = [pool.submit(simple_process_batch,
            (start, values[start:end], options.iterations)) for start, end in jobs]
        for future in futures:
            start, finished_values = future.result()
            values[start:start + len(finished_values)] = finished_values
    else:
        run_chunks(method, pool, jobs, simple_batch, (values, options.iterations))
    return values


def execute_gravity(method, pool, options, gravity_inputs):
    positions, masses = gravity_inputs
    settings = (positions, masses, options.gravity, options.softening, options.dimension == "2D")
    if method == "numpy":
        return gravity_numpy(positions, masses, options)
    results = run_chunks(method, pool, chunks(options.balls, options.batch_size), gravity_batch, settings)
    # A checksum keeps result writes observable without adding work to the timed gravity pass.
    return results


def checksum_for_workload(workload, method, output):
    """Keep result validation outside compute time, matching Unity's checksum timing."""
    if workload == "simple-iteration":
        return float(numpy.sum(output, dtype=numpy.float64)) if method == "numpy" else sum(output)
    if workload == "gravity":
        if method == "numpy":
            return float(numpy.sum(output, dtype=numpy.float64))
        return sum(component for _, batch_values in output for row in batch_values for component in row)
    return float(output)


def execute_random_search(method, pool, options):
    overall_best = float("inf")
    overall_x = 0.0
    overall_y = 0.0
    for trial in range(options.trials):
        if method == "numpy":
            best_score, best_x, best_y = random_search_numpy(options, trial)
        else:
            task_data = (trial, options.seed)
            results = run_chunks(method, pool, chunks(options.evaluations, options.batch_size), random_search_batch, task_data)
            best_score, best_x, best_y = min(results)
        if best_score < overall_best:
            overall_best, overall_x, overall_y = best_score, best_x, best_y
    return overall_best + overall_x + overall_y


def execute_pso(method, pool, options):
    total_best = 0.0
    checksum = 0.0
    for trial in range(options.trials):
        if method == "numpy":
            trial_best, trial_x, trial_y = pso_numpy(options, trial)
            total_best += trial_best
            checksum += trial_best + trial_x + trial_y
            continue
        count = options.particles
        x = [to_range(stable_value(particle * 4 + trial * 100003 + options.seed)) for particle in range(count)]
        y = [to_range(stable_value(particle * 4 + 1 + trial * 100003 + options.seed)) for particle in range(count)]
        vx = [stable_value(particle * 4 + 2 + trial * 100003 + options.seed) - 0.5 for particle in range(count)]
        vy = [stable_value(particle * 4 + 3 + trial * 100003 + options.seed) - 0.5 for particle in range(count)]
        personal_x = list(x)
        personal_y = list(y)
        personal_score = [x[index] * x[index] + y[index] * y[index] for index in range(count)]
        best_index = min(range(count), key=personal_score.__getitem__)
        global_x = x[best_index]
        global_y = y[best_index]
        global_score = personal_score[best_index]

        for iteration in range(options.iterations):
            arrays = (x, y, vx, vy, personal_x, personal_y)
            controls = (iteration, trial, options.seed, global_x, global_y)
            results = run_chunks(method, pool, chunks(count, options.batch_size), pso_batch, (arrays, controls))
            scores = [0.0] * count
            for start, batch_values in results:
                for offset, values in enumerate(batch_values):
                    particle = start + offset
                    x[particle], y[particle], vx[particle], vy[particle], scores[particle] = values
            for particle, score in enumerate(scores):
                if score < personal_score[particle]:
                    personal_score[particle] = score
                    personal_x[particle] = x[particle]
                    personal_y[particle] = y[particle]
                if score < global_score:
                    global_score = score
                    global_x = x[particle]
                    global_y = y[particle]
        total_best += global_score
        checksum += global_score + global_x + global_y
    return checksum + total_best / max(1, options.trials)


def benchmark_workload(options):
    methods = [
        ("Python single threaded", "single"),
        ("Python threads", "threads"),
        ("Python processes", "processes"),
    ]
    if numpy is not None:
        methods.append(("Python NumPy vectorized", "numpy"))
    item_count = options.balls if options.workload == "gravity" else (
        options.items if options.workload == "simple-iteration" else options.particles if options.workload == "pso" else options.evaluations)
    worker_count = options.workers or min(8, max(1, os.cpu_count() or 1))
    # Small physics and swarm arrays still need several tasks to exercise worker scheduling.
    if options.workload == "gravity":
        options.batch_size = max(1, math.ceil(options.balls / (worker_count * 4)))
    elif options.workload == "pso":
        options.batch_size = max(1, math.ceil(options.particles / worker_count))
    worker_count = min(worker_count, max(1, math.ceil(item_count / options.batch_size)))
    gravity_inputs = make_gravity_inputs(options.balls, options.dimension) if options.workload == "gravity" else None
    results = []

    print("STATUS|Python environment ready", flush=True)
    for display_name, method in methods:
        compute_times = []
        wall_times = []
        checksum = 0.0
        pool = make_executor(method, worker_count)
        try:
            for pass_index in range(options.runs + 1):
                started_at = time.perf_counter()
                if options.workload == "simple-iteration":
                    output = execute_simple(method, pool, options)
                elif options.workload == "gravity":
                    output = execute_gravity(method, pool, options, gravity_inputs)
                elif options.workload == "pso":
                    output = execute_pso(method, pool, options)
                else:
                    output = execute_random_search(method, pool, options)
                compute_ms = (time.perf_counter() - started_at) * 1000.0
                checksum = checksum_for_workload(options.workload, method, output)
                wall_ms = (time.perf_counter() - started_at) * 1000.0
                if pass_index > 0:
                    compute_times.append(compute_ms)
                    wall_times.append(wall_ms)
        finally:
            if pool is not None:
                pool.shutdown(wait=True)

        compute_summary = summarize(compute_times)
        wall_summary = summarize(wall_times)
        results.append({
            "algorithm": options.algorithm_name,
            "strategy": display_name,
            "source": "Python",
            "balls": options.balls if options.workload == "gravity" else 0,
            "workItems": options.items if options.workload == "simple-iteration" else 0,
            "iterations": options.iterations if options.workload in ("simple-iteration", "pso") else 0,
            "batchSize": options.batch_size,
            "evaluationCount": options.evaluations * options.trials if options.workload == "random-search" else (
                options.particles * (options.iterations + 1) * options.trials if options.workload == "pso" else 0),
            "particleCount": options.particles if options.workload == "pso" else 0,
            "trialCount": options.trials if options.workload in ("pso", "random-search") else 0,
            "sampleCount": options.runs * options.trials if options.workload in ("pso", "random-search") else options.runs,
            "averageProfileMs": compute_summary["average"],
            "medianProfileMs": compute_summary["median"],
            "p95ProfileMs": compute_summary["p95"],
            "averageFrameMs": wall_summary["average"],
            "p95FrameMs": wall_summary["p95"],
            "checksum": checksum,
        })
        print("{0}: {1:.3f} ms average ({2} measured runs)".format(
            display_name, compute_summary["average"], len(compute_times)))
        print("METHOD_DONE|{0}".format(display_name), flush=True)

    return worker_count, results


# endregion


# region Report and command-line settings

def main():
    project_root = Path(__file__).resolve().parents[2]
    default_output = project_root / "Library" / "PythonBatchBenchmark" / "results.json"
    parser = argparse.ArgumentParser(description="Compare Python methods with every Unity example workload.")
    parser.add_argument("--workload", choices=("simple-iteration", "gravity", "pso", "random-search"), default="simple-iteration")
    parser.add_argument("--dimension", choices=("2D", "3D"), default="3D")
    parser.add_argument("--algorithm-name", default="")
    parser.add_argument("--items", type=int, default=100000)
    parser.add_argument("--iterations", type=int, default=100)
    parser.add_argument("--batch-size", type=int, default=1000)
    parser.add_argument("--balls", type=int, default=128)
    parser.add_argument("--particles", type=int, default=64)
    parser.add_argument("--evaluations", type=int, default=6400, help="Random Search evaluations in each trial.")
    parser.add_argument("--trials", type=int, default=5)
    parser.add_argument("--seed", type=int, default=2468)
    parser.add_argument("--gravity", type=float, default=1.15)
    parser.add_argument("--softening", type=float, default=0.2)
    parser.add_argument("--runs", type=int, default=5)
    parser.add_argument("--workers", type=int, default=0)
    parser.add_argument("--output", type=Path, default=default_output)
    options = parser.parse_args()
    if min(options.items, options.iterations, options.batch_size, options.balls,
           options.particles, options.evaluations, options.trials, options.runs) < 1:
        parser.error("workload sizes, iterations, trials, and runs must be at least 1")
    if options.workers < 0:
        parser.error("workers cannot be negative")
    options.dimension_count = 2 if options.dimension == "2D" else 3

    worker_count, results = benchmark_workload(options)
    report = {
        "schemaVersion": 2,
        "workload": options.workload,
        "dimension": options.dimension if options.workload == "gravity" else "",
        "algorithm": options.algorithm_name,
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
            "balls": options.balls,
            "particles": options.particles,
            "evaluations": options.evaluations,
            "trials": options.trials,
            "measuredRuns": options.runs,
        },
        "results": results,
    }
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("Report saved to: {0}".format(options.output.resolve()))
    print("The Unity dashboard reads this report and adds its methods beside the matching Unity results.")


# endregion


if __name__ == "__main__":
    main()
