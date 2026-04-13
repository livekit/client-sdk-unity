#!/usr/bin/env python3
"""Convert NUnit3 XML test results to a self-contained HTML report."""

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

    rows = []
    for case in sorted(all_cases, key=sort_key):
        result = case["result"]
        result_class = result.lower()
        dur = f"{case['duration']:.3f}s"

        # Fixture: strip namespace prefix for readability
        fixture = case["classname"].split(".")[-1] if case["classname"] else ""

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
            f"<td>{escape(fixture)}</td>"
            f'<td class="result">{result}</td>'
            f"<td class=\"duration\">{dur}</td>"
            f"</tr>"
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
  table {{ width: 100%; border-collapse: collapse; font-size: 0.9rem; }}
  th {{ text-align: left; padding: 0.6rem 0.8rem; background: #16213e; color: #aaa; font-weight: 600; font-size: 0.75rem; text-transform: uppercase; position: sticky; top: 0; }}
  td {{ padding: 0.5rem 0.8rem; border-bottom: 1px solid #222; vertical-align: top; }}
  tr.passed td.result {{ color: #4caf50; }}
  tr.failed td.result {{ color: #f44336; font-weight: 600; }}
  tr.skipped td.result {{ color: #ff9800; }}
  tr.failed {{ background: #2a1215; }}
  td.duration {{ font-variant-numeric: tabular-nums; color: #888; white-space: nowrap; }}
  details {{ margin-top: 0.4rem; }}
  summary {{ cursor: pointer; color: #f44336; font-size: 0.8rem; }}
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
<table>
<thead><tr><th>Test</th><th>Fixture</th><th>Result</th><th>Duration</th></tr></thead>
<tbody>
{"".join(rows)}
</tbody>
</table>
</body>
</html>"""


def main():
    parser = argparse.ArgumentParser(description="Convert NUnit3 XML results to HTML")
    parser.add_argument("files", nargs="+", type=Path, help="NUnit3 XML result files")
    parser.add_argument("-o", "--output", type=Path, default=Path("test-results.html"), help="Output HTML file (default: test-results.html)")
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

    html = render_html(summaries, all_cases)
    args.output.write_text(html)
    print(f"Report written to {args.output}")


if __name__ == "__main__":
    main()
