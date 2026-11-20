#!/usr/bin/env python3
"""Build a single-page HTML test report from .trx files (dotnet test --logger trx).

Usage: python3 tools/trx_to_html.py <dir-with-trx-files> <output-dir>

File names drive the suite labels: "<suite>-<tfm>.trx", e.g. "parity-net10.0.trx".
Standard library only, so it runs on any CI runner without installing anything.
Also writes summary.json and, on GitHub Actions, a Markdown job summary.
"""
import html
import json
import os
import sys
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

SUITE_TITLES = {
    "unit": "Unit tests",
    "integration": "Integration tests (WebApplicationFactory + Azurite)",
    "parity": "PHP vs .NET parity (golden + Reqnroll BDD)",
    "servicebus": "Service Bus emulator",
}
SUITE_ORDER = ["parity", "unit", "integration", "servicebus"]


def parse_duration(value):
    if not value:
        return 0.0
    hours, minutes, seconds = value.split(":")
    return int(hours) * 3600 + int(minutes) * 60 + float(seconds)


def parse_trx(path):
    root = ET.parse(path).getroot()
    classes = {}
    for unit_test in root.iterfind(".//t:TestDefinitions/t:UnitTest", NS):
        method = unit_test.find("t:TestMethod", NS)
        classes[unit_test.get("id")] = method.get("className", "") if method is not None else ""

    tests = []
    for result in root.iterfind(".//t:Results/t:UnitTestResult", NS):
        message = result.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS)
        stack = result.findtext("t:Output/t:ErrorInfo/t:StackTrace", default="", namespaces=NS)
        class_name = classes.get(result.get("testId"), "")
        tests.append({
            "name": result.get("testName", "?"),
            "class": class_name.split(".")[-1] if class_name else "",
            "bdd": ".Features." in class_name or class_name.endswith("Feature"),
            "outcome": result.get("outcome", "Unknown"),
            "duration": parse_duration(result.get("duration")),
            "message": message.strip(),
            "stack": stack.strip(),
        })
    return tests


def suite_key(path):
    stem = path.stem
    suite, _, tfm = stem.rpartition("-")
    if not suite:
        suite, tfm = stem, ""
    return suite, tfm


def outcome_counts(tests):
    passed = sum(t["outcome"] == "Passed" for t in tests)
    failed = sum(t["outcome"] in ("Failed", "Error", "Timeout", "Aborted") for t in tests)
    skipped = len(tests) - passed - failed
    return passed, failed, skipped


def render(suites, meta):
    all_tests = [t for s in suites for t in s["tests"]]
    passed, failed, skipped = outcome_counts(all_tests)
    status = "passed" if failed == 0 and all_tests else "failed"
    e = html.escape

    rows = []
    for s in suites:
        p, f, k = outcome_counts(s["tests"])
        rows.append(
            f"<tr><td>{e(s['title'])}</td><td><code>{e(s['tfm'] or '-')}</code></td>"
            f"<td>{len(s['tests'])}</td><td class='ok'>{p}</td><td class='{'bad' if f else ''}'>{f}</td>"
            f"<td>{k}</td><td>{sum(t['duration'] for t in s['tests']):.1f}s</td></tr>")

    sections = []
    for s in suites:
        p, f, k = outcome_counts(s["tests"])
        items = []
        for t in sorted(s["tests"], key=lambda x: (x["outcome"] == "Passed", x["class"], x["name"])):
            css = {"Passed": "ok", "NotExecuted": "skip"}.get(t["outcome"], "bad")
            detail = ""
            if t["message"] or (css == "bad" and t["stack"]):
                detail = f"<pre>{e(t['message'])}\n\n{e(t['stack'])}</pre>"
            tag = "<span class='tag'>BDD</span> " if t["bdd"] else ""
            items.append(
                f"<li class='{css}'><span class='dot'></span>{tag}<span class='cls'>{e(t['class'])}</span> "
                f"{e(t['name'])} <span class='dur'>{t['duration'] * 1000:.0f} ms</span>{detail}</li>")
        open_attr = " open" if f else ""
        sections.append(
            f"<details{open_attr}><summary><strong>{e(s['title'])}</strong> <code>{e(s['tfm'])}</code> "
            f"&middot; {p} passed, {f} failed, {k} skipped</summary><ul>{''.join(items)}</ul></details>")

    run_link = ""
    if meta.get("run_url"):
        run_link = f" &middot; <a href='{e(meta['run_url'])}'>CI run</a>"
    commit = ""
    if meta.get("sha"):
        commit = f" &middot; commit <code>{e(meta['sha'][:7])}</code>"

    return f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Test report - azure-dotnet-migration</title>
