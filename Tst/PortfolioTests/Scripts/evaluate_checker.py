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
TIMELINES_PER_SCHEDULE = "Timelines / schedules"
STATISTICS = {"Mean": statistics.mean, "Median": statistics.median}


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
    if "timelines_explored" in summary:
        timelines = int(summary["timelines_explored"])
    else:
        timeline_counts = re.findall(r"^\.{5} Explored (\d+) timelines?\s*$", output, re.MULTILINE)
        if not timeline_counts:
            raise ValueError("Checker summary and output are missing the explored timeline count")
        timelines = int(timeline_counts[-1])

    elapsed_times = re.findall(r"^\.{3} Elapsed (\S+) sec\.\s*$", output, re.MULTILINE)
    elapsed = float(elapsed_times[-1]) if elapsed_times else summary["time_seconds"]
    if elapsed <= 0:
        raise ValueError("Cannot calculate schedules/sec with nonpositive checker elapsed time")

    return summary["schedules"] / elapsed, timelines


def summarize(label: str, values: Sequence[float]) -> str:
    if not values:
        return f"  {label}: no runs found a bug"
    return (
        f"  {label}: mean={statistics.mean(values):.3f}, "
        f"median={statistics.median(values):.3f}, max={max(values):.3f}, "
        f"min={min(values):.3f}"
    )


@dataclass
class StrategyResult:
    hits: int
    runs: int
    metrics: dict[str, dict[str, float | None]]


def format_table(rows: list[list[str]]) -> list[str]:
    widths = [max(map(len, column)) for column in zip(*rows)]
    return ["  ".join(cell.ljust(width) for cell, width in zip(row, widths)).rstrip()
            for row in rows]


def format_metric_table(labels: list[str], metrics: list[str], rows: list[list[str]]) -> list[str]:
    headers = labels + list(STATISTICS) * len(metrics)
    widths = [max(map(len, column)) for column in zip(headers, *rows, strict=True)]
    group_titles = [" " * width for width in widths[:len(labels)]]
    starts = range(len(labels), len(headers), 2)
    for metric, start in zip(metrics, starts, strict=True):
        group_width = widths[start] + 2 + widths[start + 1]
        extra = max(0, len(metric) - group_width)
        widths[start] += extra // 2
        widths[start + 1] += extra - extra // 2
        group_titles.append(metric.center(group_width + extra))

    def format_row(row: list[str]) -> str:
        cells = [cell.ljust(width) for cell, width in zip(row[:len(labels)], widths)]
        for start in starts:
            cells.append(f"{row[start]:>{widths[start]}}  {row[start + 1]:>{widths[start + 1]}}")
        return " │ ".join(cells).rstrip()

    divider = "─┼─".join("─" * len(title) for title in group_titles)
    return [" │ ".join(group_titles).rstrip(), format_row(headers), divider, *map(format_row, rows)]


def comparison_table(results: dict[str, StrategyResult]) -> list[str]:
    metrics = list(results["random"].metrics)
    rows = []
    for name, result in results.items():
        cells = [name, f"{result.runs - result.hits}/{result.runs}"]
        for metric in metrics:
            for stat in STATISTICS:
                value = result.metrics[metric][stat]
                if value is None:
                    cells.append("—")
                elif metric == TIMELINES_PER_SCHEDULE:
                    cells.append(f"{value:.2%}")
                else:
                    cell = f"{value:.3f}".rstrip("0").rstrip(".")
                    baseline = results["random"].metrics[metric][stat]
                    if name != "random":
                        cell += " (—)" if baseline in (None, 0) else f" ({value / baseline:.2f}x)"
                    cells.append(cell)
        rows.append(cells)

    lines = ["Strategy comparison (multipliers compare the same statistic with random)",
             "Schedules to bug, seconds to bug, and violating steps use bug-finding runs only.",
             "Timelines / schedules uses each run's distinct timeline count divided by its schedule count, excluding zero-schedule runs.",
             "— indicates an unavailable value or multiplier.", ""]
    return lines + format_metric_table(["Strategy", "Missed runs"], metrics, rows)


