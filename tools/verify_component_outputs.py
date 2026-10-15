"""Independently inspect component-authored PDFs produced by the assembled NuGet consumer."""
from hashlib import sha256
from importlib.metadata import version
from pathlib import Path
import argparse
import json
import re

from PIL import Image
from pypdf import PdfReader
from pypdf.generic import ContentStream
import pypdfium2 as pdfium

PAGES = dict(invoice=1, paid=1, empty=1, credit=1, escaped=1, many=4,
             payment=1, notice=1, fixed=2, reflow=3, batch=2)
PAGES.update({'payment-no-classes':1, 'notice-no-inline':1, 'notice-no-classes':1})
BLUE=(49/255,93/255,155/255)
GREEN=(36/255,85/255,65/255)
RUST=(166/255,66/255,50/255)


def compact(text):
    return ''.join(text.split())


def verify(directory):
    directory=Path(directory).resolve()
    evidence=json.loads((directory/'evidence.json').read_text(encoding='utf-8-sig'))
    assert evidence['Schema']=='fullbleed.dotnet.components.v1'
    cases=evidence['Cases']
    assert len(cases)==len(PAGES) and {case['Name'] for case in cases}==set(PAGES)
    records=[]
    for case in cases:
        name=case['Name'];path=directory/case['Pdf'];data=path.read_bytes()
        assert sha256(data).hexdigest()==case['PdfSha256'],name
        reader=PdfReader(path)
        assert case['Pages']==PAGES[name] and len(reader.pages)==PAGES[name],(name,len(reader.pages),PAGES[name])
        text='\n'.join(page.extract_text() for page in reader.pages)
        for expected in case['ExpectedText']:
            assert compact(expected) in compact(text),(name,expected)
        if name=='many':
            assert re.findall(r'Deliverable(\d{2})',compact(text))==[f'{i:02}' for i in range(1,41)]
        if name=='empty':assert '$0.00' in text
        if name=='credit':assert '-$50.00' in text
        if name=='escaped':assert 'A & <Client> "Café"' in text
        colors=[];sizes=[]
        for page in reader.pages:
            for operands,operation in ContentStream(page.get_contents(),reader).operations:
                if operation in (b'rg',b'RG'):colors.append(tuple(map(float,operands)))
                if operation==b'Tf':sizes.append(float(operands[1]))
        def color(expected):return any(max(abs(a-b) for a,b in zip(value,expected))<.003 for value in colors)
        def size(expected):return any(abs(value-expected)<.01 for value in sizes)
        if name=='payment':assert color(BLUE) and size(18)
        if name=='payment-no-classes':assert not color(BLUE) and not size(18)
        if name=='notice':assert color(BLUE) and color(RUST) and not color(GREEN) and size(14)
        if name=='notice-no-inline':assert color(BLUE) and color(GREEN) and not color(RUST) and size(14)
        if name=='notice-no-classes':assert not color(BLUE) and color(RUST) and not size(14)
        native=[]
        assert len(case['Previews'])==PAGES[name]
        for record,page in zip(case['Previews'],reader.pages,strict=True):
            preview=directory/record['Path'];digest=sha256(preview.read_bytes()).hexdigest()
            assert digest==record['Sha256']
            with Image.open(preview) as image:
                expected=(round(float(page.mediabox.width)*96/72),round(float(page.mediabox.height)*96/72))
                assert all(abs(a-b)<=1 for a,b in zip(image.size,expected)),(name,image.size,expected)
                assert any(low!=high for low,high in image.convert('RGB').getextrema()),name
            native.append(digest)
        pixels=[];geometry=[];independent_text=[]
        with pdfium.PdfDocument(path) as document:
            for number,page in enumerate(document,1):
                textpage=page.get_textpage()
                try:
                    independent_text.append(textpage.get_text_range())
                    width,height=page.get_size();bounds=[]
                    for index in range(textpage.count_chars()):
                        if not textpage.get_text_range(index,1).strip():continue
                        left,bottom,right,top=textpage.get_charbox(index)
                        assert -1<=left<=right<=width+1 and -1<=bottom<=top<=height+1,(name,number,(left,bottom,right,top))
                        bounds.append([round(value,3) for value in (left,bottom,right,top)])
                    geometry.append(bounds)
                    bitmap=page.render(scale=96/72)
                    try:
                        image=bitmap.to_pil().convert('RGB')
                        image.save(directory/f'pdfium-{name}-{number}.png')
                        pixels.append(sha256(image.tobytes()).hexdigest())
                    finally:bitmap.close()
                finally:textpage.close();page.close()
        for expected in case['ExpectedText']:
            assert compact(expected) in compact(''.join(independent_text)),(name,'PDFium',expected)
        record={'name':name,'pages':len(reader.pages),'pdfSha256':sha256(data).hexdigest(),
                'previewsSha256':native,'pdfiumPixelsSha256':pixels,'text':text,
                'pdfiumText':independent_text,'glyphBoundsSha256':sha256(json.dumps(geometry,separators=(',',':')).encode()).hexdigest(),
                'fontSizes':sorted(set(sizes)),'rgbColors':sorted(set(colors))}
        if case['Mode']!='batch':
            for source in ('Html','Css'):
                digest=sha256((directory/case[source]).read_bytes()).hexdigest()
                assert digest==case[source+'Sha256'],(name,source)
                record[source.lower()+'Sha256']=digest
        records.append(record)
    result={'ok':True,'packageVersion':evidence['PackageVersion'],'cases':records,
            'readers':{name:version(name) for name in ('pypdf','pypdfium2','pillow')},
            'scope':'Fourteen component fixtures: ordinary/compiled/batch output, text and glyph bounds, previews, class/descendant selectors and inline override controls.'}
    (directory/'verification.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    return result


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path);args=parser.parse_args()
    report=verify(args.directory)
    print(json.dumps({'ok':report['ok'],'cases':len(report['cases']),'pages':sum(case['pages'] for case in report['cases'])}))
