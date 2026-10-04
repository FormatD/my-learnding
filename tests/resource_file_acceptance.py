"""Real private resource upload, frozen approval and narrowly scoped child delivery."""
import base64,copy,datetime,hashlib,io,json,os,re,secrets,subprocess,tempfile,zipfile,urllib.request,urllib.error,uuid
from pathlib import Path
from api_acceptance import Client,TODAY
import api_acceptance

PNG=base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXioAAAAASUVORK5CYII=')

def verify(c,env,credentials):
    assert re.fullmatch('learning_fault_openapi_[0-9a-f]{12}',env['PGDATABASE'])
    def sql(q):return subprocess.check_output(['psql','-Atc',q],env=env,text=True).strip()
    def upload(data=PNG,mime='image/png',name='受控学习图.png',expected=201):
        return c.request('/content/resource-files',{'name':name,'mimeType':mime,'base64':base64.b64encode(data).decode()},expected=expected)
    def read(client,path,expected=200,headers=None):
        req=urllib.request.Request(api_acceptance.BASE+path,headers={'X-Learning-Request':'1',**(headers or {})})
        try:r=client.http.open(req)
        except urllib.error.HTTPError as e:r=e
        data=r.read();assert r.status==expected,(path,r.status,data[:600]);return data,r.headers
    def child_for(sid):
        child=Client()
        for cookie in c.jar:child.jar.set_cookie(copy.copy(cookie))
        child.etag=c.etag;child.request('/students/'+sid+'/child-sessions',{});return child
    def draft(cat):return c.request('/content/drafts',{'title':'受控私有材料验收','catalog':cat})
    def publish(d):
        c.request('/content/drafts/'+d['id']+':review',{});p=c.request('/content/drafts/'+d['id']+'/preview');return c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']})
    if os.environ.get('RESOURCE_FILE_BROWSER')=='1':
        browser_env=env.copy();browser_env['LEARNING_TEST_URL']=api_acceptance.BASE.rsplit('/api/v1',1)[0]
        subprocess.run(['npm','run','test:e2e','--','tests/resource-files.spec.ts','--workers=1'],cwd=Path(__file__).resolve().parents[1]/'src/web',env=browser_env,check=True)
    first=upload();assert first['hash']==hashlib.sha256(PNG).hexdigest()
    count=sql('SELECT COUNT(*) FROM "PrivateFile"');previous=c.etag;c.request('/students',{'name':'上传版本冲突控制'},expected=201);fresh=c.etag;c.etag=previous
    upload(expected=412);c.etag=fresh;assert sql('SELECT COUNT(*) FROM "PrivateFile"')==count
    upload(b'not a png',expected=422);c.request('/content/resource-files',{'name':'bad','mimeType':'image/png','base64':'?'},expected=422)
    # The signature gate deliberately makes no claim to parse full multimedia content.
    for data,mime,name in [(b'%PDF-1.4\n%%EOF','application/pdf','fixture.pdf'),(b'\xff\xd8\xff\xe0','image/jpeg','fixture.jpg'),(b'RIFF'+(36).to_bytes(4,'little')+b'WAVEfmt ','audio/wav','fixture.wav'),(b'ID3\x04\x00\x00\x00\x00\x00\x00','audio/mpeg','fixture.mp3')]:
        f=upload(data,mime,name);assert read(c,'/files/'+f['id'])[0]==data
    private=c.request('/files',{'name':'个人错题.png','mimeType':'image/png','base64':base64.b64encode(PNG).decode()},expected=201)
    source=c.request('/content/fixture',{});cat=json.loads(source['payload'])
    for k in cat['kcs']:k['code']='RESOURCE.FILE.'+uuid.uuid4().hex
    resource=cat['resources'][0];resource.update(fileId=first['id'],fileSnapshotHash=first['fileSnapshotHash'],paperReference='',url=None)
    def rejected(modify):
        bad=copy.deepcopy(cat);modify(bad['resources'][0]);d=draft(bad);c.request('/content/drafts/'+d['id']+':review',{},expected=422)
    rejected(lambda r:r.update(fileId=private['id']))
    rejected(lambda r:r.update(fileSnapshotHash='0'*64))
    rejected(lambda r:r.update(fileId=str(uuid.uuid4())))
    rejected(lambda r:r.pop('fileSnapshotHash'))
    rejected(lambda r:r.update(revisionId=None))
    d=draft(cat);release=publish(d)
    sid=c.request('/students',{'name':'文件学习学生'},expected=201)['id']
    other=c.request('/students',{'name':'文件隔离学生'},expected=201)['id']
    def plan_for(student,day=TODAY):
        c.request('/students/'+student+'/content/'+release['id']+':bind',{})
        plan=c.request('/students/'+student+'/plans/'+day+':generate',{})
        task=c.request('/plans/'+plan['id']+'/tasks',{'title':'受控文件阅读','minutes':5,'resourceRef':'不可冒充文件','type':'Resource','resourceId':resource['id']})
        return plan,task
    plan,task=plan_for(sid);path='/tasks/'+task['id']+'/resource-file'
    assert task['resourceRevisionId']==resource['revisionId'] and task['resourceId']==resource['id'] and task['resourceUrl']=='/api/v1'+path
    assert c.request('/plans/'+plan['id']+'/resources')==cat['resources']
    read(c,path,409)
    c.request('/plans/'+plan['id']+'/tasks',{'title':'非法跨版本材料','minutes':5,'resourceRef':'','type':'Resource','resourceId':str(uuid.uuid4())},expected=422)
    c.request('/plans/'+plan['id']+'/tasks',{'title':'非法任务类型','minutes':5,'resourceRef':'','type':'Schoolwork','resourceId':resource['id']},expected=422)
    def activate(student,p,day=TODAY):
        v=c.request('/students/'+student+'/plans/'+day);c.request('/plans/'+p['id']+':publish',{'previewHash':v['revision']['inputHash'],'confirmWarnings':True})
    activate(sid,plan);child=child_for(sid);wrong=child_for(other)
    assert read(child,path)[0]==PNG
    document=c.request('/openapi.json');operation=document['paths']['/api/v1/tasks/{id}/resource-file']['get'];assert {'200','206','416'}<=operation['responses'].keys() and any(p['name']=='Range' and not p.get('required',False) for p in operation['parameters'])
    empty,invalid_headers=read(child,path,416,{'Range':'bytes=999999-'});assert empty==b'' and invalid_headers['Content-Range']==f'bytes */{len(PNG)}'
    part,headers=read(child,path,206,{'Range':'bytes=0-7'});assert part==PNG[:8] and headers['Content-Range'].startswith('bytes 0-7/')
    read(child,'/files/'+first['id'],404);read(child,'/files/'+private['id'],404);read(wrong,path,404)
    child.request('/me');child.request('/content/resource-files',{'name':'x','mimeType':'image/png','base64':base64.b64encode(PNG).decode()},expected=403)
    child.request('/plans/'+plan['id']+'/resources',expected=403)
    c.request('/me')
    exported=c.request('/students/'+sid+'/export');assert any(f['id']==first['id'] and base64.b64decode(f['bytes'])==PNG and f['purpose']=='LearningResource' for f in exported['files'])
    assert all(f['id']!=private['id'] for f in exported['files'])
    c.request('/tasks/'+task['id']+':transition',{'status':'InProgress'});child.request('/tasks/'+task['id']+':transition',{'status':'Completed'})
    after=c.request('/students/'+sid+'/export');assert after['evidence']==exported['evidence']==[] and after['attempts']==[]
    # An actual later file/release binding cannot silently replace an old task's bytes.
    second=upload(PNG+b'new');newcat=copy.deepcopy(cat);newcat['resources'][0].update(fileId=second['id'],fileSnapshotHash=second['fileSnapshotHash'],revisionId=str(uuid.uuid4()))
    newer=publish(draft(newcat));c.request('/students/'+sid+'/content/'+newer['id']+':bind',{});assert read(child,path)[0]==PNG
    assert c.request('/plans/'+plan['id']+'/resources')==cat['resources']
    # Byte tampering and metadata tampering are isolated fault injections, always restored.
    fid=str(uuid.UUID(first['id']))
    try:
        sql(f'''UPDATE "PrivateFile" SET "Bytes"=decode('0001','hex') WHERE "Id"='{fid}';''');read(child,path,422)
    finally:sql(f'''UPDATE "PrivateFile" SET "Bytes"=decode('{PNG.hex()}','hex') WHERE "Id"='{fid}';''')
    try:
        sql(f'''UPDATE "PrivateFile" SET "Name"='changed.png' WHERE "Id"='{fid}';''');read(child,path,422)
    finally:sql(f'''UPDATE "PrivateFile" SET "Name"='受控学习图.png' WHERE "Id"='{fid}';''')
    assert read(child,path)[0]==PNG
    tomorrow=(datetime.date.fromisoformat(TODAY)+datetime.timedelta(days=1)).isoformat();future_plan,future_task=plan_for(sid,tomorrow);activate(sid,future_plan,tomorrow);read(child,'/tasks/'+future_task['id']+'/resource-file',409)
    foreign=Client();foreign.request('/auth/register',{'userName':'resource-foreign-'+uuid.uuid4().hex,'password':secrets.token_hex(20)},expected=201)
    read(foreign,path,404);read(foreign,'/files/'+first['id'],404)
    bad=foreign.request('/content/drafts',{'title':'跨家庭不能绑定文件','catalog':cat});failure=foreign.request('/content/drafts/'+bad['id']+':review',{},expected=422);assert failure['code']=='RESOURCE_FILE_UNKNOWN'
    c.request('/me');shared_plan,shared_task=plan_for(other);activate(other,shared_plan)
    c.request('/students/'+sid+'/paper-wrongs',{'stem':'受控纸质错题','answer':'1','fileId':first['id']},expected=201)
    archive=zipfile.ZipFile(io.BytesIO(read(c,'/family/export')[0]));manifest=json.loads(archive.read('manifest.json'));entry=next(f for f in manifest['files'] if f['id']==first['id']);assert entry['purpose']=='LearningResource' and archive.read(entry['archivePath'])==PNG
    verify_restore(c,env,first,task,shared_task,sid)
    preview=c.request('/students/'+sid+'/delete-preview');c.request('/students/'+sid+':delete',{'previewHash':preview['previewHash'],'password':credentials['password'],'confirm':'永久删除学生'})
    c.request('/me');assert read(c,'/tasks/'+shared_task['id']+'/resource-file')[0]==PNG
    read(child,path,401)
    c.request('/content/releases/'+release['id']+':withdraw',{'reason':'受控撤回验证'});read(c,'/tasks/'+shared_task['id']+'/resource-file',422)
    print('PASS actual private resource five format signatures/version conflict, personal file exclusion, frozen reviewed file snapshot, exact plan revision binding, child own active task and byte ranges, general child file denial/other student404, student and family export bytes/purpose, deletion retains shared family material/revokes child, resource completion without evidence, new release retains old task bytes, byte/metadata corruption422 and withdrawal422')