def aggregate_summary(benchmarks: dict[str, dict[str, StrategyResult]]) -> list[str]:
    if not benchmarks:
        return ["No benchmarks completed."]

    first = next(iter(benchmarks.values()))
    metrics = list(first["random"].metrics)
    count = len(benchmarks)
    hit_rows = [["Benchmark", *first]]
    for benchmark, results in benchmarks.items():
        hit_rows.append([benchmark, *[f"{results[name].hits}/{results[name].runs}" for name in first]])

    rates = {
        name: statistics.mean(results[name].hits / results[name].runs for results in benchmarks.values())
        for name in first
    }
    rows = []
    for name, rate in rates.items():
        rate_cell = f"{rate:.2%}".rstrip("%").rstrip("0").rstrip(".") + "%"
        if name != "random":
            delta = round(100 * (rate - rates["random"]), 2)
            change = f"{delta:+.2f}".rstrip("0").rstrip(".")
            rate_cell += f" ({change}%)"
        hits = sum(results[name].hits > 0 for results in benchmarks.values())
        cells = [name, rate_cell, f"{hits}/{count}"]
        for metric in metrics:
            for stat in STATISTICS:
                if metric == TIMELINES_PER_SCHEDULE:
                    shares = [results[name].metrics[metric][stat] for results in benchmarks.values()]
                    values = [share for share in shares if share is not None]
                    cell = f"{statistics.mean(values):.2%}" if values else "—"
                else:
                    values = []
                    for results in benchmarks.values():
                        value = results[name].metrics[metric][stat]
                        baseline = results["random"].metrics[metric][stat]
                        if value is not None and value > 0 and baseline is not None and baseline > 0:
                            values.append(value / baseline)
                    cell = f"{statistics.geometric_mean(values):.2f}x" if values else "—"
                cells.append(f"{cell} ({len(values)}/{count})")
        rows.append(cells)

    return [
        "Aggregate strategy comparison",
        "Bug-finding rate averages benchmark hit rates equally. Parentheses show the rate minus random's rate.",
        "Mean and Median multipliers are geometric means of the corresponding per-benchmark strategy/random ratios. Both values must be positive.",
        "Each multiplier includes its contributing benchmark count. Schedules to bug, seconds to bug, and violating steps use only runs that found a bug.",
        "Timelines / schedules shows the equally weighted averages of benchmark means and benchmark medians. Zero-schedule runs are excluded.",
        "", *format_metric_table(["Strategy", "Bug-finding rate", "Benchmarks hit"], metrics, rows),
        "", "Runs that found a bug / completed runs", "", *format_table(hit_rows),
    ]


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
    parser.add_argument("--retries", type=int, default=2,
                        help="Retries per failed run (default: 2)")
    args = parser.parse_args()
    if args.retries < 0:
        parser.error("--retries must be nonnegative")

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
    completed = {}
    failed = []
    for name in benchmark_names:
        print(f"\nBenchmark: {name}")
        benchmark_output = evaluation_dir / name
        benchmark_output.mkdir()
        results = {}
        result = evaluate_benchmark(
            BENCHMARKS[name], benchmark_output, args.runs, args.timeout, exploration_args, args.retries, results,
        )
        if result:
            print(f"Benchmark {name} failed (exit {result})", file=sys.stderr)
            exit_code = exit_code or result
            failed.append(name)
        else:
            completed[name] = results

    lines = aggregate_summary(completed)
    if failed:
        lines.extend(["", "Excluded incomplete benchmarks: " + ", ".join(failed)])
    summary_path = evaluation_dir / "evaluation_summary.txt"
    summary_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Cross-benchmark summary written to {summary_path}")
    return exit_code


