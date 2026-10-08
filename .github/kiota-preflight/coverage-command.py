import os, pathlib, re, shlex, subprocess, xml.etree.ElementTree as E
root=pathlib.Path.cwd()
kind=os.environ['TEST_KIND']
expected=os.environ['TEST_SHA']
actual=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
assert actual==expected,(actual,expected)
print(actual,flush=True)
if kind=='ado':
    source=(root/'.azure-pipelines/ci-build.yml').read_text()
    line=next(line.strip() for line in source.splitlines() if 'arguments:' in line and '--coverlet ' in line)
    arguments=line.split('arguments:',1)[1].strip()[1:-1]
    command='dotnet test kiota.slnx '+arguments.replace('$(BuildConfiguration)','Release').replace('$(Agent.TempDirectory)','./TestResults')
    command=command.replace(' --report-azdo','').replace(' --publish-azdo-test-results','')
    os.environ['TF_BUILD']='True'
    print('Azure result publishing requires upstream credentials; validating its Windows Release test/coverage command without result publishing.',flush=True)
else:
    source=(root/'.github/workflows/dotnet.yml').read_text()
    command=next(line.strip().split('run:',1)[1].strip() for line in source.splitlines() if 'run: dotnet test kiota.slnx' in line)
print('Executing:',command,flush=True)
subprocess.run(shlex.split(command),check=True)
reports=list((root/'TestResults').rglob('coverage.cobertura.*.xml'))
assert len(reports)==3,reports
roots=[E.parse(p).getroot() for p in reports]
for p,r in zip(reports,roots): print(p.name,r.attrib,flush=True)
packages=[p for r in roots for p in r.findall('packages/package')]
if kind=='baseline':
    assert not packages and sum(int(r.get('lines-valid','0')) for r in roots)==0,[p.attrib for p in packages]
    print('REPRODUCED: baseline reports zero production assemblies despite passing tests.',flush=True)
else:
    assert all(int(r.get('lines-valid','0'))>0 and int(r.get('lines-covered','0'))>0 for r in roots),[r.attrib for r in roots]
    names={p.get('name') for p in packages}
    assert {'Kiota.Builder','kiota'}<=names,names
    print('VERIFIED: unchanged tests collect non-empty Kiota.Builder and CLI coverage.',flush=True)
