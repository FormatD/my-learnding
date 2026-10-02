"""An approved family deletion stays deleted after restoring an older encrypted backup."""
import os,secrets,subprocess,uuid
from pathlib import Path
from api_acceptance import Client

def main():
    root=Path(__file__).resolve().parent.parent;suffix=uuid.uuid4().hex[:12];password=secrets.token_hex(20);c=Client()
    c.request('/auth/register',{'userName':'family-restore-'+suffix,'password':password},expected=201);identity=c.request('/me');fid=identity['family']['id']
    c.request('/students',{'name':'恢复删除测试'},expected=201);c.request('/content/fixture',{})
    env=os.environ.copy();env.update(PGHOST='127.0.0.1',PGPORT='55432',PGUSER='qianjundeng',PGDATABASE='learning',BACKUP_PASSPHRASE=secrets.token_hex(32),GNUPGHOME='/private/tmp/family-gpg-'+suffix,DELETION_LEDGER=str(root/'.local/deleted-students.txt'),FAMILY_DELETION_LEDGER=str(root/'.local/deleted-families.txt'))
    Path(env['GNUPGHOME']).mkdir(mode=0o700);env['PATH']='/opt/homebrew/opt/postgresql@16/bin:/opt/homebrew/bin:'+env['PATH']
    output=subprocess.check_output(['sh','scripts/backup.sh'],env=env,cwd=root,text=True);backup=output.strip().split('Backup saved: ')[-1]
    preview=c.request('/family/delete-preview');c.request('/family:delete',{'previewHash':preview['previewHash'],'password':password,'confirm':'永久删除家庭'})
    restore='learning_restore_'+suffix;subprocess.run(['createdb',restore],env=env,check=True);env['PGDATABASE']=restore
    try:
        subprocess.run(['sh','scripts/restore.sh',backup],env=env,cwd=root,check=True,stdout=subprocess.DEVNULL)
        def sql(query):return subprocess.check_output(['psql','-Atc',query],env=env,text=True).strip()
        assert sql(f'''SELECT count(*) FROM "Families" WHERE "Id"='{fid}' ''')=='0'
        tables=sql('''SELECT table_name FROM information_schema.columns WHERE table_schema='public' AND column_name='FamilyId' ''').splitlines()
        for table in tables:assert sql(f'''SELECT count(*) FROM "{table}" WHERE "FamilyId"='{fid}' ''')=='0',table
        assert int(sql('SELECT count(*) FROM "Families"'))>0
        print('PASS 恢复旧加密备份后家庭、成员及全部私有业务表无删除家庭数据；其他家庭保留')
        env['FAMILY_DELETION_LEDGER']=str(root/('.local/missing-family-ledger-'+suffix))
        result=subprocess.run(['sh','scripts/restore.sh',backup],env=env,cwd=root,capture_output=True,text=True)
        assert result.returncode!=0 and 'family deletion ledger' in result.stderr.lower()
        print('PASS 独立家庭删除清单缺失时拒绝恢复，不绕过删除请求')
    finally:subprocess.run(['dropdb',restore],env=env,check=True)
if __name__=='__main__':main()
