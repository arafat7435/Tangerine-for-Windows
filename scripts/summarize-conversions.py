"""Verify completed conversion results and publish a readable, reproducible report."""
import argparse
import csv
import hashlib
import html
import io
import json
import math
import shutil
import statistics
import zipfile
from collections import Counter
from datetime import datetime
from pathlib import Path
from xml.etree import ElementTree as ET


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def output_text(path, target):
    if target in {"docx", "xlsx", "ods", "epub"}:
        with zipfile.ZipFile(path) as archive:
            return "".join(
                "".join(ET.fromstring(archive.read(name)).itertext())
                for name in archive.namelist()
                if name.endswith((".xml", ".xhtml"))
            )
    data = path.read_bytes()
    encoding = "utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig"
    return html.unescape(data.decode(encoding))


def sheet_cells(path, target):
    if target in {"csv", "tsv"}:
        return list(csv.reader(io.StringIO(output_text(path, target)), delimiter="," if target == "csv" else "\t"))
    with zipfile.ZipFile(path) as archive:
        if target == "xlsx":
            ns = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
            shared = ET.fromstring(archive.read("xl/sharedStrings.xml")) if "xl/sharedStrings.xml" in archive.namelist() else []
            strings = ["".join(item.itertext()) for item in shared]
            cells = {}
            for cell in ET.fromstring(archive.read("xl/worksheets/sheet1.xml")).findall(".//s:c", ns):
                value = cell.findtext("s:v", default="", namespaces=ns)
                cells[cell.attrib["r"]] = strings[int(value)] if cell.get("t") == "s" else "".join(cell.itertext()) if cell.get("t") == "inlineStr" else value
            return [[cells.get(f"{col}{row}", "") for col in "AB"] for row in range(1, 5)]
        ns = {"t": "urn:oasis:names:tc:opendocument:xmlns:table:1.0", "p": "urn:oasis:names:tc:opendocument:xmlns:text:1.0"}
        table = ET.fromstring(archive.read("content.xml")).find(".//t:table", ns)
        return [["".join("".join(p.itertext()) for p in cell.findall("p:p", ns))
                 for cell in row.findall("t:table-cell", ns)[:2]] for row in table.findall("t:table-row", ns)[:4]]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("results", type=Path)
    parser.add_argument("--app-path", required=True, type=Path)
    parser.add_argument("--report-dir", type=Path, default=Path(__file__).resolve().parents[1] / "research")
    args = parser.parse_args()
    rows = read_json(args.results / "matrix-results.json")
    plan = read_json(args.results / "matrix-plan.json")
    summary = read_json(args.results / "complete.json")
    execution = read_json(args.results / "execution.json")
    if execution.get("Filter"):
        raise SystemExit("A final report requires an unfiltered conversion run.")
    if any(not row["passed"] for row in rows) or summary["failed"]:
        raise SystemExit("Conversion failures remain; do not publish a passing report.")
    cases = [row for row in rows if row["phase"] != "fixture"]
    key = lambda row: (row["category"], row["input"], row["target"], row["scenario"])
    if Counter(map(key, plan)) != Counter(map(key, cases)):
        raise SystemExit("Incomplete results or duplicate cases.")
    app_hash = hashlib.sha256(args.app_path.with_suffix(".dll").read_bytes()).hexdigest().upper()
    if app_hash != execution["ApplicationDllSHA256"]:
        raise SystemExit("The published binary differs from the tested binary.")

    unicode_rows = []
    text_targets = {"txt", "md", "html", "csv", "tsv", "srt", "vtt", "docx", "xlsx", "ods", "epub"}
    for row in cases:
        if row["phase"] != "conversion" or row["category"] == "Pdf" or row["target"] not in text_targets:
            continue
        body = output_text(Path(row["outputs"][0]), row["target"])
        check = {name: row[name] for name in ("input", "target", "scenario")}
        check.update(french="café" in body, hindi="नमस्ते" in body)
        if row["category"] == "Spreadsheet" and row["target"] in {"csv", "tsv", "xlsx", "ods"}:
            cells = sheet_cells(Path(row["outputs"][0]), row["target"])
            check["cells"] = (len(cells) >= 4 and cells[0][:2] == ["Conversion marker 42", "café नमस्ते"]
                              and cells[3][:2] == ["Total", "49"])
        check["passed"] = check["french"] and check["hindi"] and check.get("cells", True)
        unicode_rows.append(check)
    if any(not row["passed"] for row in unicode_rows):
        print(json.dumps([r for r in unicode_rows if not r["passed"]], ensure_ascii=True, indent=2))
        raise SystemExit("Independent Unicode/cell verification failed.")

    args.report_dir.mkdir(parents=True, exist_ok=True)
    for name, destination in [("matrix-results.json", "conversion-results.json"),
                              ("matrix-plan.json", "conversion-plan.json"),
                              ("complete.json", "conversion-summary.json"),
                              ("execution.json", "conversion-execution.json")]:
        shutil.copyfile(args.results / name, args.report_dir / destination)
    write_json(args.report_dir / "conversion-unicode.json", unicode_rows)
    with (args.report_dir / "conversion-results.csv").open("w", encoding="utf-8-sig", newline="") as output:
        writer = csv.writer(output)
        writer.writerow(["Category", "Input", "Output", "Scenario", "Result", "Conversion milliseconds", "Files"])
        for row in cases:
            writer.writerow([row["category"], row["input"], row["target"], row["scenario"],
                             "Correctly rejected: source has no required content" if row["phase"] == "source-limit" else "PASS",
                             row["conversionMs"] if row["phase"] == "conversion" else "", " | ".join(row["outputs"])])

    standard = [row for row in cases if row["scenario"] == "standard"]
    extra = len(cases) - len(standard)
    extensions = sorted({row["input"] for row in standard})
    lines = ["# Conversion verification", "", datetime.now().astimezone().isoformat(timespec="seconds"), "",
             f"**{summary['passed']} successful conversions, {summary['sourceLimits']} correct source-limit rejections, zero failures.**",
             f"All {len(standard)} standard routes offered by the Windows app were exercised across {len(extensions)} input extensions, including aliases. An additional {extra} cases cover source variations. Fixture preparation is excluded from these counts.", "",
             "| Category | Standard routes | Successful conversions including extra cases | Correct rejections | Median seconds | 95th percentile seconds |",
             "|---|---:|---:|---:|---:|---:|"]
    timings = []
    for group in summary["categories"]:
        category = group["category"]
        matches = [r for r in cases if r["category"] == category]
        durations = sorted(r["conversionMs"] / 1000 for r in matches if r["phase"] == "conversion")
        median, p95 = statistics.median(durations), durations[math.ceil(len(durations) * .95) - 1]
        timings.append({"category": category, "medianSeconds": median, "p95Seconds": p95, "maxSeconds": max(durations)})
        lines.append(f"| {category} | {sum(r['scenario'] == 'standard' for r in matches)} | {len(durations)} | {sum(r['phase'] == 'source-limit' for r in matches)} | {median:.3f} | {p95:.3f} |")
    lines += ["", "Timings cover conversion only; output validation and fixture setup are excluded. Fixtures use 160×120 images, two-second media, and short documents/slides. These are measured test times on this PC, not a promise for large files.", "",
              "## Validation", "",
              "Outputs were decoded or parsed and original-file hashes checked. Validation covers image dimensions/nonblank pixels, media streams/duration and full FFmpeg decoding, PDF/page/slide counts, document text, spreadsheet totals, EPUB package/title validity, subtitle timings, and extracted archive entry hashes. Multi-page TIFF and image-only PDF tests check page preservation; image-only PDF → DOCX is also converted back to PDF to check page count. Staging cleanup is checked at completion.", "",
              f"An independent Python read of {len(unicode_rows)} applicable outputs confirms both French (`café`) and Hindi (`नमस्ते`) text. CSV/TSV outputs also retain the expected columns and calculated total. See [Unicode/cell checks](conversion-unicode.json). PDF text extraction is excluded from the multilingual round-trip assertion because glyph extraction and reflow can differ.", "",
              "## Fixes from this audit", "",
              "- Removed HTML styles/scripts from text/EPUB extraction and avoided Word's hidden HTML-export security dialog for HTM/DOC/RTF routes. Local HTML assets are resolved against the original input.",
              "- Preserved Unicode in legacy Word TXT output and spreadsheet TSV output. UTF-8 TXT/CSV/TSV without a BOM now opens correctly in Office through a normalized temporary copy; TSV imports explicitly use tabs to preserve separate columns.",
              "- Removed Word table control characters that made EPUB XML invalid; EPUB titles retain the original document filename.",
              "- Rejected empty text extraction from image-only PDFs with a useful message; removed the extra trailing page in scanned PDF → DOCX exports.", "",
              "## Source limits and remaining scope", "",
              "Four image-only PDF routes (TXT, Markdown, EPUB, XLSX) correctly request extractable text/OCR instead of reporting blank success. PNG/JPG, DOCX page images and PPTX page images succeed. One silent-video → MP3 case correctly reports that the source contains no audio.", "",
              "These results cover every conversion offered by this build using generated fixtures. They do not establish perfect behavior for arbitrary files, codecs, protected files, complex layouts, or full Mac parity. Office routes require locally installed Word, Excel or PowerPoint. OCR and Apple Pages conversion are unavailable; CSV/TSV exports contain the active sheet. See [feature limits](FEATURE-MATRIX.md).", "",
              "## Reproduce and inspect", "",
              "```powershell", "$env:TANGERINE_RAR_FIXTURE = 'C:\\path\\to\\fixture.rar'",
              ".\\scripts\\test.ps1 -ConversionsOnly -ResultRoot D:\\TangerineConversionCheck", "```", "",
              "Run in PowerShell 7 with the configured published app. The app's `--conversion-matrix <fresh-directory>` command supports `--only` with category, extension or scenario filters. Final acceptance must use the full matrix.", "",
              f"Input extensions: {', '.join(extensions)}.", "",
              f"Published executable: `{args.app_path}`.",
              f"Application DLL SHA-256: `{app_hash}`.",
              f"Complete output files: `{args.results.resolve()}`.", "",
              "[Every case (CSV)](conversion-results.csv) · [Raw results](conversion-results.json) · [Plan](conversion-plan.json) · [Summary](conversion-summary.json) · [Execution metadata](conversion-execution.json)", "",
              "The RAR fixture is the SharpCompress `Rar.rar` recorded in [the earlier verification report](VERIFICATION.md). No personal input files were used. Earlier workflow/desktop checks remain separate and are not added to these conversion counts.", ""]
    (args.report_dir / "CONVERSION-REPORT.md").write_text("\n".join(lines), encoding="utf-8")
    write_json(args.report_dir / "conversion-timings.json", timings)
    print(json.dumps({"passed": summary["passed"], "correctRejections": summary["sourceLimits"],
                      "standardRoutes": len(standard), "inputExtensions": len(extensions),
                      "independentUnicodeOutputs": len(unicode_rows), "report": str(args.report_dir / "CONVERSION-REPORT.md")}, indent=2))


if __name__ == "__main__":
    main()
