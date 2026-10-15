"""Set up an isolated NumPy environment when needed, then run the benchmark."""

import os
import subprocess
import sys
from pathlib import Path


# region Automatic setup

project_root = Path(__file__).resolve().parents[2]
benchmark_script = Path(__file__).with_name("benchmark.py")
requirements_file = Path(__file__).with_name("requirements.txt")
environment_folder = project_root / "Library" / "PythonBatchBenchmark" / "venv"

# Reuse the current Python installation when it already has NumPy.
numpy_check = subprocess.run(
    [sys.executable, "-c", "import numpy"],
    stdout=subprocess.DEVNULL,
    stderr=subprocess.DEVNULL,
    check=False,
)
python_executable = sys.executable

if numpy_check.returncode != 0:
    print("STATUS|Preparing a project-local Python environment", flush=True)
    environment_python = environment_folder / ("Scripts/python.exe" if os.name == "nt" else "bin/python")

    if not environment_python.exists():
        environment_folder.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run([sys.executable, "-m", "venv", str(environment_folder)], check=True)

    # Install NumPy only in this project's ignored Library folder.
    environment_numpy_check = subprocess.run(
        [str(environment_python), "-c", "import numpy"],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        check=False,
    )
    if environment_numpy_check.returncode != 0:
        print("STATUS|Installing NumPy for the Python comparison", flush=True)
        subprocess.run(
            [str(environment_python), "-m", "pip", "install", "-r", str(requirements_file)],
            check=True,
        )
    python_executable = str(environment_python)

# Forward Unity's item count, iterations, batch size, and run count to the benchmark.
benchmark_command = [python_executable, str(benchmark_script)] + sys.argv[1:]
exit_code = subprocess.run(benchmark_command, cwd=project_root, check=False).returncode
sys.exit(exit_code)

# endregion
