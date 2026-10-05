"""Actual restricted upload lifecycle; all tampering/expiry stays in disposable DB."""
import base64,copy,hashlib,io,json,os,re,subprocess,tempfile,secrets,time,urllib.request,urllib.error,uuid,zipfile
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from resource_file_acceptance import PNG

def verify(c,env,credentials):
    assert re.fullmatch('learning_fault_openapi_[0-9a-f]{12}',env['PGDATABASE'])
    def sql(q):return subprocess.check_output(['psql','-X','-At','-v','ON_ERROR_STOP=1','-c',q],env=env,text=True).strip()
    def ticket(data=PNG,purpose='LearningResource',mime='image/png',key=None):
        return c.request('/files/upload-tickets',{'name':'upload.png','mimeType':mime,'size':len(data),'sha256':hashlib.sha256(data).hexdigest(),'purpose':purpose},key=key,expected=201)
    def raw(who,path,data=None,method='GET',token=None,key=None,expected=200,version=None,mime='application/octet-stream'):
        headers={'X-Learning-Request':'1','If-Match':version or who.etag,'Idempotency-Key':key or str(uuid.uuid4()),'Content-Type':mime}
        if token is not None:headers['X-Upload-Credential']=token
        request=urllib.request.Request(api_acceptance.BASE+path,data=data,method=method,headers=headers)
        try:r=who.http.open(request)
        except urllib.error.HTTPError as e:r=e
        content=r.read();assert r.status==expected,(path,r.status,content[:500]);who.etag=r.headers.get('etag') or who.etag
        return json.loads(content) if 'json' in r.headers.get('Content-Type','') and content else content
    def stage(t,data=PNG,expected=200,key=None,who=c,version=None,token=None):return raw(who,'/files/uploads/'+t['fileId'],data,'PUT',t['credential'] if token is None else token,key,expected,version)
    def complete(t,expected=200,key=None,who=c,version=None,token=None):return raw(who,'/files/'+t['fileId']+':complete',b'{}','POST',t['credential'] if token is None else token,key,expected,version,'application/json')
    for field,value in [('size',0),('size',10_000_001),('sha256','bad'),('sha256',hashlib.sha256(PNG).hexdigest()+'\n'),('mimeType','text/html'),('purpose','anything')]:
        body={'name':'upload.png','mimeType':'image/png','size':len(PNG),'sha256':hashlib.sha256(PNG).hexdigest(),'purpose':'LearningResource'};body[field]=value;c.request('/files/upload-tickets',body,expected=422)
    key=str(uuid.uuid4());t=ticket(key=key);assert t['familyVersion']==c.etag.strip('"')
    body={'name':'upload.png','mimeType':'image/png','size':len(PNG),'sha256':hashlib.sha256(PNG).hexdigest(),'purpose':'LearningResource'}
    assert c.request('/files/upload-tickets',body,key=key,expected=201)==t
    command=sql(f'''SELECT "Response" FROM "Commands" WHERE "Key"='{key}' ''');assert t['credential'] not in command and not command.startswith('{')
    assert sql(f'''SELECT "CredentialHash" FROM "FileUploadTicket" WHERE "Id"='{t['fileId']}' ''')==hashlib.sha256(t['credential'].encode()).hexdigest()
    assert c.request('/files/upload-tickets/'+t['fileId'])['status']=='AwaitingBytes'
    raw(c,'/files/'+t['fileId'],expected=404);complete(t,409);stage(t,token='0'*64,expected=403);stage(t,data=PNG+b'x',expected=422)
    before=sql(f'''SELECT "Status" || ':' || octet_length("StagedBytes") FROM "FileUploadTicket" WHERE "Id"='{t['fileId']}' ''');assert before=='AwaitingBytes:0'
    byte_key=str(uuid.uuid4());receipt=stage(t,key=byte_key);assert receipt['familyVersion']==c.etag.strip('"') and receipt['bytesUploaded']==len(PNG)
    assert stage(t,key=byte_key)==receipt;raw(c,'/files/uploads/'+t['fileId'],PNG,'PUT',t['credential'],byte_key,415,mime='text/plain');stage(t,data=PNG[:-1]+b'x',key=byte_key,expected=409)
    raw(c,'/files/'+t['fileId'],expected=404)
    # Same-length non-UTF8 byte differences retain distinct command hashes.
    mutated=bytearray(PNG);mutated[9]^=1;stage(t,bytes(mutated),expected=422)
    archive=zipfile.ZipFile(io.BytesIO(raw(c,'/family/export')));manifest=json.loads(archive.read('manifest.json'));pending=next(x for x in manifest['data']['FileUploadTicket'] if x['id']==t['fileId']);assert 'credentialHash' not in pending and 'stagedBytes' not in pending and t['credential'] not in json.dumps(manifest)
    completion_key=str(uuid.uuid4());file=complete(t,key=completion_key);assert file['id']==t['fileId'] and file['hash']==hashlib.sha256(PNG).hexdigest() and file['fileSnapshotHash'] and file['familyVersion']==c.etag.strip('"')
    assert complete(t,key=completion_key)==file and raw(c,'/files/'+file['id'])==PNG
    assert stage(t,key=byte_key)==receipt;stage(t,expected=409)
    assert sql(f'''SELECT COUNT(*) FROM "PrivateFile" WHERE "Id"='{t['fileId']}' ''')=='1'
    assert sql(f'''SELECT octet_length("StagedBytes") FROM "FileUploadTicket" WHERE "Id"='{t['fileId']}' ''')=='0'
    status=c.request('/files/upload-tickets/'+t['fileId']);assert status['status']=='Completed' and status['completedFileId']==file['id']
    # Unrelated writes cannot silently advance a ticket pipeline's captured version.
    conflict=ticket();expected='"'+conflict['familyVersion']+'"';c.request('/students',{'name':'上传冲突事实'},expected=201);stage(conflict,expected=412,version=expected)
    # Create another authorized family member: opaque credential alone does not grant access.
    name='upload-member-'+uuid.uuid4().hex;password='member-'+uuid.uuid4().hex;member=c.request('/family/members',{'userName':name,'password':password,'roles':['Parent','ContentEditor']},expected=201)
    other=Client();other.request('/auth/login',{'userName':name,'password':password});other.request('/me');other.request('/files/upload-tickets/'+t['fileId'],expected=404);stage(conflict,who=other,expected=404);complete(conflict,who=other,expected=404)
    other.request('/me');member_key=str(uuid.uuid4());issued_by_member=other.request('/files/upload-tickets',body,key=member_key,expected=201)
    c.request('/me');c.request('/family/members/'+member['id']+'/roles',{'roles':['Parent'],'reason':'受控撤销上传权限'},method='PUT')
    other.request('/me',expected=401);other.request('/auth/login',{'userName':name,'password':password});other.request('/me');other.request('/files/upload-tickets',body,key=member_key,expected=403);stage(issued_by_member,who=other,expected=403);complete(issued_by_member,who=other,expected=403)
    child=Client()
    for cookie in c.jar:child.jar.set_cookie(copy.copy(cookie))
    sid=c.request('/students',{'name':'上传孩子权限'},expected=201)['id'];child.etag=c.etag;child.request('/students/'+sid+'/child-sessions',{});child.request('/me');child.request('/files/upload-tickets',body,expected=403);stage(conflict,who=child,expected=403);complete(conflict,who=child,expected=403);child.request('/files/upload-tickets/'+t['fileId'],expected=403)
    c.request('/me')
    # Tamper staged bytes after the accepted upload: completion revalidates independently.
    damaged=ticket();stage(damaged)
    try:
        sql(f'''UPDATE "FileUploadTicket" SET "StagedBytes"=decode('{bytes(mutated).hex()}','hex') WHERE "Id"='{damaged['fileId']}' ''');complete(damaged,422)
        assert sql(f'''SELECT COUNT(*) FROM "PrivateFile" WHERE "Id"='{damaged['fileId']}' ''')=='0'
    finally:sql(f'''UPDATE "FileUploadTicket" SET "StagedBytes"=decode('{PNG.hex()}','hex') WHERE "Id"='{damaged['fileId']}' ''')
    expired=ticket();expired_key=str(uuid.uuid4());stage(expired,key=expired_key);sql(f'''UPDATE "FileUploadTicket" SET "CreatedAt"=NOW()-INTERVAL '20 minutes',"ExpiresAt"=NOW()-INTERVAL '5 minutes' WHERE "Id"='{expired['fileId']}' ''');stage(expired,expected=410,key=expired_key)
    for _ in range(200):
        state=sql(f'''SELECT "Status" || ':' || octet_length("StagedBytes") FROM "FileUploadTicket" WHERE "Id"='{expired['fileId']}' ''')
        if state=='Expired:0':break
        time.sleep(.1)
    else:raise AssertionError('Actual upload cleanup did not expire staged bytes')
    c.request('/me');complete(expired,410);assert c.request('/files/upload-tickets/'+expired['fileId'])['status']=='Expired';raw(c,'/files/'+expired['fileId'],expected=404)
    verify_restore(env,damaged,t)
    if os.environ.get('UPLOAD_TICKET_BROWSER')=='1':
        browser_env=env|{'LEARNING_TEST_URL':api_acceptance.BASE.rsplit('/api/v1',1)[0]};subprocess.run(['npm','run','test:e2e','--','tests/upload-tickets.spec.ts','tests/resource-files.spec.ts','--workers=1'],cwd=Path(__file__).resolve().parents[1]/'src/web',env=browser_env,check=True)
    c.request('/me')
    print('PASS actual restricted upload declarations/secret-protected cache, own account/child guards, no file before completion, exact byte hashes and retry receipts, captured conflict, completion corruption recheck, one durable file and cleared staging, family export excludes staged bytes/credential, actual expiry worker clears blob and expired token410; legacy uploads retained')


