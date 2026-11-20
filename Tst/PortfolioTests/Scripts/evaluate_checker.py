#!/usr/bin/env python3
"""Run P Checker evaluations for the configured benchmarks."""

import argparse
import json
import re
import statistics
import subprocess
import sys
import time
from collections.abc import Sequence
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
PROJECT_FILE = REPO_ROOT / "Src" / "PCompiler" / "PCommandLine" / "PCommandLine.csproj"
BENCHMARK_ROOT = Path("/home/johnr/purdue/research/mgs/p-benchmark")
MUTATION_BOUNDARY_POLICIES = ("best-prefix", "random-best-prefix", "random-execution")


@dataclass(frozen=True)
class Benchmark:
    directory: Path
    project: str
    project_dll: str
    test_case: str


BENCHMARKS = {
    "leader-election": Benchmark(
        directory=BENCHMARK_ROOT / "leader-election",
        project="LeaderElection-Unreliable-Nondeterministic.pproj",
        project_dll=(
            "PGenerated/LeaderElection/UnreliableNondeterministic/PChecker/net8.0/"
            "LeaderElectionUnreliableNetworkNondeterministicTimer.dll"
        ),
        test_case="tcOneLeaderElection1",
    ),
    "mutual-exclusion": Benchmark(
        directory=BENCHMARK_ROOT / "mutual-exclusion",
        project="MutualExclusion-Unreliable-Nondeterministic.pproj",
        project_dll=(
            "PGenerated/MutualExclusion/UnreliableNondeterministic/PChecker/net8.0/"
            "MutualExclusionUnreliableNetworkNondeterministicTimer.dll"
        ),
        test_case="tcMultipleRequests",
    ),
    "two-phase-commit": Benchmark(
        directory=BENCHMARK_ROOT / "two-phase-commit",
        project="TwoPhaseCommit-Unreliable-Nondeterministic.pproj",
        project_dll=(
            "PGenerated/TwoPhaseCommit/UnreliableNondeterministic/PChecker/net8.0/"
            "TwoPhaseCommitUnreliableNetworkNondeterministicTimer.dll"
        ),
        test_case="tcMultipleClientsNoFailureAtomicity",
    ),
    "single-decree-paxos": Benchmark(
        directory=BENCHMARK_ROOT / "single-decree-paxos",
        project="SingleDecreePaxos-Unreliable-Nondeterministic.pproj",
        project_dll=(
            "PGenerated/UnreliableNondeterministic/PChecker/net8.0/"
            "SingleDecreePaxosUnreliableNetworkNondeterministicTimer.dll"
        ),
        test_case="testBasicPaxos3on1",
    ),
}


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


def parse_run_performance(output: str, summary: dict[str, float]) -> tuple[float, int]:
    timeline_counts = re.findall(r"^\.{5} Explored (\d+) timelines?\s*$", output, re.MULTILINE)
    if not timeline_counts:
        raise ValueError("Checker output is missing the explored timeline count")

    elapsed_times = re.findall(r"^\.{3} Elapsed (\S+) sec\.\s*$", output, re.MULTILINE)
    elapsed = float(elapsed_times[-1]) if elapsed_times else summary["time_seconds"]
    if elapsed <= 0:
        raise ValueError("Cannot calculate schedules/sec with nonpositive checker elapsed time")

    return summary["schedules"] / elapsed, int(timeline_counts[-1])


def summarize(label: str, values: Sequence[float]) -> str:
    if not values:
        return f"  {label}: no runs found a bug"
    return (
        f"  {label}: mean={statistics.mean(values):.3f}, "
        f"median={statistics.median(values):.3f}, max={max(values):.3f}, "
        f"min={min(values):.3f}"
    )


def read_monitor_statistics(directory: Path) -> list[dict[str, float]]:
    paths = sorted(directory.glob("*_monitor_guided_statistics.json"))
    if not paths:
        raise FileNotFoundError(f"No monitor-guided statistics JSON found under {directory}")

    reports = []
    for path in paths:
        report = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(report, dict) or not report or any(
            type(value) not in (int, float) for value in report.values()
        ):
            raise ValueError(f"{path} must contain an object with numeric statistics")
        reports.append(report)
    return reports


