#!/usr/bin/env python3
"""Read-only Phase 1 Telegram raw-data audit. Python standard library only."""
import argparse
import collections
import json
from pathlib import Path


def audit(document):
    results = document.get("results", document)
    messages = results.get("rawTelegramMessages")
    if not isinstance(messages, list):
        raise ValueError("Missing results.rawTelegramMessages")
    by_channel = collections.Counter(m.get("channel") for m in messages)
    empty = sum(not (m.get("rawMessage") or "").strip() for m in messages)
    return {
        "rawMessageCount": len(messages),
        "channelsWithMessages": len(by_channel),
        "emptyRawMessages": empty,
        "nonEmptyRawMessages": len(messages) - empty,
        "parsedAdvertisementsCount": len(results.get("parsedAdvertisements", [])),
        "messagesProcessed": results.get("messagesProcessed"),
        "failedChannels": results.get("failedChannels"),
        "rawCountsByChannel": dict(sorted(by_channel.items())),
        "note": "Read-only: no filtering, no DB calls, no reference-price calculation.",
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("diagnostics_json", type=Path)
    parser.add_argument("--out", type=Path, help="Optional report JSON destination")
    args = parser.parse_args()
    data = json.loads(args.diagnostics_json.read_text(encoding="utf-8"))
    report = audit(data)
    output = json.dumps(report, ensure_ascii=False, indent=2)
    if args.out:
        args.out.write_text(output + "\n", encoding="utf-8")
    print(output)


if __name__ == "__main__":
    main()
