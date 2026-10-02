"""Operational backlog, failure metrics and recovery, on an isolated database and API."""
import getpass,json,os,secrets,socket,subprocess,tempfile,time,urllib.request,uuid
from pathlib import Path
import api_acceptance
from api_acceptance import Client

def main():
    root=Path(__file__).resolve().parent.parent;env=os.environ.copy();suffix=uuid.uuid4().hex[:12];database='learning_fault_'+suffix
    env.update(PGHOST='127.0.0.1',PGPORT='55432',PGUSER=getpass.getuser(),PGDATABASE=database,PLAN_TEST_USERNAME='plan-fixture-'+suffix,PLAN_TEST_PASSWORD=secrets.token_hex(20))
    env['PATH']='/opt/homebrew/opt/postgresql@16/bin:'+env['PATH'];connection=f'Host=127.0.0.1;Port=55432;Database={database};Username={env["PGUSER"]}';env['PERSISTENCE_TEST_CONNECTION']=connection;env['ConnectionStrings__Learning']=connection
    dotnet=str(root/'.tools/dotnet/dotnet');child=None
    subprocess.run(['createdb',database],env=env,check=True)
    with tempfile.TemporaryDirectory(prefix='learning-operations-') as tmp:
        env.update(DeletionLedger=str(Path(tmp)/'deleted-students.txt'),FamilyDeletionLedger=str(Path(tmp)/'deleted-families.txt'),ExportDirectory=str(Path(tmp)/'exports'))
        try:
            output=subprocess.check_output([dotnet,str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'plan-boundary-seed'],env=env,text=True);fixture=json.loads(output.strip().splitlines()[-1])
            with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
            with open(Path(tmp)/'api.log','w+') as log:
                child=subprocess.Popen([dotnet,str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',f'http://127.0.0.1:{port}'],cwd=root/'src/server',env=env,stdout=log,stderr=subprocess.STDOUT)
                deadline=time.monotonic()+30
                while time.monotonic()<deadline:
                    if child.poll() is not None:log.seek(0);raise AssertionError('isolated API stopped: '+log.read()[-1500:])
                    try:
                        with urllib.request.urlopen(f'http://127.0.0.1:{port}/api/health',timeout=1) as response:
                            if response.status==200:break
                    except OSError:time.sleep(.1)
                else:raise AssertionError('isolated API did not become ready')
                api_acceptance.BASE=f'http://127.0.0.1:{port}/api/v1';c=Client();credentials={'userName':env['PLAN_TEST_USERNAME'],'password':env['PLAN_TEST_PASSWORD']};c.request('/auth/login',credentials);c.request('/me')
                sid=fixture['studentId'];fid=fixture['familyId'];baseline=c.request('/operations');assert baseline['projection']['pending']==0 and baseline['requests']['errorRate']==0
                assert not baseline['backup']['verified'] and not baseline['model']['connected'] and baseline['storage']['available']
                original_payload=next(r['payload'] for r in c.request('/content')['releases'] if r['id']==fixture['releaseId']);evidence=c.request('/students/'+sid+'/mastery')
                def sql(query):return subprocess.check_output(['psql','-At','-v','ON_ERROR_STOP=1','-c',query],env=env,text=True)
                sql(f'''UPDATE "Outbox" SET "ProcessedAt"=NULL,"Retries"=3,"Error"='PROJECTION_FAILED',"CreatedAt"=NOW()-interval '45 seconds' WHERE "FamilyId"='{fid}';''')
                delayed=c.request('/operations');codes={s['code'] for s in delayed['signals']};assert {'ProjectionDelayed','ProjectionFailed'}<=codes and delayed['projection']['failed']==1 and delayed['projection']['oldestSeconds']>=45
                job=delayed['projection']['jobs'][0];assert job['student']=='Due review fixture' and job['canRetry']
                deadline=time.monotonic()+12
                while time.monotonic()<deadline:
                    log.flush();log.seek(0);logs=log.read()
                    if 'Operational alert ProjectionDelayed' in logs and 'Operational alert ProjectionFailed' in logs:break
                    time.sleep(.2)
                else:raise AssertionError('independent monitor did not emit backlog alerts')
                print('PASS 30秒结果积压和重试耗尽分别提示，独立监控自动写日志，未重新提交答案')
                sql(f'''UPDATE "Releases" SET "Payload"='invalid-json' WHERE "Id"='{fixture['releaseId']}' AND "FamilyId"='{fid}';''');failure=c.request('/students/'+sid+'/catalog',expected=500);assert failure['code']=='SERVER_ERROR' and failure['traceId']
                restored_payload=original_payload.replace("'","''");sql(f'''UPDATE "Releases" SET "Payload"='{restored_payload}' WHERE "Id"='{fixture['releaseId']}';''');observed=c.request('/operations');assert observed['requests']['serverErrors']==1 and observed['requests']['errorRate']>0
                print('PASS 家庭接口服务异常计数含真实500，响应有追踪编号、不暴露内部堆栈')
                source=c.request('/content/sources',{'title':'建库运行监控验收','text':'先乘除后加减，按独立步骤核对。'},expected=201);run=c.request('/builder/runs',{'sourceId':source['id']},expected=202)
                for _ in range(60):
                    if any(r['id']==run['id'] and r['status']=='Completed' for r in c.request('/builder')['runs']):break
                    time.sleep(.1)
                else:raise AssertionError('builder did not settle')
                sql(f'''UPDATE "BuilderRuns" SET "Status"='Failed',"Error"='INPUT_SNAPSHOT_UNKNOWN' WHERE "Id"='{run['id']}' AND "FamilyId"='{fid}';''');state=c.request('/operations');assert state['builder']['failed']==1 and any(s['code']=='BuilderFailed' for s in state['signals'])
                other=Client();other.request('/auth/register',{'userName':'ops-other-'+suffix,'password':env['PLAN_TEST_PASSWORD']},expected=201);other.request('/me');empty=other.request('/operations');assert empty['projection']['pending']==0 and empty['builder']['failed']==0 and empty['requests']['serverErrors']==0;other.request('/jobs/'+job['id']+':retry',{},expected=404)
                c.request('/me');c.request('/family/members',{'userName':'ops-parent-'+suffix,'password':env['PLAN_TEST_PASSWORD'],'roles':['Parent']},expected=201);parent=Client();parent.request('/auth/login',{'userName':'ops-parent-'+suffix,'password':env['PLAN_TEST_PASSWORD']});parent.request('/me');restricted=parent.request('/operations');assert restricted['builder'] is None and restricted['storage'] is None and not any(s['code'].startswith('Builder') for s in restricted['signals'])
                print('PASS 建库失败与未审核统计按内容权限显示，家庭请求计数/结果任务隔离，非负责人不读取存储容量')
                c.request('/me');c.request('/jobs/'+job['id']+':retry',{},expected=202)
                for _ in range(100):
                    state=c.request('/operations')
                    if state['projection']['pending']==0:break
                    time.sleep(.1)
                else:raise AssertionError('manual retry did not settle')
                assert not any(s['code'].startswith('Projection') for s in state['signals']) and c.request('/students/'+sid+'/mastery')==evidence
                c.request('/jobs/'+job['id']+':retry',{},expected=409)
                deadline=time.monotonic()+12
                while time.monotonic()<deadline:
                    log.flush();log.seek(0)
                    if 'Operational alert resolved ProjectionFailed' in log.read():break
                    time.sleep(.2)
                else:raise AssertionError('resolved backlog not logged')
                c.request('/students/'+sid+'/child-sessions',{});c.request('/operations',expected=403);c.request('/jobs/'+job['id']+':retry',{},expected=403)
                print('PASS 人工重试沿用原记录且无重复证据，已完成不再重试，恢复自动记日志，孩子不可读或操作')
        finally:
            if child is not None and child.poll() is None:child.terminate();child.wait(timeout=10)
            subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
