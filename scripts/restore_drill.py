#!/usr/bin/env python3
"""Restore the real encrypted snapshot to a disposable database and verify every retained COPY row."""
import argparse
from collections import Counter
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import time
import uuid
import daily_backup as backup


def identifier(name):
    if not re.fullmatch('[A-Za-z0-9_]+',name):raise backup.BackupError('UNKNOWN_ARCHIVE_IDENTIFIER')
    return '"'+name+'"'


def read_copy(path):
    tables={};table=None
    with Path(path).open() as source:
        for line in source:
            if table is not None:
                if line=='\\.\n':table=None;continue
                values=line.rstrip('\n').split('\t')
                if len(values)!=len(tables[table]['columns']):raise backup.BackupError('COPY_SHAPE_INVALID')
                tables[table]['rows'].append(dict(zip(tables[table]['columns'],values)))
            elif line.startswith('COPY '):
                match=re.fullmatch(r'COPY public\."([A-Za-z0-9_]+)" \((.+)\) FROM stdin;\n',line)
                if not match:raise backup.BackupError('COPY_HEADER_UNSUPPORTED')
                table=match[1];columns=[value.strip('"') for value in match[2].split(', ')]
                for column in columns:identifier(column)
                if table in tables:raise backup.BackupError('COPY_DUPLICATE_TABLE')
                tables[table]={'columns':columns,'rows':[]}
    if table is not None:raise backup.BackupError('COPY_TRUNCATED')
    return tables


def ledger(path):
    result=[]
    for line in backup.private_file(path).read_text().splitlines():
        if not line:continue
        fields=line.split(',')
        if len(fields)!=2:raise backup.BackupError('DELETION_LEDGER_INVALID')
        result.append(tuple(str(uuid.UUID(value)) for value in fields))
    return result


def expected_rows(tables,families,students,foreign_keys):
    removed={name:set() for name in tables}
    deleted_families={family for family,_ in families};deleted_students=set(students)
    for name,value in tables.items():
        for index,row in enumerate(value['rows']):
            if name in {'AuthSessions','Commands'} or (name=='Families' and row['Id'] in deleted_families) or (name=='Students' and (row['FamilyId'],row['Id']) in deleted_students):removed[name].add(index)
    # Student-owned images have no student FK. Keep any image still used by another student or a source.
    for index,file in enumerate(tables['PrivateFile']['rows']):
        linked=[r for r in tables['PaperWrong']['rows'] if r['FileId']==file['Id']]
        affected=any((r['FamilyId'],r['StudentId']) in deleted_students for r in linked)
        remaining=any((r['FamilyId'],r['StudentId']) not in deleted_students for r in linked)
        source=any(r['FamilyId']==file['FamilyId'] and r['Hash']==file['Hash'] for r in tables['Sources']['rows'])
        if affected and not remaining and not source:removed['PrivateFile'].add(index)
    # Compute deletion closure from the actual restored schema, rather than maintaining a stale table allowlist.
    changed=True
    while changed:
        changed=False
        for fk in foreign_keys:
            if fk['delete']!='c':continue
            parent,child=fk['parent'],fk['child'];deleted={tuple(tables[parent]['rows'][i][column] for column in fk['parentColumns']) for i in removed[parent]}
            for i,row in enumerate(tables[child]['rows']):
                key=tuple(row[column] for column in fk['childColumns'])
                if i not in removed[child] and '\\N' not in key and key in deleted:removed[child].add(i);changed=True
    return {name:[row for i,row in enumerate(value['rows']) if i not in removed[name]] for name,value in tables.items()}


