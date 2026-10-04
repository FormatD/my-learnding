"""Stable actual PostgreSQL task history; terminal fixture rows are not executed jobs."""
import base64,copy,hashlib,json,os,subprocess,uuid
from urllib.parse import quote
from pathlib import Path
import api_acceptance

def verify(c,env,credentials):
    if not env.get('PGDATABASE','').startswith('learning_fault_'):raise ValueError('Disposable learning_fault_ database required')
    family=c.request('/me')['family']['id'];ids=sorted(str(uuid.uuid4()) for _ in range(55));kind='CursorFixture';payload='{}';digest=hashlib.sha256(payload.encode()).hexdigest()
    def sql(statement):return subprocess.check_output(['psql','-X','-v','ON_ERROR_STOP=1','-Atc',statement],env=env,text=True)
    def insert(id,stamp):
        sql(f'''INSERT INTO "BackgroundJob" ("Id","FamilyId","CreatedAt","Type","InputRef","IdempotencyKey","InputPayload","InputHash","Status","AttemptCount","MaxAttempts","RetryRound","LeaseSeconds","HeartbeatSeconds") VALUES ('{id}','{family}','{stamp}','{kind}','{id}','{id}','{{}}','{digest}','Cancelled',0,4,0,30,5)''')
    try:
        for id in ids:insert(id,'2000-01-01T00:00:00Z')
        url='/background-jobs/window?pageSize=20';first=c.request(url);assert len(first['jobs'])==20 and first['nextCursor']
        newcomer=str(uuid.uuid4());insert(newcomer,'2100-01-01T00:00:00Z')
        seen=[];page=first
        while True:
            page_ids={j['id'] for j in page['jobs']};assert all(a['jobId'] in page_ids and a['familyId']==family for a in page['attempts']);seen.extend(j['id'] for j in page['jobs'])
            if not page['nextCursor']:break
            page=c.request(url+'&cursor='+quote(page['nextCursor'],safe=''))
        assert len(seen)==len(set(seen)) and newcomer not in seen and [id for id in seen if id in ids]==ids
        assert c.request(url)['jobs'][0]['id']==newcomer
        token=quote(first['nextCursor'],safe='');c.request('/background-jobs/window?pageSize=19&cursor='+token,expected=422)
        for field,value in [('version',99),('scope','builder-calls/1'),('familyId',str(uuid.uuid4())),('filterId',str(uuid.uuid4()))]:
            decoded=json.loads(base64.b64decode(first['nextCursor']));decoded[field]=value;bad=quote(base64.b64encode(json.dumps(decoded).encode()).decode(),safe='');c.request(url+'&cursor='+bad,expected=422)
        c.request(url+'&cursor=broken',expected=422);c.request(url+'&cursor='+('A'*1025),expected=422);c.request('/background-jobs/window?pageSize=51',expected=422)
        assert c.request('/background-jobs?pageSize=20')['page']==1
        if os.environ.get('BACKGROUND_CURSOR_BROWSER')=='1':
            root=Path(__file__).resolve().parents[1];browser_env=env.copy();browser_env.update(LEARNING_TEST_URL=api_acceptance.BASE.rsplit('/api/v1',1)[0],BACKGROUND_CURSOR_USER=credentials['userName'],BACKGROUND_CURSOR_PASSWORD=credentials['password'])
            subprocess.run(['npm','run','test:e2e','--','tests/background-cursor.spec.ts','--workers=1'],cwd=root/'src/web',env=browser_env,check=True)
        student=c.request('/students',{'name':'后台游标孩子隔离'},expected=201);child=api_acceptance.Client()
        for cookie in c.jar:child.jar.set_cookie(copy.copy(cookie))
        child.etag=c.etag;child.request('/students/'+student['id']+'/child-sessions',{});child.request('/background-jobs/window',expected=403)
        print('PASS actual background cursor: 55 equal-time UUID ties, top insertion, each original fixture once, page-only claims, refresh, scoped invalid cursors/size and child403; legacy page retained; controlled terminal rows are not executed jobs')
    finally:sql(f'''DELETE FROM "BackgroundJob" WHERE "FamilyId"='{family}' AND "Type"='{kind}' ''')
