"""Encrypt, restore to an isolated database, reapply a real deletion ledger."""
import base64
import json
import os
from pathlib import Path
import secrets
import subprocess
import time
import uuid
from api_acceptance import Client

def main():
    start=time.monotonic()
    root=Path(__file__).resolve().parent.parent
    c=Client();suffix=uuid.uuid4().hex[:12];password=secrets.token_hex(20)
    c.request('/auth/register',{'userName':'restore-'+suffix,'password':password},expected=201)
    c.request('/me')
    s=c.request('/students',{'name':'删除恢复验收'},expected=201)
    d=c.request('/content/fixture',{});c.request('/content/drafts/'+d['id']+':review',{})
    p=c.request('/content/drafts/'+d['id']+'/preview');r=c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']})
    png='iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aS1cAAAAASUVORK5CYII='
    f=c.request('/files',{'name':'delete-test.png','mimeType':'image/png','base64':png},expected=201)
    c.request('/students/'+s['id']+'/paper-wrongs',{'stem':'私有删除验证','answer':'错误','fileId':f['id']},expected=201)
    env=os.environ.copy();env.update(PGHOST='127.0.0.1',PGPORT='55432',PGUSER=os.environ.get('USER','qianjundeng'),PGDATABASE='learning',BACKUP_PASSPHRASE=secrets.token_hex(32),GNUPGHOME='/private/tmp/learning-gpg-'+suffix,DELETION_LEDGER=str(root/'.local/deleted-students.txt'))
    Path(env['GNUPGHOME']).mkdir(mode=0o700,parents=True,exist_ok=True)
    env['PATH']='/opt/homebrew/opt/postgresql@16/bin:/opt/homebrew/bin:'+env['PATH']
    output=subprocess.check_output(['sh','scripts/backup.sh'],env=env,cwd=root,text=True)
    backup=output.strip().split('Backup saved: ')[-1]
    before=c.request('/students/'+s['id']+'/delete-preview')
    c.request('/students/'+s['id']+':delete',{'previewHash':before['previewHash'],'password':password,'confirm':'永久删除学生'})
    restore='learning_restore_'+suffix
    subprocess.run(['createdb',restore],env=env,check=True)
    env['PGDATABASE']=restore
    try:
        subprocess.run(['sh','scripts/restore.sh',backup],env=env,cwd=root,check=True,stdout=subprocess.DEVNULL)
        def sql(text):return subprocess.check_output(['psql','-Atc',text],env=env,text=True).strip()
        assert sql(f'''SELECT COUNT(*) FROM "Students" WHERE "Id"='{s['id']}' ''')=='0'
        assert sql(f'''SELECT COUNT(*) FROM "PrivateFile" WHERE "Id"='{f['id']}' ''')=='0'
        assert sql(f'''SELECT COUNT(*) FROM "PaperWrong" WHERE "StudentId"='{s['id']}' ''')=='0'
        assert sql(f'''SELECT COUNT(*) FROM "Commands" WHERE "FamilyId"='{s['familyId']}' ''')=='0'
        assert sql(f'''SELECT "Hash" FROM "Releases" WHERE "Id"='{r['id']}' ''')==r['hash']
        report={'case':'AT40','status':'passed','checks':['删除学生不会复活','关联私有图片已删除','纸质错题已删除','旧响应缓存已清理','家庭发布内容保持完整'],'seconds':round(time.monotonic()-start,2),'method':'PostgreSQL custom-format + GPG AES256 + independent deletion ledger'}
        (root/'.local/restore-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
        print(json.dumps(report,ensure_ascii=False))
    finally:
        subprocess.run(['dropdb',restore],env=env,check=True)
        Path(root/backup).unlink(missing_ok=True)

if __name__=='__main__':main()