def execute(config_path):
    start=time.monotonic();config=backup.load_config(config_path);state=json.loads(backup.private_file(config['stateFile']).read_text());last=state.get('lastSuccess')
    if not isinstance(last,dict) or not last.get('archiveVerified'):raise backup.BackupError('NO_VERIFIED_ARCHIVE')
    name=last['archive']
    if not backup.NAME.fullmatch(name):raise backup.BackupError('ARCHIVE_NAME_INVALID')
    archive=backup.private_file(Path(config['backupDir'])/name)
    if backup.digest(archive)!=last['sha256']:raise backup.BackupError('ARCHIVE_DIGEST_MISMATCH')
    ledger_paths=[config['deletionLedger'],config['familyDeletionLedger']];ledger_hashes=[backup.digest(backup.private_file(path)) for path in ledger_paths]
    students,families=ledger(ledger_paths[0]),ledger(ledger_paths[1]);secret=backup.private_file(config['passphraseFile']).read_text().strip()
    env=os.environ.copy();pg=config['postgres'];env.update(PGHOST=pg['host'],PGPORT=str(pg['port']),PGUSER=pg['user'])
    target='learning_restore_drill_'+uuid.uuid4().hex[:12];created=False
    def run(args,extra=None):return subprocess.run(args,env=env|(extra or {}),check=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE).stdout
    def sql(query):return run(['psql','-At','-v','ON_ERROR_STOP=1','-c',query],{'PGDATABASE':target}).decode().strip()
    with tempfile.TemporaryDirectory(prefix='learning-drill-',dir='/private/tmp') as tmp:
        tmp=Path(tmp);gpg=tmp/'gpg';gpg.mkdir(mode=0o700);env['GNUPGHOME']=str(gpg)
        try:
            plain=tmp/'archive.pgdump';data=tmp/'archive.sql'
            subprocess.run(['gpg','--batch','--yes','--pinentry-mode','loopback','--passphrase-fd','0','--decrypt','--output',str(plain),str(archive)],input=secret.encode(),env=env,check=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            run(['pg_restore','--data-only','--no-owner','--file='+str(data),str(plain)])
            tables=read_copy(data)
            if len(tables)!=last['tableCount']:raise backup.BackupError('ARCHIVE_TABLE_COUNT_DIFFERS')
            run(['createdb',target]);created=True
            run(['sh',str(Path(__file__).resolve().parent/'restore.sh'),str(archive)],{'PGDATABASE':target,'BACKUP_PASSPHRASE':secret,'DELETION_LEDGER':ledger_paths[0],'FAMILY_DELETION_LEDGER':ledger_paths[1]})
            if sql("SELECT count(*) FROM pg_constraint WHERE NOT convalidated")!='0':raise backup.BackupError('RESTORED_CONSTRAINT_UNVALIDATED')
            fk_sql="""SELECT COALESCE(jsonb_agg(jsonb_build_object('child',ch.relname,'parent',pa.relname,'delete',c.confdeltype,'childColumns',(SELECT jsonb_agg(a.attname ORDER BY k.n) FROM unnest(c.conkey) WITH ORDINALITY k(v,n) JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.v),'parentColumns',(SELECT jsonb_agg(a.attname ORDER BY k.n) FROM unnest(c.confkey) WITH ORDINALITY k(v,n) JOIN pg_attribute a ON a.attrelid=c.confrelid AND a.attnum=k.v))),'[]'::jsonb) FROM pg_constraint c JOIN pg_class ch ON ch.oid=c.conrelid JOIN pg_class pa ON pa.oid=c.confrelid JOIN pg_namespace ns ON ns.oid=ch.relnamespace WHERE c.contype='f' AND ns.nspname='public'"""
            expected=expected_rows(tables,families,students,json.loads(sql(fk_sql)))
            compared=0
            for table,value in tables.items():
                columns=value['columns'];query='COPY (SELECT '+','.join(identifier(c) for c in columns)+' FROM '+identifier(table)+') TO STDOUT'
                actual_text=run(['psql','-q','-v','ON_ERROR_STOP=1','-c',query],{'PGDATABASE':target}).decode()
                actual=actual_text.split('\n')[:-1] if actual_text else []
                wanted=['\t'.join(row[c] for c in columns) for row in expected[table]]
                if Counter(actual)!=Counter(wanted):raise backup.BackupError('RESTORED_ROWS_DIFFER_'+table.upper())
                compared+=len(wanted)
            if ledger_hashes!=[backup.digest(path) for path in ledger_paths]:raise backup.BackupError('DELETION_LEDGER_CHANGED_REPEAT_DRILL')
            result={'format':'learning-restore-drill/1','status':'Passed','checkedAt':backup.stamp(backup.utc()),'archive':name,'archiveSha256':last['sha256'],
                    'snapshotStartedAt':last['snapshotStartedAt'],'seconds':round(time.monotonic()-start,2),'tableCount':len(tables),'retainedRowsCompared':compared,
                    'allRetainedColumnsEqual':True,'constraintsValidated':True,'sessionsAndCachesCleared':True,'latestDeletionLedgersApplied':True,
                    'independentDiskVerified':False,'sustainedRpoVerified':False,'method':'real full snapshot; COPY multiset equality after FK cascade and private image ownership deletion closure',
                    'notice':'本机同盘、临时空库演练；不切换原应用，不证明独立磁盘、持续RPO或正式灾难切换RTO。'}
            backup.write_json(Path(config['stateFile']).with_suffix('.restore-drill.json'),result)
            return result
        finally:
            if created:run(['dropdb','--force',target])
            subprocess.run(['gpgconf','--kill','gpg-agent'],env=env,check=False,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)


def main():
    os.umask(0o077);parser=argparse.ArgumentParser();parser.add_argument('--config',required=True);args=parser.parse_args()
    try:
        result=execute(args.config);print(json.dumps({key:result[key] for key in ['status','seconds','tableCount','retainedRowsCompared','allRetainedColumnsEqual','latestDeletionLedgersApplied']},ensure_ascii=False))
    except (backup.BackupError,OSError,ValueError,TypeError,KeyError,subprocess.CalledProcessError) as error:
        print(json.dumps({'status':'Failed','errorCode':str(error) if isinstance(error,backup.BackupError) else 'RESTORE_DRILL_FAILED'}));raise SystemExit(1)


if __name__=='__main__':main()