def evaluate_benchmark(
    benchmark: Benchmark,
    benchmark_output: Path,
    runs: int,
    timeout: int,
    exploration_args: Sequence[str],
    retries: int = 2,
    completed_results: dict[str, StrategyResult] | None = None,
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

    report_lines = []
    results = {}
    for strategy_name, strategy_args, policy in strategies:
        bug_schedules: list[float] = []
        bug_times: list[float] = []
        violating_schedule_lengths: list[float] = []
        schedule_rates: list[float] = []
        timelines_explored: list[int] = []
        timeline_shares: list[float] = []
        never_found = 0
        monitor_reports: list[dict[str, float]] = []
        print(f"\n{strategy_name}: {runs} runs")
        for run_index in range(1, runs + 1):
            run_label = f"{strategy_name} run {run_index}"
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
            for attempt in range(1, retries + 2):
                started_at = time.time()
                exit_code = 2
                output = ""
                try:
                    result = subprocess.run(
                        command, cwd=benchmark.directory, text=True,
                        stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                        timeout=timeout + 30,
                    )
                    output = result.stdout
                    exit_code = result.returncode or 2
                    summary_file = newest_summary(run_output, started_at)
                    summary = parse_summary(summary_file)
                    if result.returncode and not (result.returncode == 1 and summary["bugs"] > 0):
                        raise ValueError(f"checker exited with code {result.returncode}")
                    exit_code = 2
                    if summary["bugs"] > 0 and "violating_schedule_length" not in summary:
                        raise ValueError(f"{summary_file} is missing violating_schedule_length")
                    schedule_rate, timeline_count = parse_run_performance(output, summary)
                    if policy is not None:
                        monitor_reports.extend(read_monitor_statistics(summary_file.parent))
                    break
                except subprocess.TimeoutExpired as exc:
                    reason = f"exceeded {timeout + 30}s wall limit"
                    output = exc.stdout or ""
                    if isinstance(output, bytes):
                        output = output.decode(errors="replace")
                    exit_code = 124
                except (OSError, ValueError) as exc:
                    reason = str(exc)

                run_output.mkdir(parents=True, exist_ok=True)
                failed_output = run_output.with_name(f"{run_output.name}_failed_{attempt:02d}")
                run_output.rename(failed_output)
                (failed_output / "checker.log").write_text(output, encoding="utf-8")
                print(f"{run_label} attempt {attempt}/{retries + 1}: {reason}. "
                      f"Failed attempt saved in {failed_output}", file=sys.stderr)
                if attempt > retries:
                    return exit_code

            schedule_rates.append(schedule_rate)
            timelines_explored.append(timeline_count)
            if summary["schedules"] > 0:
                timeline_shares.append(timeline_count / summary["schedules"])

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
        display_name = f"MGF {policy.replace('-', ' ')}" if policy else strategy_name
        metrics = {
            "Schedules to bug": bug_schedules,
            "Seconds to bug": bug_times,
            "Violating steps": violating_schedule_lengths,
            "Schedules/sec": schedule_rates,
            "Timelines explored": timelines_explored,
            TIMELINES_PER_SCHEDULE: timeline_shares,
        }
        summaries = {
            metric: {stat: calculate(values) if values else None for stat, calculate in STATISTICS.items()}
            for metric, values in metrics.items()
        }
        results[display_name] = StrategyResult(runs - never_found, runs, summaries)
        report_lines.extend(["", f"{display_name}:", *strategy_summary])

        if policy is not None:
            monitor_summary = summarize_monitor_statistics(policy, monitor_reports, runs)
            monitor_summary_path = benchmark_output / f"{strategy_name}_monitor_guided_statistics.txt"
            monitor_summary_path.write_text(monitor_summary, encoding="utf-8")
            print(f"Monitor-guided statistics summary written to {monitor_summary_path}")

    report = "\n".join(["Final summary (all strategies)", "", *comparison_table(results),
                        *report_lines]) + "\n"
    print(f"\n{report}", end="")
    summary_path = benchmark_output / "evaluation_summary.txt"
    summary_path.write_text(report, encoding="utf-8")
    print(f"\nSummary written to {summary_path}")
    if completed_results is not None:
        completed_results.update(results)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
