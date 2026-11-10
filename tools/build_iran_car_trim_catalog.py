#!/usr/bin/env python3
"""Build an auditable Iranian car model/trim catalog from public model pages.

Usage: python tools/build_iran_car_trim_catalog.py --output CarShow/Data/iran-car-trims.json
Uses only Python standard library. Respect site terms and crawl rate.
"""
import argparse
import html
import json
import re
import time
from urllib.parse import urljoin, urlparse
from urllib.request import Request, urlopen

BASE = "https://nerkhehrooz.com"
INDEX = BASE + "/cars/specs"
MODEL_URL = re.compile(r'^/cars/specs/([^/?#]+)/([^/?#]+)$')
ANCHOR = re.compile(r'<a\b[^>]*href=["\']([^"\']+)["\'][^>]*>(.*?)</a>', re.I | re.S)
TAG = re.compile(r'<[^>]+>')
HEADING = re.compile(r'<h[123]\b[^>]*>(.*?)</h[123]>', re.I | re.S)
PAGES = 25

def fetch(url):
    request = Request(url, headers={"User-Agent": "CarShowCatalogBuilder/1.0 (low-rate research)"})
    with urlopen(request, timeout=30) as response:
        return response.read().decode("utf-8", errors="replace")

def clean(value):
    value = TAG.sub(" ", value)
    return re.sub(r'\s+', ' ', html.unescape(value)).strip()

def model_links(page):
    found = set()
    for href, _ in ANCHOR.findall(page):
        url = urljoin(BASE, html.unescape(href))
        path = urlparse(url).path
        if MODEL_URL.match(path):
            found.add(url)
    return found

def parse_model(page, url):
    match = MODEL_URL.match(urlparse(url).path)
    if not match:
        return []
    brand_slug, model_slug = match.groups()
    title = re.search(r'<h1\b[^>]*>(.*?)</h1>', page, re.I | re.S)
    display = clean(title.group(1)) if title else model_slug
    trims = []
    # Each trim on the model detail page is presented as an H3 heading.
    for heading in HEADING.findall(page):
        name = clean(heading)
        if not name or name.startswith(("چرا ", "مشخصات ")):
            continue
        # Only H3 names inside the "تیپ‌های" section qualify.
        trims.append(name)
    section = re.search(r'تیپ‌های.*?(?=چرا انتخاب تیپ|دیگر مدل‌های|$)', page, re.S)
    if section:
        trims = [clean(x) for x in re.findall(r'<h3\b[^>]*>(.*?)</h3>', section.group(), re.I | re.S)]
    else:
        trims = []
    return [{"brand": brand_slug, "model": display, "trim": trim,
             "source": url, "aliases": [], "verified": False} for trim in dict.fromkeys(trims)]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="iran-car-trims.json")
    parser.add_argument("--delay", type=float, default=1.0)
    args = parser.parse_args()
    links = set()
    for page_number in range(1, PAGES + 1):
        url = INDEX if page_number == 1 else INDEX + "?page=" + str(page_number)
        try:
            links.update(model_links(fetch(url)))
        except Exception as exc:
            print("Index failed:", url, exc)
        time.sleep(args.delay)
    entries = []
    failures = []
    for url in sorted(links):
        try:
            entries.extend(parse_model(fetch(url), url))
        except Exception as exc:
            failures.append({"url": url, "error": str(exc)})
        time.sleep(args.delay)
    entries.sort(key=lambda x: (x["brand"], x["model"], x["trim"]))
    with open(args.output, "w", encoding="utf-8") as stream:
        json.dump({"source": INDEX, "count": len(entries), "records": entries,
                   "failedPages": failures,
                   "note": "Unverified scraped candidates; review before enabling as authoritative trim matches."},
                  stream, ensure_ascii=False, indent=2)
    print(f"models={len(links)} trims={len(entries)} failures={len(failures)}")
    if failures:
        raise SystemExit("Partial crawl; inspect failedPages and retry.")

if __name__ == "__main__":
    main()