def verify_restore(c,env,file,task,shared_task,sid):
    root=Path(__file__).resolve().parents[1];family=c.request('/me')['family']['id'];restores=[]
    def sql(q,database):return subprocess.check_output(['psql','-At','-v','ON_ERROR_STOP=1','-c',q],env=env|{'PGDATABASE':database},text=True).strip()
    with tempfile.TemporaryDirectory(prefix='resource-restore-',dir='/private/tmp') as directory:
        tmp=Path(directory);(tmp/'gpg').mkdir(mode=0o700);students=tmp/'students.txt';families=tmp/'families.txt';students.touch(mode=0o600);families.touch(mode=0o600)
        backup_env=env|{'PATH':'/opt/homebrew/bin:'+env['PATH'],'BACKUP_DIR':str(tmp/'archives'),'BACKUP_PASSPHRASE':secrets.token_hex(32),'GNUPGHOME':str(tmp/'gpg'),'DELETION_LEDGER':str(students),'FAMILY_DELETION_LEDGER':str(families)}
        def restore():
            database='learning_restore_resource_'+uuid.uuid4().hex[:12];restores.append(database);subprocess.run(['createdb',database],env=env,check=True,stdout=subprocess.DEVNULL)
            subprocess.run(['sh',str(root/'scripts/restore.sh'),str(archive)],env=backup_env|{'PGDATABASE':database},check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);return database
        try:
            subprocess.run(['sh',str(root/'scripts/backup.sh')],env=backup_env,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);archive=next((tmp/'archives').glob('*.gpg'))
            restored=restore()
            for table,where in [('PrivateFile',f"\"Id\"='{file['id']}'"),('Tasks',f"\"Id\" IN ('{task['id']}','{shared_task['id']}')")]:
                query=f'SELECT jsonb_agg(to_jsonb(t) ORDER BY t."Id") FROM "{table}" t WHERE {where}';assert sql(query,env['PGDATABASE'])==sql(query,restored)
            students.write_text(f'{family},{sid}\n');deleted=restore()
            assert sql(f"SELECT COUNT(*) FROM \"Students\" WHERE \"Id\"='{sid}'",deleted)=='0'
            assert sql(f"SELECT \"Purpose\" FROM \"PrivateFile\" WHERE \"Id\"='{file['id']}'",deleted)=='LearningResource'
            assert sql(f"SELECT \"ResourceRevisionId\" FROM \"Tasks\" WHERE \"Id\"='{shared_task['id']}'",deleted)==shared_task['resourceRevisionId']
            families.write_text(f'{family},{uuid.uuid4()}\n');removed=restore();assert sql(f"SELECT COUNT(*) FROM \"PrivateFile\" WHERE \"FamilyId\"='{family}'",removed)=='0'
            print('PASS actual encrypted backup restores resource bytes/purpose and fixed task references exactly; latest student deletion retains shared learning file, family deletion removes it; temporary same-disk test does not prove independent disk or disaster RTO')
        finally:
            for database in restores:subprocess.run(['dropdb','--if-exists','--force',database],env=env,check=True,stdout=subprocess.DEVNULL)
            subprocess.run(['gpgconf','--kill','gpg-agent'],env=backup_env,check=True,stdout=subprocess.DEVNULL)
