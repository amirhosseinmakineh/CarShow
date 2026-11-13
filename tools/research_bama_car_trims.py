#!/usr/bin/env python3
"""Cross-check Iranian brand/model/trim names against public Bama technical pages.
Only follows trim-navigation links on public technical spec pages.\nDoes not collect individual seller advertisements, PII, or prices.
"""
import argparse,json,re,time
from pathlib import Path
from datetime import datetime,timezone
from urllib.parse import urljoin,urlparse
import requests
from bs4 import BeautifulSoup

BASE="https://bama.ir"
INDEX=BASE+"/car-reviews"
DETAIL=re.compile(r"^/car-reviews/([^/]+)/([^/]+-specs-[^/]+)/?$")
HEADING=re.compile(r"^تریم[\u200c\s-]*های\s+(.+)$")
def urls(raw):
    out={}
    for a in BeautifulSoup(raw,"html.parser").select("a[href]"):
        p=urlparse(urljoin(INDEX,a["href"])).path
        if DETAIL.match(p):out[BASE+p.rstrip("/")]=a.get_text(" ",strip=True)
    return out
def parse(raw,url,stamp):
    soup=BeautifulSoup(raw,"html.parser")
    h1=soup.find("h1")
    m=DETAIL.match(urlparse(url).path)
    if not h1 or not m:return []
    brand=m.group(1)
    title=h1.get_text(" ",strip=True).removeprefix("مشخصات فنی ").strip()
    title=re.sub(r"\s+سال\s+(?:\d{4}|[۰-۹]{4})\s*[-–].*","",title)
    h3=next((h for h in soup.find_all(["h3","h2"]) if HEADING.match(h.get_text(" ",strip=True))),None)
    rows=[]
    if h3:
        family=HEADING.match(h3.get_text(" ",strip=True)).group(1).strip()
        seen=set()
        for e in h3.next_elements:
            if getattr(e,"name",None) in ("h2","h3","h4"):break
            if getattr(e,"name",None)!="a" or not e.has_attr("href"):continue
            target=urljoin(url,e["href"])
            if not DETAIL.match(urlparse(target).path):continue
            trim=e.get_text(" ",strip=True)
            if not trim or trim in seen:continue
            seen.add(trim)
            rows.append({"brand":brand,"model":family,"trim":trim,"fullName":family+" "+trim,
            "source":target,"quality":"bama-technical-trim","observedAt":stamp,
            "note":"Published Bama technical trim navigation; confirm against other sources"})
    if not rows:
        rows.append({"brand":brand,"model":title,"trim":None,"fullName":title,"source":url,
        "quality":"bama-title-unparsed","observedAt":stamp,"note":"No explicit trim listing; do not infer trim"})
    return rows
def main():
    p=argparse.ArgumentParser()
    p.add_argument("--delay",type=float,default=.75)
    p.add_argument("--max-pages",type=int,default=0)
    args=p.parse_args()
    session=requests.Session()
    session.headers.update({"User-Agent":"CarShowPublicCatalogResearch/1.0 (technical pages)"})
    def get(url):
        err=None
        for i in range(3):
            try:
                r=session.get(url,timeout=25)
                r.raise_for_status()
                time.sleep(args.delay)
                return r.text
            except requests.RequestException as ex:
                err=ex
                time.sleep(2*(i+1))
        raise RuntimeError(str(err))
    discovered=urls(get(INDEX))
    pages=sorted(discovered)
    if args.max_pages:pages=pages[:args.max_pages]
    stamp=datetime.now(timezone.utc).isoformat()
    data=[];errors=[]
    for i,url in enumerate(pages,1):
        try:data+=parse(get(url),url,stamp)
        except Exception as exc:errors.append({"url":url,"error":str(exc)})
        if i%40==0:print("Bama",i,len(pages),"rows",len(data),flush=True)
    dedup={}
    for row in data:
        k=(row["brand"],row["model"],row["trim"])
        dedup.setdefault(k,row)
    summary={"discoveredPages":len(discovered),"scanned":len(pages),
      "rows":len(dedup),"explicitTrims":sum(bool(r["trim"]) for r in dedup.values()),
      "errors":len(errors),"capturedAt":stamp}
    path=Path("CarShow/Data/Research/iran-bama-technical-trims.json")
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps({"summary":summary,"failedUrls":errors,"records":list(dedup.values())},
        ensure_ascii=False,indent=2),encoding="utf8")
    print("SUMMARY",json.dumps(summary,ensure_ascii=False),flush=True)
    if not args.max_pages and (len(discovered)<100 or len(errors)>len(pages)*.2):
        raise SystemExit("Partial Bama coverage; output should not be considered complete")
if __name__=="__main__":main()
