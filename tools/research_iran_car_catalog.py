#!/usr/bin/env python3
"""Source-attributed Iranian car taxonomy research. Output is NOT a verified factory catalog."""
import argparse
import os
import csv
import json
import re
import time
from datetime import datetime, timezone
from pathlib import Path

import requests
from bs4 import BeautifulSoup

ROOT="https://nemuda.com/car"
SPEC="https://nerkhehrooz.com/cars/specs"
BRAND=re.compile(r"^\s*(.+?)،\s*([0-9۰-۹٠-٩٬,]+)\s*تیپ\s*—\s*https://nemuda\.com/car/([a-z0-9-]+)/?\s*$")
ENTRY=re.compile(r"^\s*(.+?)\s*—\s*(https://nemuda\.com/car/[a-z0-9-]+/[a-z0-9-]+)/?\s*$")
MODEL_LINK=re.compile(r"^/cars/specs/[^/?#]+/[^/?#]+/?$")
def num(s):
    return int(s.translate(str.maketrans("۰۱۲۳۴۵۶۷۸۹٠١٢٣٤٥٦٧٨٩","01234567890123456789")).replace("٬","").replace(",",""))
def norm(s):
    return " ".join(str(s or "").replace("ي","ی").replace("ك","ک").replace("\u200c"," ").split()).lower()
def key(x):
    return tuple(norm(x.get(k)) for k in ("brand","model","trim"))
def get_brands(txt):
    started=False
    out=[]
    for line in txt.splitlines():
        if line.strip()=="برندها:":started=True;continue
        if started and line.strip().startswith("بر اساس بودجه:"):break
        if started:
            m=BRAND.match(line)
            if m:out.append({"brand":m.group(1),"expected":num(m.group(2)),"slug":m.group(3)})
    return out
def read_nemuda(txt, b, stamp):
    active=False;families=[];candidates=[]
    for line in txt.splitlines():
        if re.match(r"^تیپ‌های\s+.+\(مدل به مدل\):\s*$",line.strip()):
            active=True;continue
        if not active:continue
        if line.startswith("تیپ‌های ") and "داده" in line:break
        if line.strip().startswith("همهٔ برندهای"):break
        m=ENTRY.match(line)
        if not m or not m.group(2).startswith(ROOT+"/"+b["slug"]+"/"):continue
        label=m.group(1)
        full=re.sub(r"\s*،\s*مدل\s+.*$","",label)
        full=re.sub(r"\s*\([0-9۰-۹٠-٩٬,]+\s*داده\)\s*$","",full).strip()
        if not ("، مدل" in label or "داده)" in label):
            families.append(full)
        else:candidates.append((full,m.group(2)))
    families=sorted(set(families),key=len,reverse=True)
    rows=[]
    for title,url in candidates:
        family=next((f for f in families if title==f or title.startswith(f+" ") or title.startswith(f+" - ")),None)
        suffix=title[len(family):].strip(" -–()") if family else ""
        rows.append({"brand":b["brand"],"model":family or title,"trim":suffix or None,
          "fullName":title,"source":url,"quality":"market-named-trim" if suffix else "market-vehicle-identity",
          "observedAt":stamp,"note":"Public market index; not factory verified"})
    return rows,{"brand":b["brand"],"expected":b["expected"],"families":len(families),
                 "links":len(rows),"namedTrims":sum(bool(x["trim"]) for x in rows)}
def specs_index(txt):
    out=set();soup=BeautifulSoup(txt,"html.parser")
    for a in soup.select("a[href]"):
        href=a.get("href","")
        if MODEL_LINK.match(href):out.add("https://nerkhehrooz.com"+href.rstrip("/"))
    return out
def specs_model(txt,url,stamp):
    soup=BeautifulSoup(txt,"html.parser")
    title=soup.find("h1")
    if not title:return []
    model=re.sub(r"^مشخصات\s*","",title.get_text(" ",strip=True))
    parts=url.split("/")
    slug=parts[-2]
    crumb=soup.select_one('a[href="/cars/specs/'+slug+'"]')
    brand=crumb.get_text(" ",strip=True) if crumb else slug
    h2=next((e for e in soup.find_all("h2") if "تیپ‌های" in e.get_text(" ",strip=True)),None)
    if not h2:return []
    rows=[]
    for e in h2.next_elements:
        if getattr(e,"name",None)=="h2":break
        if getattr(e,"name",None)!="h3":continue
        t=e.get_text(" ",strip=True)
        a=e.find("a",href=True)
        if t:rows.append({"brand":brand,"model":model,"trim":t,"fullName":model+" "+t,
           "source":requests.compat.urljoin(url,a["href"]) if a else url,
           "quality":"technical-catalog-trim","observedAt":stamp,
           "note":"Published trim name; presence in Iranian market/production year needs checking"})
    return rows
