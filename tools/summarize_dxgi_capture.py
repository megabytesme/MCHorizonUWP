#!/usr/bin/env python3
"""Summarize PresentMon --v1_metrics CSVs independently by swap chain."""
import argparse
import csv
import json
import math
import statistics
from collections import Counter, defaultdict
from pathlib import Path


def number(value):
    try:
        result = float(value)
        return result if math.isfinite(result) else None
    except (TypeError, ValueError):
        return None


def quantile(values, q):
    position = (len(values) - 1) * q
    lo = math.floor(position)
    hi = math.ceil(position)
    return values[lo] + (values[hi] - values[lo]) * (position - lo)


def stats(values, cadence=False):
    result = {"count": len(values)}
    if not values:
        return result
    values = sorted(values)
    mean = statistics.fmean(values)
    median = quantile(values, .50)
    result.update(
        mean_ms=mean, p50_ms=median, p95_ms=quantile(values, .95),
        p99_ms=quantile(values, .99), max_ms=values[-1],
    )
    if cadence:
        worst = values[-math.ceil(len(values) * .01):]
        result.update(
            mean_fps=1000 / mean,
            median_fps=1000 / median,
            one_percent_low_fps=1000 / statistics.fmean(worst),
            slow_frames_over_16_67ms=sum(x > 1000 / 60 for x in values),
            slow_frames_over_33_33ms=sum(x > 1000 / 30 for x in values),
            slow_frames_over_50ms=sum(x > 50 for x in values),
        )
    return result


def column_values(rows, column, positive=False, displayed_only=False):
    result = []
    for row in rows:
        if displayed_only and (row.get('Dropped') or '').strip() != '0':
            continue
        value = number(row.get(column))
        if value is not None and (not positive or value > 0):
            result.append(value)
    return result


def counts(rows, column):
    if not rows or column not in rows[0]:
        return None
    return dict(sorted(Counter((r.get(column) or '').strip() or '<missing>' for r in rows).items()))


def summarize(key, rows):
    times = column_values(rows, 'TimeInSeconds')
    optional = ('msInPresentAPI', 'msUntilRenderComplete', 'msUntilRenderStart',
                'msGPUActive', 'msGPUVideoActive', 'msUntilDisplayed')
    return {
        'application': key[0], 'process_id': key[1], 'swap_chain_address': key[2],
        'rows': len(rows),
        'observed_present_time_span_seconds': max(times) - min(times) if len(times) >= 2 else None,
        'present_runtime_counts': counts(rows, 'Runtime'),
        'present_mode_counts': counts(rows, 'PresentMode'),
        'sync_interval_counts': counts(rows, 'SyncInterval'),
        'present_flag_counts': counts(rows, 'PresentFlags'),
        'allows_tearing_counts': counts(rows, 'AllowsTearing'),
        'dropped_counts': counts(rows, 'Dropped'),
        'application_present_cadence': stats(column_values(rows, 'msBetweenPresents', positive=True), True),
        'display_cadence': stats(column_values(rows, 'msBetweenDisplayChange', positive=True, displayed_only=True), True),
        'other_timing_ms': {name: stats(column_values(rows, name, displayed_only=(name == 'msUntilDisplayed')))
                            for name in optional if name in rows[0]},
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('csv', type=Path, help='PresentMon CSV captured with --v1_metrics')
    parser.add_argument('--process', help='case-insensitive exact Application name')
    parser.add_argument('--pid', help='exact ProcessID')
    parser.add_argument('--swapchain', help='case-insensitive exact SwapChainAddress')
    args = parser.parse_args()
    with args.csv.open(encoding='utf-8-sig', newline='') as file:
        reader = csv.DictReader(file)
        required = {'Application', 'ProcessID', 'SwapChainAddress', 'msBetweenPresents'}
        missing = required - set(reader.fieldnames or [])
        if missing:
            parser.error('missing v1 columns: ' + ', '.join(sorted(missing)))
        groups = defaultdict(list)
        for row in reader:
            key = tuple((row.get(name) or '').strip() for name in ('Application', 'ProcessID', 'SwapChainAddress'))
            if args.process and key[0].casefold() != args.process.casefold():
                continue
            if args.pid and key[1] != args.pid:
                continue
            if args.swapchain and key[2].casefold() != args.swapchain.casefold():
                continue
            groups[key].append(row)
    if not groups:
        parser.error('no matching rows')
    report = {
        'source': str(args.csv),
        'methodology': {
            'grouping': 'Each process name, PID and swap chain is summarized separately; never aggregate chains.',
            'cadence': 'Positive finite intervals only. Display cadence requires Dropped=0; missing/unknown is excluded. Counts can differ from rows.',
            'fps': 'Mean FPS is 1000 / mean interval; median FPS is 1000 / median interval. 1% low is 1000 / mean of the slowest ceil(1% of intervals).',
            'percentiles': 'Linear interpolation at rank (n-1)*p on sorted intervals.',
            'duration': 'Observed span of TimeInSeconds in each group, not necessarily the full timed recording duration.',
            'drop_counts': 'Observed CSV values only; --exclude_dropped makes drop counts incomplete.',
            'timing': 'Optional timing fields are measured PresentMon values, not CPU thread load; missing and NA are excluded, never replaced with zero. GPU attribution and accuracy depend on system and capture.',
        },
        'chains': [summarize(key, rows) for key, rows in sorted(groups.items())],
    }
    print(json.dumps(report, indent=2, allow_nan=False))


if __name__ == '__main__':
    main()
