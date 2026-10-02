"""Gold expectations for actual archive comparison, deletion closure, and corruption refusal."""
import getpass,json,os,secrets,subprocess,sys,tempfile,uuid
from pathlib import Path

def main():
    root=Path(__file__).resolve().parent.parent;sys.path.insert(0,str(root/'scripts'))
    import daily_backup as backup,restore_drill
    env=os.environ.copy();database='learning_fault_'+uuid.uuid4().hex[:12]
    env.update(PGHOST='127.0.0.1',PGPORT='55432',PGUSER=getpass.getuser(),PGDATABASE=database,
               PLAN_TEST_USERNAME='drill-fixture-'+uuid.uuid4().hex[:12],PLAN_TEST_PASSWORD=secrets.token_hex(20),
               PERSISTENCE_TEST_CONNECTION=f'Host=127.0.0.1;Port=55432;Database={database};Username={getpass.getuser()}')
    env['PATH']='/opt/homebrew/opt/postgresql@16/bin:/opt/homebrew/bin:'+env['PATH'];os.environ['PATH']=env['PATH']
    subprocess.run(['createdb',database],env=env,check=True)
    with tempfile.TemporaryDirectory(prefix='learning-drill-test-',dir='/private/tmp') as tmp:
        tmp=Path(tmp)
        try:
            fixture=json.loads(subprocess.check_output([str(root/'.tools/dotnet/dotnet'),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'plan-boundary-seed'],env=env,text=True).strip().splitlines()[-1])
            key=tmp/'key';key.write_text(secrets.token_hex(32));key.chmod(0o600)
            for name in ['students.txt','families.txt']:(tmp/name).touch(mode=0o600)
            config={'backupDir':str(tmp/'archives'),'stateFile':str(tmp/'state.json'),'passphraseFile':str(key),'dataDir':str(tmp),
                    'deletionLedger':str(tmp/'students.txt'),'familyDeletionLedger':str(tmp/'families.txt'),
                    'postgres':{'host':'127.0.0.1','port':55432,'user':getpass.getuser(),'database':database}}
            configfile=tmp/'config.json';configfile.write_text(json.dumps(config));configfile.chmod(0o600)
            subprocess.run(['psql','-v','ON_ERROR_STOP=1','-c','''UPDATE "Attempts" SET "Answer"=E'列内换行\\n制表\\t反斜杠\\\\与NEL\u0085末尾' '''],env=env,check=True,stdout=subprocess.DEVNULL)
            burden1=str(uuid.uuid4());burden2=str(uuid.uuid4());actor=str(uuid.uuid4());family_id=fixture['familyId'];student_id=fixture['studentId']
            burden_sql=f'''INSERT INTO "ParentBurdenRecord" ("Id","FamilyId","StudentId","RecordedBy","Date","Category","Minutes","Note","SupersedesId","CorrectionReason","Method","CreatedAt") VALUES
                ('{burden1}','{family_id}','{student_id}','{actor}',CURRENT_DATE,'Daily',7.5,E'核对作业\\n原始说明',NULL,'','ParentReported',CURRENT_TIMESTAMP),
                ('{burden2}','{family_id}','{student_id}','{actor}',CURRENT_DATE,'Daily',3,'更正说明','{burden1}','扣除休息时间','ParentReported',CURRENT_TIMESTAMP)'''
            subprocess.run(['psql','-v','ON_ERROR_STOP=1','-c',burden_sql],env=env,check=True,stdout=subprocess.DEVNULL)
            created=backup.execute(backup.load_config(configfile));result=restore_drill.execute(configfile)
            assert result['status']=='Passed' and result['allRetainedColumnsEqual'] and result['retainedRowsCompared']>10 and result['tableCount']>=30
            assert not result['independentDiskVerified'] and not result['sustainedRpoVerified']
            print('PASS 真实归档逐列比较所有表、证据与复习；家长投入原记录/更正链及COPY内换行/制表/反斜杠/NEL保持原样',flush=True)
            (tmp/'students.txt').write_text(fixture['familyId']+','+fixture['studentId']+'\n')
            deleted=restore_drill.execute(configfile);assert deleted['retainedRowsCompared']<result['retainedRowsCompared']
            (tmp/'families.txt').write_text(fixture['familyId']+','+str(uuid.uuid4())+'\n')
            family=restore_drill.execute(configfile);assert family['retainedRowsCompared']<deleted['retainedRowsCompared'] and family['latestDeletionLedgersApplied']
            print('PASS 学生及整家最新删除清单的真实恢复，与独立FK删除闭包预期一致',flush=True)
            archive=Path(created['archive']);raw=bytearray(archive.read_bytes());raw[-1]^=1;archive.write_bytes(raw)
            try:restore_drill.execute(configfile);raise AssertionError('corrupt archive accepted')
            except backup.BackupError as error:assert str(error)=='ARCHIVE_DIGEST_MISMATCH'
            print('PASS 归档损坏在建恢复库前拒绝；不以旧成功记录证明新损坏归档可恢复',flush=True)
        finally:subprocess.run(['dropdb','--force',database],env=env,check=True)

if __name__=='__main__':main()
