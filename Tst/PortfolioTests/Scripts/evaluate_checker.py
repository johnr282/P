#!/usr/bin/env python3
"""Repeatedly run P Checker strategies and summarize time to first bug."""

import argparse
import statistics
import subprocess
import sys
import time
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
PROJECT_FILE = REPO_ROOT / "Src" / "PCompiler" / "PCommandLine" / "PCommandLine.csproj"
STRATEGIES = ("--sch-random", "--sch-monitorguided")


def parse_summary(path: Path) -> dict[str, float]:
    values = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if ":" not in line:
            continue
        key, value = line.split(":", 1)
        try:
            values[key.strip()] = float(value.strip())
        except ValueError:
            continue
    missing = {"bugs", "schedules", "time_seconds"} - values.keys()
    if missing:
        raise ValueError(f"{path} is missing fields: {', '.join(sorted(missing))}")
    return values


def newest_summary(output_root: Path, started_at: float) -> Path:
    candidates = [
        path for path in (output_root / "BugFinding").rglob("*_summary.txt")
        if path.stat().st_mtime >= started_at - 2
    ]
    if not candidates:
        raise FileNotFoundError(
            f"No new *_summary.txt found under {output_root / 'BugFinding'}"
        )
    return max(candidates, key=lambda path: path.stat().st_mtime)


def summarize(label: str, values: list[float]) -> str:
    if not values:
        return f"  {label}: no runs found a bug"
    return (
        f"  {label}: mean={statistics.mean(values):.3f}, "
        f"median={statistics.median(values):.3f}, max={max(values):.3f}, "
        f"min={min(values):.3f}"
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", type=Path, required=True,
                        help="Path to the compiled P test DLL")
    parser.add_argument("--output-parent", type=Path, required=True,
                        help="Directory containing (or where to create) PCheckerOutput")
    parser.add_argument("--test-case", required=True,
                        help="P test case name, for example tcOneLeaderElection1")
    parser.add_argument("--runs", type=int, required=True,
                        help="Number of runs per strategy")
    parser.add_argument("--timeout", type=int, required=True,
                        help="Per-run timeout in seconds")
    parser.add_argument("--schedules", type=int, required=True,
                        help="Schedules per run")
    parser.add_argument("--max-steps", type=int, required=True,
                        help="Maximum steps per schedule")
    args = parser.parse_args()

    dll = args.dll.expanduser().resolve()
    output_parent = args.output_parent.expanduser().resolve()
    if not dll.is_file():
        parser.error(f"DLL does not exist: {dll}")
    if not PROJECT_FILE.is_file():
        parser.error(f"P source project not found: {PROJECT_FILE}")
    if args.runs < 1 or args.timeout < 1 or args.schedules < 1 or args.max_steps < 1:
        parser.error("runs, timeout, schedules, and max_steps must all be positive")
    output_parent.mkdir(parents=True, exist_ok=True)

    # Build once from this checkout. Subsequent executions use the built source output.
    build = subprocess.run(
        ["dotnet", "build", str(PROJECT_FILE), "--configuration", "Release"],
        cwd=REPO_ROOT, text=True,
    )
    if build.returncode:
        return build.returncode
    all_results = {}
    for strategy in STRATEGIES:
        bug_schedules: list[float] = []
        bug_times: list[float] = []
        never_found = 0
        print(f"\n{strategy}: {args.runs} runs")
        for run_index in range(1, args.runs + 1):
            started_at = time.time()
            # Give each invocation its own output folder. P Checker rotates
            # up to ten prior runs when reusing an output directory; isolated
            # folders avoid that history management and preserve every run.
            run_output = (
                output_parent / "PCheckerOutput" /
                strategy.removeprefix("--") / f"run_{run_index:03d}"
            )
            command = [
                "dotnet", "run", "--project", str(PROJECT_FILE),
                "--configuration", "Release", "--no-build", "--", "check", str(dll),
                "-tc", args.test_case,
                "--timeout", str(args.timeout),
                "-s", str(args.schedules),
                "-ms", str(args.max_steps),
                "--inbox-type", "eventset",
                "--outdir", str(run_output),
                strategy,
            ]
            try:
                result = subprocess.run(
                    command, cwd=output_parent, text=True,
                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                    timeout=args.timeout + 30,
                )
            except subprocess.TimeoutExpired as exc:
                print(f"Run {run_index} exceeded {args.timeout + 30}s wall limit.", file=sys.stderr)
                if exc.stdout:
                    print(exc.stdout if isinstance(exc.stdout, str) else exc.stdout.decode(errors="replace"), file=sys.stderr)
                return 124
            try:
                summary_file = newest_summary(run_output, started_at)
                summary = parse_summary(summary_file)
            except (FileNotFoundError, ValueError) as exc:
                print(f"Run {run_index}: {exc}\nChecker output:\n{result.stdout}", file=sys.stderr)
                return 2

            # P Checker uses exit code 1 to signal that it found a bug.
            # That's a successful evaluation result; other nonzero codes
            # indicate a run failure (as does code 1 without a bug summary).
            if result.returncode and not (result.returncode == 1 and summary["bugs"] > 0):
                print(f"Run {run_index} failed (exit {result.returncode}):\n{result.stdout}", file=sys.stderr)
                return result.returncode

            if summary["bugs"] > 0:
                bug_schedules.append(summary["schedules"])
                bug_times.append(summary["time_seconds"])
                print(f"  run {run_index}: bug found after {summary['schedules']:g} schedules, {summary['time_seconds']:g}s")
            else:
                never_found += 1
                print(f"  run {run_index}: no bug ({summary['schedules']:g} schedules, {summary['time_seconds']:g}s)")

        print(summarize("Schedules to find a bug", bug_schedules))
        print(summarize("Time to find a bug (seconds)", bug_times))
        print(f"  Runs that never found a bug: {never_found}/{args.runs}")
        all_results[strategy] = (bug_schedules, bug_times, never_found)

    print("\nFinal summary (all strategies)")
    for strategy, (bug_schedules, bug_times, never_found) in all_results.items():
        print(f"\n{strategy}:")
        print(summarize("Schedules to find a bug", bug_schedules))
        print(summarize("Time to find a bug (seconds)", bug_times))
        print(f"  Runs that never found a bug: {never_found}/{args.runs}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