<style>
  :root {{ --ok:#1a7f37; --bad:#cf222e; --skip:#9a6700; --muted:#57606a; --line:#d0d7de; --bg:#f6f8fa; }}
  * {{ box-sizing: border-box; }}
  body {{ font: 15px/1.5 -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; margin: 0; color: #1f2328; }}
  main {{ max-width: 1080px; margin: 0 auto; padding: 32px 20px 64px; }}
  h1 {{ font-size: 26px; margin: 0 0 4px; }}
  .sub {{ color: var(--muted); margin: 0 0 20px; }}
  .note {{ background: #fff8c5; border: 1px solid #d4a72c66; border-radius: 6px; padding: 10px 14px; margin-bottom: 24px; }}
  .cards {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; margin-bottom: 24px; }}
  .card {{ border: 1px solid var(--line); border-radius: 8px; padding: 14px 16px; background: var(--bg); }}
  .card b {{ display: block; font-size: 28px; }}
  .card.status b {{ color: {"var(--ok)" if status == "passed" else "var(--bad)"}; text-transform: uppercase; }}
  table {{ width: 100%; border-collapse: collapse; margin-bottom: 28px; }}
  th, td {{ text-align: left; padding: 8px 10px; border-bottom: 1px solid var(--line); }}
  th {{ background: var(--bg); font-weight: 600; }}
  td.ok {{ color: var(--ok); }} td.bad {{ color: var(--bad); font-weight: 600; }}
  details {{ border: 1px solid var(--line); border-radius: 8px; margin-bottom: 12px; }}
  summary {{ cursor: pointer; padding: 10px 14px; background: var(--bg); border-radius: 8px; }}
  ul {{ list-style: none; margin: 0; padding: 6px 14px 12px; }}
  li {{ padding: 4px 0; border-bottom: 1px dashed #eaeef2; word-break: break-word; }}
  .dot {{ display: inline-block; width: 9px; height: 9px; border-radius: 50%; margin-right: 8px; background: var(--ok); }}
  li.bad .dot {{ background: var(--bad); }} li.skip .dot {{ background: var(--skip); }}
  .cls {{ color: var(--muted); }} .dur {{ color: var(--muted); font-size: 12px; float: right; }}
  .tag {{ font-size: 11px; background: #ddf4ff; color: #0969da; border-radius: 10px; padding: 1px 7px; }}
  pre {{ white-space: pre-wrap; background: #fff1f0; border-radius: 6px; padding: 10px; font-size: 12px; }}
  footer {{ color: var(--muted); font-size: 13px; margin-top: 32px; }}
  code {{ font-size: 13px; }}
</style>
</head>
<body>
<main>
  <h1>Legacy PHP API &rarr; .NET 10 on Azure: test report</h1>
  <p class="sub">Generated {e(meta['generated'])}{commit}{run_link} &middot;
     <a href="https://github.com/ShipSureQA/azure-dotnet-migration">source on GitHub</a></p>
  <div class="note"><strong>Self-initiated demo, not client work.</strong> Built by Nagarasu P
     (<a href="https://shipsureqa.github.io">ShipSure QA</a>) to show PHP-to-.NET migration testing,
     the net8.0 &rarr; net10.0 upgrade path and Azure integration on free emulators.</div>
  <div class="cards">
    <div class="card status"><span>Status</span><b>{status}</b></div>
    <div class="card"><span>Total</span><b>{len(all_tests)}</b></div>
    <div class="card"><span>Passed</span><b style="color:var(--ok)">{passed}</b></div>
    <div class="card"><span>Failed</span><b style="color:{'var(--bad)' if failed else 'inherit'}">{failed}</b></div>
    <div class="card"><span>Skipped</span><b>{skipped}</b></div>
  </div>
  <table>
    <thead><tr><th>Suite</th><th>Framework</th><th>Tests</th><th>Passed</th><th>Failed</th><th>Skipped</th><th>Time</th></tr></thead>
    <tbody>{''.join(rows)}</tbody>
  </table>
  {''.join(sections)}
  <footer>Framework column: the runtime the code under test ran on. The parity suite runs the
    legacy PHP 8 API and the .NET API side by side and compares every response.</footer>
</main>
</body>
</html>
"""


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    source, output = Path(sys.argv[1]), Path(sys.argv[2])
    files = sorted(source.rglob("*.trx"))
    if not files:
        sys.exit(f"No .trx files found under {source}")

    suites = []
    for path in files:
        suite, tfm = suite_key(path)
        suites.append({
            "suite": suite,
            "tfm": tfm,
            "title": SUITE_TITLES.get(suite, suite),
            "tests": parse_trx(path),
        })
    order = {name: i for i, name in enumerate(SUITE_ORDER)}
    suites.sort(key=lambda s: (order.get(s["suite"], 99), s["tfm"]))

    server = os.environ.get("GITHUB_SERVER_URL")
    repo = os.environ.get("GITHUB_REPOSITORY")
    run_id = os.environ.get("GITHUB_RUN_ID")
    meta = {
        "generated": datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC"),
        "sha": os.environ.get("GITHUB_SHA", ""),
        "run_url": f"{server}/{repo}/actions/runs/{run_id}" if server and repo and run_id else "",
    }

    output.mkdir(parents=True, exist_ok=True)
    (output / "index.html").write_text(render(suites, meta), encoding="utf-8")
    (output / ".nojekyll").write_text("", encoding="utf-8")

    summary = []
    for s in suites:
        p, f, k = outcome_counts(s["tests"])
        summary.append({"suite": s["suite"], "tfm": s["tfm"], "total": len(s["tests"]), "passed": p, "failed": f, "skipped": k})
    (output / "summary.json").write_text(json.dumps({"generated": meta["generated"], "suites": summary}, indent=2), encoding="utf-8")

    step_summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if step_summary:
        lines = ["| Suite | Framework | Total | Passed | Failed | Skipped |", "|---|---|---|---|---|---|"]
        lines += [f"| {SUITE_TITLES.get(s['suite'], s['suite'])} | {s['tfm']} | {s['total']} | {s['passed']} | {s['failed']} | {s['skipped']} |" for s in summary]
        with open(step_summary, "a", encoding="utf-8") as handle:
            handle.write("## Test results\n\n" + "\n".join(lines) + "\n")

    total = sum(s["total"] for s in summary)
    failed = sum(s["failed"] for s in summary)
    print(f"Report: {output / 'index.html'} ({total} tests, {failed} failed, {len(files)} trx files)")


if __name__ == "__main__":
    main()
