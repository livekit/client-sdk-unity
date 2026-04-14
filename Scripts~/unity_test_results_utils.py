#!/usr/bin/env python3
"""Convert NUnit3 XML test results to HTML, GitHub-flavored Markdown, or console summary."""

import argparse
import xml.etree.ElementTree as ET
from html import escape
from pathlib import Path


def parse_test_cases(xml_path: Path):
    tree = ET.parse(xml_path)
    root = tree.getroot()

    summary = {
        "file": xml_path.name,
        "total": int(root.get("total", 0)),
        "passed": int(root.get("passed", 0)),
        "failed": int(root.get("failed", 0)),
        "skipped": int(root.get("skipped", 0)),
        "duration": float(root.get("duration", 0)),
        "start_time": root.get("start-time", ""),
        "end_time": root.get("end-time", ""),
    }

    cases = []
    for tc in root.iter("test-case"):
        case = {
            "name": tc.get("name", ""),
            "fullname": tc.get("fullname", ""),
            "classname": tc.get("classname", ""),
            "result": tc.get("result", "Unknown"),
            "duration": float(tc.get("duration", 0)),
            "message": "",
            "stack_trace": "",
            "skip_reason": "",
        }

        failure = tc.find("failure")
        if failure is not None:
            msg = failure.find("message")
            st = failure.find("stack-trace")
            case["message"] = msg.text.strip() if msg is not None and msg.text else ""
            case["stack_trace"] = st.text.strip() if st is not None and st.text else ""

        reason = tc.find("reason")
        if reason is not None:
            msg = reason.find("message")
            case["skip_reason"] = msg.text.strip() if msg is not None and msg.text else ""

        cases.append(case)

    return summary, cases


def sort_key(case):
    order = {"Failed": 0, "Skipped": 2, "Passed": 3}
    return order.get(case["result"], 1)


def group_by_fixture(all_cases):
    """Group cases by fixture (short classname) and sort fixtures: those with failures first."""
    from collections import OrderedDict
    groups = OrderedDict()
    for case in all_cases:
        fixture = case["classname"].split(".")[-1] if case["classname"] else "Other"
        groups.setdefault(fixture, []).append(case)

    def fixture_sort_key(item):
        fixture, cases = item
        has_failed = any(c["result"] == "Failed" for c in cases)
        has_skipped = any(c["result"] == "Skipped" for c in cases)
        return (0 if has_failed else 1 if has_skipped else 2, fixture)

    return sorted(groups.items(), key=fixture_sort_key)


def render_html(summaries, all_cases):
    total = sum(s["total"] for s in summaries)
    passed = sum(s["passed"] for s in summaries)
    failed = sum(s["failed"] for s in summaries)
    skipped = sum(s["skipped"] for s in summaries)
    duration = sum(s["duration"] for s in summaries)

    if failed > 0:
        overall_class = "failed"
        overall_text = "FAILED"
    elif total == passed + skipped:
        overall_class = "passed"
        overall_text = "PASSED"
    else:
        overall_class = "unknown"
        overall_text = "UNKNOWN"

    fixture_sections = []
    for fixture, cases in group_by_fixture(all_cases):
        f_passed = sum(1 for c in cases if c["result"] == "Passed")
        f_failed = sum(1 for c in cases if c["result"] == "Failed")
        f_skipped = sum(1 for c in cases if c["result"] == "Skipped")
        has_failed = f_failed > 0

        parts = []
        if f_failed:
            parts.append(f'<span class="cnt failed">{f_failed} failed</span>')
        if f_skipped:
            parts.append(f'<span class="cnt skipped">{f_skipped} skipped</span>')
        if f_passed:
            parts.append(f'<span class="cnt passed">{f_passed} passed</span>')
        fixture_class = "failed" if has_failed else "passed" if f_passed == len(cases) else "skipped"
        open_attr = " open" if has_failed else ""

        rows = []
        for case in sorted(cases, key=sort_key):
            result = case["result"]
            result_class = result.lower()
            dur = f"{case['duration']:.3f}s"

            detail = ""
            if result == "Failed" and (case["message"] or case["stack_trace"]):
                detail = (
                    f'<details><summary>Details</summary>'
                    f'<div class="failure-msg">{escape(case["message"])}</div>'
                )
                if case["stack_trace"]:
                    detail += f'<pre class="stack-trace">{escape(case["stack_trace"])}</pre>'
                detail += "</details>"
            elif result == "Skipped" and case["skip_reason"]:
                detail = f'<div class="skip-reason">{escape(case["skip_reason"])}</div>'

            rows.append(
                f'<tr class="{result_class}">'
                f"<td>{escape(case['name'])}{detail}</td>"
                f'<td class="result">{result}</td>'
                f'<td class="duration">{dur}</td>'
                f"</tr>"
            )

        fixture_sections.append(
            f'<details class="fixture {fixture_class}"{open_attr}>'
            f'<summary><strong>{escape(fixture)}</strong> &mdash; {", ".join(parts)}</summary>'
            f'<table><thead><tr><th>Test</th><th>Result</th><th>Duration</th></tr></thead>'
            f'<tbody>{"".join(rows)}</tbody></table>'
            f'</details>'
        )

    files = ", ".join(s["file"] for s in summaries)
    time_range = ""
    if summaries[0]["start_time"]:
        time_range = f'{summaries[0]["start_time"]} &mdash; {summaries[-1]["end_time"]}'

    return f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Test Results</title>