def html_page(rows):
    js=json.dumps(rows,ensure_ascii=False,separators=(",",":")).replace("<","\\u003c")
    return '''<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>کاتالوگ پژوهشی تیپ‌های خودرو ایران | CarShow</title><style>
    body{margin:0;background:#f4f7fc;color:#1a3150;font:14px Tahoma,Arial}header{padding:35px;background:#19376d;color:white}main{max-width:1300px;margin:auto;padding:22px}h1{font-size:27px}p{line-height:2}.panel{background:white;padding:18px;border:1px solid #dce5f0;border-radius:12px;margin:13px 0}input,select,button{font:inherit;padding:12px;border-radius:8px;border:1px solid #dce5f0;background:white;margin:5px}button{background:#245fca;color:white;cursor:pointer}table{width:100%;border-collapse:collapse;min-width:800px}td,th{text-align:right;border-bottom:1px solid #e8edf6;padding:12px}th{background:#eff3fa}a{color:#2358ad}.table{overflow-x:auto}#stats{font-weight:bold}span{font-size:12px;color:#66778e}</style></head><body><header><h1>پژوهش تیپ‌های خودرو بازار ایران</h1><p>برند، خانواده، تیپ، منبع و سطح اطمینان — فهرست پژوهشی و نه دیتابیس نهایی قطعی.</p></header><main><div class="panel" id="stats"></div><div class="panel"><b>کنترل کیفیت:</b> سال ساخت از تیپ مستقل است؛ نام مدل به‌عنوان تیپ واقعی شمارش نمی‌شود. دادهٔ استخراج‌شده از آگهی با تأیید فنی کارخانه یکسان نیست و نباید مستقیم برای قیمت‌گذاری استفاده شود.</div><div><input id="q" placeholder="جستجوی خودرو و تیپ"><select id="brand"><option value="">همه برندها</option></select><select id="kind"><option value="">تمام منابع</option></select><button id="json">دریافت JSON</button></div><div class="panel table"><table><thead><tr><th>برند</th><th>مدل</th><th>تیپ</th><th>نوع منبع</th><th>ارجاع</th></tr></thead><tbody id="rows"></tbody></table></div><button id="more">بیشتر</button><span id="count"></span></main><script id="data" type="application/json">'''+js+'''</script><script>
    (()=>{let d=JSON.parse(document.getElementById("data").textContent),$=s=>document.getElementById(s),page=100;let bs=[...new Set(d.map(x=>x.brand))].sort((a,b)=>a.localeCompare(b,"fa")),ks=[...new Set(d.map(x=>x.quality))].filter(Boolean);bs.forEach(x=>$("brand").append(new Option(x,x)));ks.forEach(x=>$("kind").append(new Option(x,x)));$("stats").textContent="رکورد: "+d.length.toLocaleString("fa-IR")+" | دارای نام تیپ: "+d.filter(x=>x.trim).length.toLocaleString("fa-IR")+" | برند: "+bs.length.toLocaleString("fa-IR");function render(){let q=$("q").value.trim().toLowerCase(),b=$("brand").value,k=$("kind").value,rs=d.filter(x=>(!b||x.brand===b)&&(!k||x.quality===k)&&(!q||[x.brand,x.model,x.trim,x.fullName].join(" ").toLowerCase().includes(q)));let tbody=$("rows");tbody.replaceChildren();rs.slice(0,page).forEach(x=>{let tr=document.createElement("tr");[x.brand,x.model,x.trim||"نامشخص",x.quality||"—"].forEach(y=>{let td=document.createElement("td");td.textContent=y;tr.append(td)});let td=document.createElement("td"),a=document.createElement("a");a.href=x.source;a.textContent="مشاهده منبع";a.target="_blank";a.rel="noopener";td.append(a);tr.append(td);tbody.append(tr)});$("count").textContent=page+" / "+rs.length;$("more").disabled=page>=rs.length}["q","brand","kind"].forEach(k=>$(k).addEventListener("input",()=>{page=100;render()}));$("more").onclick=()=>{page+=100;render()};$("json").onclick=()=>{let u=URL.createObjectURL(new Blob([JSON.stringify(d,null,2)],{type:"application/json"})),a=document.createElement("a");a.href=u;a.download="IranCarTrimCatalog.json";a.click();setTimeout(()=>URL.revokeObjectURL(u),1000)};render()})();</script></body></html>'''
