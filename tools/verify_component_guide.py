"""Compile and render the actual README and component-guide snippets using an isolated package reference."""
from hashlib import sha256
from pathlib import Path
import argparse
import json
import os
import re
import subprocess
import tempfile

from pypdf import PdfReader
from pypdf.generic import ContentStream

ROOT=Path(__file__).resolve().parents[1]


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--packages',type=Path,default=ROOT/'artifacts/packages')
    parser.add_argument('--output',type=Path,default=ROOT/'artifacts/component-guide')
    args=parser.parse_args()
    manifest=json.loads((args.packages/'package-manifest.json').read_text())
    version=manifest['version'];assert re.fullmatch(r'\d+\.\d+\.\d+',version)
    package=args.packages/f'FullBleed.DotNet.{version}.nupkg'
    assert sha256(package.read_bytes()).hexdigest()==manifest['package']['sha256']
    args.output.mkdir(parents=True,exist_ok=True)
    source={name:re.findall(r'```csharp\n(.*?)```',(ROOT/path).read_text(encoding='utf-8'),re.S)
            for name,path in [('readme','README.md'),('guide','docs/components.md')]}
    assert len(source['guide'])==6
    source['readme']=source['readme'][:1]
    env={**os.environ,'DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE':'true'}
    env.pop('FULLBLEED_NATIVE_LIBRARY',None)
    reports=[]
    for name,blocks in source.items():
        consumer=Path(tempfile.mkdtemp(prefix='fullbleed-component-guide-'))
        (consumer/'global.json').write_text('{"sdk":{"version":"8.0.100","rollForward":"latestFeature","allowPrerelease":false}}')
        (consumer/'Consumer.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><RollForward>LatestPatch</RollForward><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
<ItemGroup><PackageReference Include="FullBleed.DotNet" Version="{version}" /></ItemGroup></Project>''')
        actual='\n'.join(blocks)
        if name=='guide':
            actual+='''
payment.RenderPdfToFile(engine, "output/payment.pdf");
styledNotice.RenderPdfToFile(engine, "output/notice.pdf");
payment.RenderImagePagesToDirectory(engine, "output/payment-preview", dpi: 96);
styledNotice.RenderImagePagesToDirectory(engine, "output/notice-preview", dpi: 96);
'''
        actual+=f'\nif (FullBleedEngine.GetNativeFeatures().BindingVersion != "{version}") throw new Exception("Wrong packaged native library");\n'
        (consumer/'Program.cs').write_text(actual,encoding='utf-8')
        target=args.output/name;target.mkdir(exist_ok=True)
        (target/'Program.cs').write_text(actual,encoding='utf-8')
        commands=[['dotnet','restore','Consumer.csproj','--source',str(args.packages.resolve()),'--packages',str(consumer/'packages'),'--force-evaluate'],
                  ['dotnet','run','--project','Consumer.csproj','-c','Release','--no-restore']]
        for index,command in enumerate(commands):
            result=subprocess.run(command,cwd=consumer,env=env,capture_output=True,text=True,encoding='utf-8')
            (target/f'{index}.log').write_text(result.stdout+result.stderr,encoding='utf-8')
            assert result.returncode==0,(name,command,(result.stdout+result.stderr)[-2000:])
        files=[]
        for path in (consumer/'output').rglob('*'):
            if not path.is_file():continue
            relative=path.relative_to(consumer/'output');destination=target/relative
            destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(path.read_bytes())
            files.append({'path':relative.as_posix(),'sha256':sha256(path.read_bytes()).hexdigest()})
        if name=='readme':
            reader=PdfReader(target/'report.pdf');assert len(reader.pages)==1
            text=''.join(reader.pages[0].extract_text().split())
            assert 'Quarterlyreport' in text and 'Deterministicprintoutput.' in text
        else:
            assert len(PdfReader(target/'invoice.pdf').pages)==1
            assert len(PdfReader(target/'batch.pdf').pages)==2
            for fixture in ('payment','notice'):
                reader=PdfReader(target/(fixture+'.pdf'));assert len(reader.pages)==1
                colors=[];sizes=[]
                for operands,operation in ContentStream(reader.pages[0].get_contents(),reader).operations:
                    if operation in (b'rg',b'RG'):colors.append(tuple(map(float,operands)))
                    if operation==b'Tf':sizes.append(float(operands[1]))
                assert any(max(abs(a-b) for a,b in zip(color,(49/255,93/255,155/255)))<.003 for color in colors)
                assert (18 if fixture=='payment' else 14) in sizes
                if fixture=='notice':assert any(max(abs(a-b) for a,b in zip(color,(166/255,66/255,50/255)))<.003 for color in colors)
        reports.append({'name':name,'consumer':str(consumer),'blocks':len(blocks),
                        'source_sha256':sha256(actual.encode()).hexdigest(),'files':files})
    receipt={'ok':True,'version':version,'package_sha256':manifest['package']['sha256'],'reports':reports,
             'scope':'Actual README quickstart and all six component-guide C# blocks compile and render against the isolated NuGet; styling colors/sizes and PDF page counts independently checked.'}
    (args.output/'verification.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'ok':True,'projects':len(reports),'source_blocks':sum(r['blocks'] for r in reports)}))


if __name__=='__main__':main()