def summarize_monitor_statistics(policy: str, reports: list[dict[str, float]], runs: int) -> str:
    field_values: dict[str, list[float]] = {}
    for report in reports:
        for field, value in report.items():
            field_values.setdefault(field, []).append(value)

    lines = [
        f"Monitor-guided statistics ({policy})",
        f"Checker reports: {len(reports)} from {runs} runs",
        "",
    ]
    prefixes = ("Min", "Max", "Mean", "Median")
    grouped_fields = set()
    for field, values in field_values.items():
        if field in grouped_fields:
            continue

        prefix = next((label for label in prefixes if field.startswith(label)), "")
        metric = field[len(prefix):]
        if prefix and metric:
            group = {label: f"{label}{metric}" for label in prefixes}
            if all(member in field_values for member in group.values()):
                lines.append(f"  {metric}:")
                for label, member in group.items():
                    lines.append("  " + summarize(label, field_values[member]))
                grouped_fields.update(group.values())
                continue

        lines.append(summarize(field, values))
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-parent", type=Path, required=True,
                        help="Parent directory for timestamped evaluation results")
    parser.add_argument("--benchmark", choices=BENCHMARKS,
                        help="Run only this benchmark (default: run all benchmarks)")
    parser.add_argument("--runs", type=int, default=1,
                        help="Number of runs per configuration (default: 1)")
    parser.add_argument("--timeout", type=int, required=True,
                        help="Per-run timeout in seconds")
    parser.add_argument("--schedules", type=int,
                        help="Schedules per run")
    parser.add_argument("--max-steps", type=int,
                        help="Maximum steps per schedule")
    args = parser.parse_args()

    output_parent = args.output_parent.expanduser().resolve()
    benchmark_names = (args.benchmark,) if args.benchmark is not None else tuple(BENCHMARKS)
    for name in benchmark_names:
        benchmark = BENCHMARKS[name]
        project = benchmark.directory / benchmark.project
        dll = benchmark.directory / benchmark.project_dll
        if not project.is_file():
            parser.error(f"{name} project file does not exist: {project}")
        if not dll.is_file():
            parser.error(f"{name} DLL does not exist: {dll}")
    if not PROJECT_FILE.is_file():
        parser.error(f"P source project not found: {PROJECT_FILE}")
    if args.runs < 1 or args.timeout < 1:
        parser.error("runs and timeout must both be positive")

    exploration_args = []
    for option, value in (("--schedules", args.schedules), ("--max-steps", args.max_steps)):
        if value is not None:
            if value < 1:
                parser.error(f"{option} must be positive")
            exploration_args.extend([option, str(value)])

    # Build once from this checkout. Subsequent executions use the built source output.
    build = subprocess.run(
        ["dotnet", "build", str(PROJECT_FILE), "--configuration", "Release"],
        cwd=REPO_ROOT, text=True,
    )
    if build.returncode:
        return build.returncode

    timestamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H-%M-%SZ")
    evaluation_dir = output_parent / f"evaluation-results-{timestamp}"
    evaluation_dir.mkdir(parents=True, exist_ok=False)
    print(f"Evaluation directory: {evaluation_dir}")

    exit_code = 0
    for name in benchmark_names:
        print(f"\nBenchmark: {name}")
        benchmark_output = evaluation_dir / name
        benchmark_output.mkdir()
        result = evaluate_benchmark(
            BENCHMARKS[name], benchmark_output, args.runs, args.timeout, exploration_args,
        )
        if result:
            print(f"Benchmark {name} failed (exit {result})", file=sys.stderr)
            exit_code = exit_code or result
    return exit_code