def verify_restore(env,pending,completed):
    root=Path(__file__).resolve().parents[1];database='learning_restore_upload_'+uuid.uuid4().hex[:12]
    def sql(query):return subprocess.check_output(['psql','-X','-At','-v','ON_ERROR_STOP=1','-c',query],env=env|{'PGDATABASE':database},text=True).strip()
    with tempfile.TemporaryDirectory(prefix='upload-restore-',dir='/private/tmp') as directory:
        tmp=Path(directory);(tmp/'gpg').mkdir(mode=0o700);students=tmp/'students';families=tmp/'families';students.touch(mode=0o600);families.touch(mode=0o600)
        backup_env=env|{'PATH':'/opt/homebrew/bin:'+env['PATH'],'BACKUP_DIR':str(tmp/'archives'),'BACKUP_PASSPHRASE':secrets.token_hex(32),'GNUPGHOME':str(tmp/'gpg'),'DELETION_LEDGER':str(students),'FAMILY_DELETION_LEDGER':str(families)}
        created=False
        try:
            subprocess.run(['sh',str(root/'scripts/backup.sh')],env=backup_env,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);archive=next((tmp/'archives').glob('*.gpg'))
            subprocess.run(['createdb',database],env=env,check=True,stdout=subprocess.DEVNULL);created=True
            subprocess.run(['sh',str(root/'scripts/restore.sh'),str(archive)],env=backup_env|{'PGDATABASE':database},check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            assert sql(f'''SELECT "Status" || ':' || octet_length("StagedBytes") FROM "FileUploadTicket" WHERE "Id"='{pending['fileId']}' ''')=='Expired:0'
            assert sql(f'''SELECT COUNT(*) FROM "PrivateFile" WHERE "Id"='{pending['fileId']}' ''')=='0'
            assert sql(f'''SELECT "Status" || ':' || "CompletedFileId" FROM "FileUploadTicket" WHERE "Id"='{completed['fileId']}' ''')=='Completed:'+completed['fileId']
            assert sql(f'''SELECT encode("Bytes",'hex') FROM "PrivateFile" WHERE "Id"='{completed['fileId']}' ''')==PNG.hex()
            assert sql('SELECT COUNT(*) FROM "Commands"')=='0'
            print('PASS actual encrypted restore expires pending upload bytes/credentials, preserves completed files exactly, clears command credentials; same-disk drill only')
        finally:
            if created:subprocess.run(['dropdb','--if-exists','--force',database],env=env,check=True,stdout=subprocess.DEVNULL)
            subprocess.run(['gpgconf','--kill','gpg-agent'],env=backup_env,check=True,stdout=subprocess.DEVNULL)