<style>
  * {{ margin: 0; padding: 0; box-sizing: border-box; }}
  body {{ font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; background: #1a1a2e; color: #e0e0e0; padding: 2rem; }}
  h1 {{ margin-bottom: 0.5rem; font-size: 1.5rem; }}
  .meta {{ color: #888; font-size: 0.85rem; margin-bottom: 1.5rem; }}
  .summary {{ display: flex; gap: 1rem; margin-bottom: 1.5rem; flex-wrap: wrap; }}
  .stat {{ background: #16213e; border-radius: 8px; padding: 1rem 1.5rem; min-width: 120px; text-align: center; }}
  .stat .value {{ font-size: 1.8rem; font-weight: 700; }}
  .stat .label {{ font-size: 0.75rem; text-transform: uppercase; color: #888; margin-top: 0.25rem; }}
  .stat.passed .value {{ color: #4caf50; }}
  .stat.failed .value {{ color: #f44336; }}
  .stat.skipped .value {{ color: #ff9800; }}
  .stat.total .value {{ color: #2196f3; }}
  .overall {{ font-size: 0.85rem; font-weight: 700; padding: 0.3rem 0.8rem; border-radius: 4px; display: inline-block; margin-bottom: 1rem; }}
  .overall.passed {{ background: #1b5e20; color: #a5d6a7; }}
  .overall.failed {{ background: #b71c1c; color: #ef9a9a; }}
  .overall.unknown {{ background: #555; }}
  details.fixture {{ background: #16213e; border-radius: 8px; margin-bottom: 0.5rem; }}
  details.fixture > summary {{ cursor: pointer; padding: 0.8rem 1rem; font-size: 0.95rem; list-style: none; }}
  details.fixture > summary::-webkit-details-marker {{ display: none; }}
  details.fixture > summary::before {{ content: "\u25b6 "; font-size: 0.7rem; margin-right: 0.5rem; display: inline-block; transition: transform 0.15s; }}
  details.fixture[open] > summary::before {{ transform: rotate(90deg); }}
  details.fixture.failed > summary {{ color: #f44336; }}
  details.fixture.passed > summary {{ color: #4caf50; }}
  details.fixture.skipped > summary {{ color: #ff9800; }}
  .cnt.failed {{ color: #f44336; }}
  .cnt.passed {{ color: #4caf50; }}
  .cnt.skipped {{ color: #ff9800; }}
  table {{ width: 100%; border-collapse: collapse; font-size: 0.9rem; }}
  th {{ text-align: left; padding: 0.6rem 0.8rem; background: #0f1a30; color: #aaa; font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }}
  td {{ padding: 0.5rem 0.8rem; border-bottom: 1px solid #1a2744; vertical-align: top; }}
  tr.passed td.result {{ color: #4caf50; }}
  tr.failed td.result {{ color: #f44336; font-weight: 600; }}
  tr.skipped td.result {{ color: #ff9800; }}
  tr.failed {{ background: #2a1215; }}
  td.duration {{ font-variant-numeric: tabular-nums; color: #888; white-space: nowrap; }}
  details:not(.fixture) {{ margin-top: 0.4rem; }}
  details:not(.fixture) > summary {{ cursor: pointer; color: #f44336; font-size: 0.8rem; }}
  .failure-msg {{ background: #1a1a1a; padding: 0.5rem; border-radius: 4px; margin-top: 0.3rem; font-size: 0.8rem; color: #ef9a9a; }}
  .stack-trace {{ background: #1a1a1a; padding: 0.5rem; border-radius: 4px; margin-top: 0.3rem; font-size: 0.75rem; color: #999; overflow-x: auto; white-space: pre-wrap; word-break: break-all; }}
  .skip-reason {{ font-size: 0.8rem; color: #ff9800; margin-top: 0.3rem; }}
</style>
</head>
<body>
<h1>Test Results</h1>
<div class="meta">{escape(files)}{(' &bull; ' + time_range) if time_range else ''}</div>
<span class="overall {overall_class}">{overall_text}</span>
<div class="summary">
  <div class="stat total"><div class="value">{total}</div><div class="label">Total</div></div>
  <div class="stat passed"><div class="value">{passed}</div><div class="label">Passed</div></div>
  <div class="stat failed"><div class="value">{failed}</div><div class="label">Failed</div></div>
  <div class="stat skipped"><div class="value">{skipped}</div><div class="label">Skipped</div></div>
  <div class="stat"><div class="value">{duration:.1f}s</div><div class="label">Duration</div></div>
</div>
{"".join(fixture_sections)}
</body>
</html>"""


def render_markdown(summaries, all_cases):
    total = sum(s["total"] for s in summaries)
    passed = sum(s["passed"] for s in summaries)
    failed = sum(s["failed"] for s in summaries)
    skipped = sum(s["skipped"] for s in summaries)
    duration = sum(s["duration"] for s in summaries)

    if failed > 0:
        status = "\U0001f534 FAILED"
    elif total == passed + skipped:
        status = "\U0001f7e2 PASSED"
    else:
        status = "\u2753 UNKNOWN"

    icons = {"Failed": "\u274c", "Skipped": "\u26a0\ufe0f", "Passed": "\u2705"}

    lines = [
        f"### {status}",
        f"**{total}** tests \u2014 **{passed}** passed, **{failed}** failed, **{skipped}** skipped \u2014 {duration:.1f}s",
        "",
    ]

    for fixture, cases in group_by_fixture(all_cases):
        f_passed = sum(1 for c in cases if c["result"] == "Passed")
        f_failed = sum(1 for c in cases if c["result"] == "Failed")
        f_skipped = sum(1 for c in cases if c["result"] == "Skipped")
        has_failed = f_failed > 0

        parts = []
        if f_failed:
            parts.append(f"{f_failed} failed")
        if f_skipped:
            parts.append(f"{f_skipped} skipped")
        if f_passed:
            parts.append(f"{f_passed} passed")
        fixture_icon = "\u274c" if has_failed else "\u2705" if f_passed == len(cases) else "\u26a0\ufe0f"
        summary_text = f"{fixture_icon} {fixture} \u2014 {', '.join(parts)}"

        # Fixtures with failures are expanded by default
        open_attr = " open" if has_failed else ""
        lines.append(f"<details{open_attr}>")
        lines.append(f"<summary>{summary_text}</summary>")
        lines.append("")
        lines.append("| Test | Result | Duration |")
        lines.append("|------|--------|----------|")

        for case in sorted(cases, key=sort_key):
            result = case["result"]
            icon = icons.get(result, "")
            dur = f"{case['duration']:.3f}s"
            lines.append(f"| {case['name']} | {icon} {result} | {dur} |")

            if result == "Failed" and (case["message"] or case["stack_trace"]):
                detail = f"<details><summary>Details</summary><code>{escape(case['message'])}</code>"
                if case["stack_trace"]:
                    detail += f"<br><pre>{escape(case['stack_trace'])}</pre>"
                detail += "</details>"
                lines.append(f"| {detail} | | |")

        lines.append("")
        lines.append("</details>")
        lines.append("")

    return "\n".join(lines)


def render_console(summaries, all_cases):
    total = sum(s["total"] for s in summaries)
    passed = sum(s["passed"] for s in summaries)
    failed = sum(s["failed"] for s in summaries)
    skipped = sum(s["skipped"] for s in summaries)
    duration = sum(s["duration"] for s in summaries)

    lines = [f"{total} tests: {passed} passed, {failed} failed, {skipped} skipped ({duration:.1f}s)"]

    failed_cases = [c for c in all_cases if c["result"] == "Failed"]
    if failed_cases:
        lines.append("")
        lines.append("FAILED TESTS:")
        for case in failed_cases:
            lines.append(f"  FAIL: {case['fullname']} ({case['duration']:.3f}s)")
            if case["message"]:
                for msg_line in case["message"].splitlines():
                    lines.append(f"        {msg_line}")
            if case["stack_trace"]:
                for st_line in case["stack_trace"].splitlines()[:10]:
                    lines.append(f"        {st_line}")

    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description="Convert NUnit3 XML test results to HTML, Markdown, or console summary")
    parser.add_argument("files", nargs="+", type=Path, help="NUnit3 XML result files")
    parser.add_argument("-o", "--output", type=Path, default=None, help="Output file (default: test-results.html or stdout for markdown/console)")
    parser.add_argument("-f", "--format", choices=["html", "markdown", "console"], default="html", help="Output format (default: html)")
    args = parser.parse_args()

    summaries = []
    all_cases = []
    for f in args.files:
        if not f.exists():
            print(f"Warning: {f} not found, skipping")
            continue
        summary, cases = parse_test_cases(f)
        summaries.append(summary)
        all_cases.extend(cases)

    if not summaries:
        print("No valid XML files found")
        return

    if args.format == "console":
        output = render_console(summaries, all_cases)
        if args.output:
            args.output.write_text(output)
        else:
            print(output)
    elif args.format == "markdown":
        output = render_markdown(summaries, all_cases)
        if args.output:
            args.output.write_text(output)
            print(f"Report written to {args.output}")
        else:
            print(output)
    else:
        output = render_html(summaries, all_cases)
        out_path = args.output or Path("test-results.html")
        out_path.write_text(output)
        print(f"Report written to {out_path}")


if __name__ == "__main__":
    main()