def evaluate_benchmark(
    benchmark: Benchmark,
    benchmark_output: Path,
    runs: int,
    timeout: int,
    exploration_args: Sequence[str],
) -> int:
    dll = benchmark.directory / benchmark.project_dll
    project = benchmark.directory / benchmark.project
    strategies = [
        ("random", ["--sch-random"], None),
        ("fest-random", ["--sch-feedback"], None),
    ]
    strategies.extend(
        (
            f"monitor-guided-feedback-{policy}",
            ["--sch-monitorguidedfeedback", "--sch-monitorguidedfeedback-boundary", policy],
            policy,
        )
        for policy in MUTATION_BOUNDARY_POLICIES
    )
    print(f"Evaluating strategies {', '.join(name for name, _, _ in strategies)}")

    report_lines = ["Final summary (all strategies)"]
    for strategy_name, strategy_args, policy in strategies:
        bug_schedules: list[float] = []
        bug_times: list[float] = []
        violating_schedule_lengths: list[float] = []
        schedule_rates: list[float] = []
        timelines_explored: list[int] = []
        never_found = 0
        monitor_reports: list[dict[str, float]] = []
        print(f"\n{strategy_name}: {runs} runs")
        for run_index in range(1, runs + 1):
            run_label = f"{strategy_name} run {run_index}"
            started_at = time.time()
            # Give each invocation its own output folder. P Checker rotates
            # up to ten prior runs when reusing an output directory; isolated
            # folders avoid that history management and preserve every run.
            run_output = benchmark_output / strategy_name / f"run_{run_index:03d}"
            command = [
                "dotnet", "run", "--project", str(PROJECT_FILE),
                "--configuration", "Release", "--no-build", "--no-launch-profile",
                "--", "check", str(dll),
                "-tc", benchmark.test_case,
                "--timeout", str(timeout),
                *exploration_args,
                "--inbox-type", "eventset",
                "--outdir", str(run_output),
                "--pproj", str(project),
                *strategy_args,
            ]
            try:
                result = subprocess.run(
                    command, cwd=benchmark.directory, text=True,
                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                    timeout=timeout + 30,
                )
            except subprocess.TimeoutExpired as exc:
                print(f"{run_label} exceeded {timeout + 30}s wall limit.", file=sys.stderr)
                if exc.stdout:
                    output = exc.stdout if isinstance(exc.stdout, str) else exc.stdout.decode(errors="replace")
                    print(output, file=sys.stderr)
                return 124
            try:
                summary_file = newest_summary(run_output, started_at)
                summary = parse_summary(summary_file)
                if summary["bugs"] > 0 and "violating_schedule_length" not in summary:
                    raise ValueError(f"{summary_file} is missing violating_schedule_length")
            except (FileNotFoundError, ValueError) as exc:
                print(f"{run_label}: {exc}\nChecker output:\n{result.stdout}", file=sys.stderr)
                return 2

            # P Checker uses exit code 1 to signal that it found a bug.
            # That's a successful evaluation result; other nonzero codes
            # indicate a run failure (as does code 1 without a bug summary).
            if result.returncode and not (result.returncode == 1 and summary["bugs"] > 0):
                print(f"{run_label} failed (exit {result.returncode}):\n{result.stdout}", file=sys.stderr)
                return result.returncode

            try:
                schedule_rate, timeline_count = parse_run_performance(result.stdout, summary)
                if policy is not None:
                    monitor_reports.extend(read_monitor_statistics(summary_file.parent))
            except (OSError, ValueError) as exc:
                print(f"{run_label}: {exc}", file=sys.stderr)
                return 2
            schedule_rates.append(schedule_rate)
            timelines_explored.append(timeline_count)

            if summary["bugs"] > 0:
                bug_schedules.append(summary["schedules"])
                bug_times.append(summary["time_seconds"])
                violating_schedule_lengths.append(summary["violating_schedule_length"])
                status = "bug found"
            else:
                never_found += 1
                status = "no bug"
            outcome = f"{status} ({summary['schedules']:g} schedules, {summary['time_seconds']:g}s)"
            print(f"  Run {run_index}: {outcome}")

        strategy_summary = [
            summarize("Schedules to find a bug", bug_schedules),
            summarize("Time to find a bug (seconds)", bug_times),
            summarize("Violating schedule length (steps)", violating_schedule_lengths),
            f"  Runs that never found a bug: {never_found}/{runs}",
            summarize("Schedules/sec", schedule_rates),
            summarize("Timelines explored", timelines_explored),
        ]
        print("\n".join(strategy_summary))
        report_lines.extend(["", f"{strategy_name}:", *strategy_summary])

        if policy is not None:
            monitor_summary = summarize_monitor_statistics(policy, monitor_reports, runs)
            monitor_summary_path = benchmark_output / f"{strategy_name}_monitor_guided_statistics.txt"
            monitor_summary_path.write_text(monitor_summary, encoding="utf-8")
            print(f"Monitor-guided statistics summary written to {monitor_summary_path}")

    report = "\n".join(report_lines) + "\n"
    print(f"\n{report}", end="")
    summary_path = benchmark_output / "evaluation_summary.txt"
    summary_path.write_text(report, encoding="utf-8")
    print(f"\nSummary written to {summary_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