def main():
    p=argparse.ArgumentParser()
    p.add_argument("--delay",type=float,default=.5)
    p.add_argument("--nemuda-only",action="store_true")
    p.add_argument("--max-brands",type=int,default=0)
    a=p.parse_args()
    s=requests.Session()
    s.headers.update({"User-Agent":"CarShowCatalogResearch/1.0 public-low-rate"})
    errors=[];reports=[];fresh=[];stamp=datetime.now(timezone.utc).isoformat()
    def get(url):
        for attempt in range(3):
            try:
                r=s.get(url,timeout=30);r.raise_for_status()
                time.sleep(a.delay)
                return r.text
            except requests.RequestException:
                time.sleep(2*(attempt+1))
                if attempt==2:raise
    brands=get_brands(get(ROOT+"/index.txt"))
    if a.max_brands:brands=brands[:a.max_brands]
    for idx,b in enumerate(brands,1):
        url=ROOT+"/"+b["slug"]+"/index.txt"
        try:
            rows,report=read_nemuda(get(url),b,stamp)
            fresh+=rows;reports.append(report)
        except Exception as ex:errors.append({"url":url,"error":str(ex)})
        if idx%20==0:print("Nemuda",idx,len(brands),len(fresh),flush=True)
    tech_links=set()
    if not a.nemuda_only:
        for page in range(1,26):
            url=SPEC if page==1 else SPEC+"?page="+str(page)
            try:tech_links.update(specs_index(get(url)))
            except Exception as ex:errors.append({"url":url,"error":str(ex)})
        for idx,url in enumerate(sorted(tech_links),1):
            try:
                found=specs_model(get(url),url,stamp)
                if not found:errors.append({"url":url,"error":"no h3 trim section"})
                fresh+=found
            except Exception as ex:errors.append({"url":url,"error":str(ex)})
            if idx%50==0:print("Specs",idx,len(tech_links),len(fresh),flush=True)
    baseline="CarShow/Data/Research/iran-car-trim-catalog-expanded.json" if os.path.exists("CarShow/Data/Research/iran-car-trim-catalog-expanded.json") else "CarShow/Data/Research/iran-car-trim-catalog-researched.json"
    before=json.loads(open(baseline,encoding="utf-8").read())["records"]
    merged={}
    for row in before+fresh:
        k=key(row)
        if not k[0] or not k[1]:continue
        if k in merged:
            old=merged[k]
            if old.get("source")!=row.get("source"):
                old["alternateSources"]=sorted(set(old.get("alternateSources",[])+[row.get("source","")]))
        else:merged[k]=row
    rows=list(merged.values())
    rows.sort(key=lambda x:(norm(x.get("brand")),norm(x.get("model")),norm(x.get("trim"))))
    audit={"collectedAt":stamp,"sourceBrandCount":len(brands),"brandReports":reports,
       "totalNamedCandidates":sum(bool(x.get("trim")) for x in rows),"recordCount":len(rows),
       "rawSourceRows":len(fresh),"expectedSourceTrims":sum(b["expected"] for b in brands),
       "specModelPages":len(tech_links),"failedUrls":errors}
    output="CarShow/Data/Research/"
    with open(output+"iran-car-trim-catalog-expanded.json","w",encoding="utf-8") as f:json.dump({"audit":audit,"records":rows},f,ensure_ascii=False,indent=2)
    with open(output+"iran-car-trim-catalog-expanded.html","w",encoding="utf-8") as f:f.write(html_page(rows))
    with open(output+"iran-car-trim-catalog-expanded.csv","w",encoding="utf-8-sig",newline="") as f:
        w=csv.DictWriter(f,fieldnames=["brand","model","trim","fullName","source","quality"],extrasaction="ignore");w.writeheader();w.writerows(rows)
    with open(output+"iran-car-trim-research-audit.json","w",encoding="utf-8") as f:json.dump(audit,f,ensure_ascii=False,indent=2)
    print(json.dumps({k:v for k,v in audit.items() if k not in ("brandReports","failedUrls")},ensure_ascii=False),flush=True)
    if not a.max_brands and (len(brands)<100 or len(reports)<90):
        raise SystemExit("Source incomplete; consult audit. Catalog saved as research candidate only.")
if __name__=="__main__":main()
